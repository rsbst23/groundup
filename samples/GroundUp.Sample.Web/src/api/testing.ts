import { vi } from 'vitest';

/** A JSON response, as the API would send it. */
export function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers },
  });
}

/** An empty response (for example a bare 401). */
export function emptyResponse(status: number, headers: Record<string, string> = {}): Response {
  return new Response(null, { status, headers });
}

type Handler = (url: string, init: RequestInit) => Response | Promise<Response>;

/**
 * Replaces global fetch with a mock that answers by URL. Each handler is used once, in order,
 * for its URL; a URL with no handler left fails the test.
 */
export function mockFetch(routes: Record<string, Handler[]>) {
  const queues = new Map(Object.entries(routes).map(([url, handlers]) => [url, [...handlers]]));
  const mock = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = String(input);
    const handler = queues.get(url)?.shift();
    if (!handler) {
      throw new Error(`Unexpected fetch: ${init.method ?? 'GET'} ${url}`);
    }
    return handler(url, init);
  });
  vi.stubGlobal('fetch', mock);
  return mock;
}

/** The headers a mocked fetch call was made with. */
export function headersOf(mock: ReturnType<typeof mockFetch>, callIndex: number): Record<string, string> {
  const init = mock.mock.calls[callIndex]?.[1] as RequestInit | undefined;
  return (init?.headers ?? {}) as Record<string, string>;
}
