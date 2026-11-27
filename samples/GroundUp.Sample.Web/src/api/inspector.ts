/**
 * In-memory history of API calls for the request inspector. Values that must not be shown are
 * masked when an entry is recorded, so unmasked values never enter the store. Cleared on reload.
 */

export const MASK = '***';
export const INSPECTOR_CAPACITY = 100;

const maskedHeaders = new Set(['authorization', 'cookie', 'set-cookie', 'x-csrf-token']);
const maskedKeyPattern = /token|password|secret/i;

export interface InspectorEntry {
  id: number;
  method: string;
  url: string;
  /** When the request started (ms since the epoch). */
  startedAt: number;
  /** Null while the call is in flight. */
  durationMs: number | null;
  /** HTTP status; 0 when the API could not be reached; null while in flight. */
  status: number | null;
  correlationId: string | null;
  requestHeaders: Record<string, string>;
  requestBody: unknown;
  responseHeaders: Record<string, string>;
  responseBody: unknown;
  /** Set when the call failed before a response (network error, abort). */
  error: string | null;
}

/** Header names are lower-cased; the masked ones are replaced with `***`. */
export function maskHeaders(headers: Headers | Record<string, string>): Record<string, string> {
  const entries = headers instanceof Headers ? [...headers.entries()] : Object.entries(headers);
  return Object.fromEntries(
    entries.map(([name, value]) => {
      const lower = name.toLowerCase();
      return [lower, maskedHeaders.has(lower) ? MASK : value];
    }),
  );
}

/** Replaces the value of any key containing token, password or secret, at any depth. */
export function maskBody(body: unknown): unknown {
  if (Array.isArray(body)) {
    return body.map(maskBody);
  }
  if (typeof body === 'object' && body !== null) {
    return Object.fromEntries(
      Object.entries(body).map(([key, value]) => [key, maskedKeyPattern.test(key) ? MASK : maskBody(value)]),
    );
  }
  return body;
}

let entries: readonly InspectorEntry[] = [];
let nextId = 1;
const listeners = new Set<() => void>();

function publish(next: readonly InspectorEntry[]): void {
  entries = next;
  listeners.forEach((listener) => listener());
}

/** Records the start of a call and returns its id. Newest first; keeps the last 100. */
export function recordRequest(request: {
  method: string;
  url: string;
  headers: Record<string, string>;
  body: unknown;
}): number {
  const id = nextId++;
  const entry: InspectorEntry = {
    id,
    method: request.method,
    url: request.url,
    startedAt: Date.now(),
    durationMs: null,
    status: null,
    correlationId: null,
    requestHeaders: maskHeaders(request.headers),
    requestBody: maskBody(request.body),
    responseHeaders: {},
    responseBody: null,
    error: null,
  };
  publish([entry, ...entries].slice(0, INSPECTOR_CAPACITY));
  return id;
}

/** Completes a recorded call with its response, or with the error that stopped it. */
export function recordResponse(
  id: number,
  response:
    | { status: number; headers: Headers; body: unknown; correlationId: string | null }
    | { error: string },
): void {
  const now = Date.now();
  publish(
    entries.map((entry) => {
      if (entry.id !== id) {
        return entry;
      }
      const durationMs = now - entry.startedAt;
      if ('error' in response) {
        return { ...entry, durationMs, status: 0, error: response.error };
      }
      return {
        ...entry,
        durationMs,
        status: response.status,
        correlationId: response.correlationId,
        responseHeaders: maskHeaders(response.headers),
        responseBody: maskBody(response.body),
      };
    }),
  );
}

export function subscribeInspector(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The current entries, newest first. The array is replaced on every change (for useSyncExternalStore). */
export function getInspectorSnapshot(): readonly InspectorEntry[] {
  return entries;
}

export function clearInspector(): void {
  publish([]);
}
