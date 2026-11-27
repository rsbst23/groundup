import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { proxiedPrefixes } from '../proxiedPrefixes';

// Reads the route files rather than the generated tree, so the test needs no browser and no build.
const routesDir = dirname(fileURLToPath(import.meta.url));

function routePaths(dir: string, base = ''): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (entry.isDirectory()) {
      return routePaths(join(dir, entry.name), `${base}/${entry.name}`);
    }
    if (!/\.tsx?$/.test(entry.name) || entry.name.endsWith('.test.ts') || entry.name.startsWith('__')) {
      return [];
    }
    const name = entry.name.replace(/\.tsx?$/, '');
    // File-based routing: index is the folder itself, dots are path separators.
    const segment = name === 'index' ? '' : `/${name.replace(/\./g, '/')}`;
    return [`${base}${segment}` || '/'];
  });
}

describe('routes', () => {
  const paths = routePaths(routesDir);

  it('finds the route files', () => {
    expect(paths).toContain('/');
    expect(paths).toContain('/wizard');
  });

  it.each(paths)('%s does not start with a proxied prefix', (path) => {
    for (const prefix of proxiedPrefixes) {
      // Vite matches a proxy key as a plain prefix, so /authors would also go to the API.
      expect(path.startsWith(prefix), `${path} starts with ${prefix}`).toBe(false);
    }
  });
});
