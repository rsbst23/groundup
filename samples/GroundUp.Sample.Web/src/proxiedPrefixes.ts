/**
 * Path prefixes the dev server sends to the sample API. App routes must never start with one of
 * these, or the dev server would send the page request to the API (see routes.test.ts).
 * Add a prefix here when the app starts calling a new API path.
 */
export const proxiedPrefixes = ['/api', '/auth', '/setup', '/health', '/swagger'] as const;
