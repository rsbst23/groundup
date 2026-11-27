/** The framework's one result type, as JSON (GroundUp.Core OperationResult<T>). */
export interface OperationResult<T> {
  data?: T | null;
  success: boolean;
  message: string;
  errors?: string[] | null;
  statusCode: number;
  errorCode?: string | null;
}

/** True for a body shaped like an OperationResult: a boolean `success` and a numeric `statusCode`. */
export function isOperationResult(body: unknown): body is OperationResult<unknown> {
  return (
    typeof body === 'object' &&
    body !== null &&
    typeof (body as Record<string, unknown>).success === 'boolean' &&
    typeof (body as Record<string, unknown>).statusCode === 'number'
  );
}

/** Returns an OperationResult's `data`, or the body unchanged when it is not an OperationResult. */
export function unwrap(body: unknown): unknown {
  return isOperationResult(body) ? (body.data ?? null) : body;
}
