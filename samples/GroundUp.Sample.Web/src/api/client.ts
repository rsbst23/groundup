import { clearCsrfToken, getCsrfToken } from './csrf';
import { ApiError, parseErrorBody } from './errors';
import { recordRequest, recordResponse } from './inspector';
import { unwrap } from './operationResult';

export const CORRELATION_ID_HEADER = 'X-Correlation-Id';
export const CSRF_HEADER = 'X-CSRF-Token';
const CSRF_FAILED = 'CSRF_VALIDATION_FAILED';

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
const unsafeMethods: ReadonlySet<HttpMethod> = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export interface ApiRequestOptions {
  method?: HttpMethod;
  /** Sent as JSON. */
  body?: unknown;
  headers?: Record<string, string>;
  /** Sent as X-Correlation-Id; the API generates one when this is missing. */
  correlationId?: string;
  signal?: AbortSignal;
  /** Return the whole body, without unwrapping an OperationResult's `data`. */
  raw?: boolean;
}

export interface ApiResponse<T> {
  data: T;
  status: number;
  /** For example the pagination headers (X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages). */
  headers: Headers;
  correlationId: string | null;
}

interface RawResponse {
  status: number;
  statusText: string;
  headers: Headers;
  body: unknown;
  correlationId: string | null;
}

/** Reads the body: null when empty, parsed JSON for a JSON content type, otherwise (or if unparseable) the text. */
async function readBody(response: Response): Promise<unknown> {
  const text = await response.text();
  if (text.length === 0) {
    return null;
  }
  if ((response.headers.get('content-type') ?? '').includes('json')) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }
  return text;
}

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

/** One HTTP exchange, recorded in the inspector. Never adds CSRF; never throws for an HTTP status. */
async function send(
  path: string,
  method: HttpMethod,
  headers: Record<string, string>,
  body: unknown,
  signal: AbortSignal | undefined,
): Promise<RawResponse> {
  const requestHeaders: Record<string, string> = { Accept: 'application/json', ...headers };
  if (body !== undefined) {
    requestHeaders['Content-Type'] = 'application/json';
  }

  const id = recordRequest({ method, url: path, headers: requestHeaders, body: body ?? null });

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers: requestHeaders,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin',
      signal,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    recordResponse(id, { error: message });
    if (isAbort(error)) {
      throw error; // let TanStack Query treat it as a cancellation
    }
    throw new ApiError({ status: 0, message: `Could not reach the API: ${message}` });
  }

  const responseBody = await readBody(response);
  const correlationId = response.headers.get(CORRELATION_ID_HEADER);
  recordResponse(id, { status: response.status, headers: response.headers, body: responseBody, correlationId });

  return {
    status: response.status,
    statusText: response.statusText,
    headers: response.headers,
    body: responseBody,
    correlationId,
  };
}

/** GET /auth/csrf. Null when nobody is signed in (401). */
async function fetchCsrfToken(): Promise<string | null> {
  const response = await send('/auth/csrf', 'GET', {}, undefined, undefined);
  if (response.status === 401) {
    return null;
  }
  if (response.status < 200 || response.status >= 300) {
    throw parseErrorBody(response.status, response.statusText, response.body, response.correlationId);
  }
  const token = (response.body as { token?: unknown } | null)?.token;
  return typeof token === 'string' ? token : null;
}

function isCsrfFailure(response: RawResponse): boolean {
  return (
    response.status === 403 &&
    typeof response.body === 'object' &&
    response.body !== null &&
    (response.body as { errorCode?: unknown }).errorCode === CSRF_FAILED
  );
}

/**
 * The one way the app calls the API. Same-origin with cookies; adds the CSRF token to POST, PUT,
 * PATCH and DELETE and retries once with a new token after 403 CSRF_VALIDATION_FAILED; unwraps
 * OperationResult `data`; throws ApiError for anything other than 2xx.
 */
export async function apiFetch<T>(path: string, options: ApiRequestOptions = {}): Promise<ApiResponse<T>> {
  const method = options.method ?? 'GET';
  const unsafe = unsafeMethods.has(method);

  const attempt = async (): Promise<RawResponse> => {
    const headers: Record<string, string> = { ...options.headers };
    if (options.correlationId) {
      headers[CORRELATION_ID_HEADER] = options.correlationId;
    }
    if (unsafe) {
      const token = await getCsrfToken(fetchCsrfToken);
      if (token) {
        headers[CSRF_HEADER] = token;
      }
    }
    return send(path, method, headers, options.body, options.signal);
  };

  let response = await attempt();
  if (unsafe && isCsrfFailure(response)) {
    // The token belonged to another user or expired with the session: get a new one, once.
    clearCsrfToken();
    response = await attempt();
  }

  if (response.status < 200 || response.status >= 300) {
    throw parseErrorBody(response.status, response.statusText, response.body, response.correlationId);
  }

  return {
    data: (options.raw ? response.body : unwrap(response.body)) as T,
    status: response.status,
    headers: response.headers,
    correlationId: response.correlationId,
  };
}
