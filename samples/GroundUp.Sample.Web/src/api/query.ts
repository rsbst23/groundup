/** Appends the defined values as a query string (URL-encoded); undefined values are left out. */
export function withQuery(path: string, query: Record<string, string | number | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params.set(key, String(value));
    }
  }
  const text = params.toString();
  return text ? `${path}?${text}` : path;
}

/** A path segment, encoded (ids and tokens from the API or the URL). */
export function segment(value: string): string {
  return encodeURIComponent(value);
}
