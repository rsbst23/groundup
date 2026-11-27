import { isOperationResult } from './operationResult';

/** Every failed API call ends as one of these, whatever shape the response had. */
export class ApiError extends Error {
  /** HTTP status, or 0 when the API could not be reached. */
  readonly status: number;
  readonly errorCode: string | null;
  /** Validation and other detail messages, flattened. */
  readonly errors: string[];
  readonly correlationId: string | null;
  /** The parsed response body (or its text), for debugging. */
  readonly body: unknown;

  constructor(init: {
    status: number;
    message: string;
    errorCode?: string | null;
    errors?: string[];
    correlationId?: string | null;
    body?: unknown;
  }) {
    super(init.message);
    this.name = 'ApiError';
    this.status = init.status;
    this.errorCode = init.errorCode ?? null;
    this.errors = init.errors ?? [];
    this.correlationId = init.correlationId ?? null;
    this.body = init.body ?? null;
  }
}

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** ASP.NET Core ProblemDetails: `title` and `status`, optional `detail` and `errors` by field. */
function isProblemDetails(body: JsonObject): boolean {
  return typeof body.title === 'string' && typeof body.status === 'number';
}

function problemDetailsErrors(errors: unknown): string[] {
  if (!isObject(errors)) {
    return [];
  }
  return Object.entries(errors).flatMap(([field, messages]) =>
    (Array.isArray(messages) ? messages : [messages]).map((message) => `${field}: ${String(message)}`),
  );
}

/**
 * Turns a failed response into an ApiError. Reads, in order: OperationResult; the auth
 * controllers' `{ error, errorCode }`; the exception middleware's `{ message, errorCode,
 * correlationId }`; ProblemDetails; anything else gets a plain message from the status.
 * The correlation ID comes from the response header, or from the body when the header is missing.
 */
export function parseErrorBody(
  status: number,
  statusText: string,
  body: unknown,
  headerCorrelationId: string | null,
): ApiError {
  const bodyCorrelationId = isObject(body) ? stringOrNull(body.correlationId) : null;
  const correlationId = headerCorrelationId ?? bodyCorrelationId;
  const fallbackMessage = `HTTP ${status}${statusText ? ` ${statusText}` : ''}`;

  if (isOperationResult(body)) {
    return new ApiError({
      status,
      message: stringOrNull(body.message) ?? fallbackMessage,
      errorCode: body.errorCode ?? null,
      errors: body.errors ?? [],
      correlationId,
      body,
    });
  }

  if (isObject(body)) {
    const error = stringOrNull(body.error);
    if (error) {
      return new ApiError({ status, message: error, errorCode: stringOrNull(body.errorCode), correlationId, body });
    }

    const message = stringOrNull(body.message);
    if (message) {
      return new ApiError({ status, message, errorCode: stringOrNull(body.errorCode), correlationId, body });
    }

    if (isProblemDetails(body)) {
      return new ApiError({
        status,
        message: stringOrNull(body.detail) ?? stringOrNull(body.title) ?? fallbackMessage,
        errors: problemDetailsErrors(body.errors),
        correlationId,
        body,
      });
    }
  }

  return new ApiError({ status, message: fallbackMessage, correlationId, body });
}
