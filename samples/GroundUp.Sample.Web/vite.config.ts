/// <reference types="vitest/config" />
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';
import { proxiedPrefixes } from './src/proxiedPrefixes.ts';

// The browser only talks to this dev server. API paths are proxied to the sample API with the
// browser's Host kept (changeOrigin: false), so the API builds the Keycloak redirect_uri and the
// post-logout URI on this origin and sets its cookies here, as for a deployed client.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const target = env.GROUNDUP_API_URL || 'http://localhost:5000';

  return {
    plugins: [
      // Test files can sit next to routes; they are not routes.
      tanstackRouter({ target: 'react', autoCodeSplitting: true, routeFileIgnorePattern: '\\.test\\.ts$' }),
      react(),
    ],
    server: {
      port: 5173,
      // A busy port must fail: Keycloak only accepts the registered redirect URI.
      strictPort: true,
      // Subdomain tenants (testing manual, subdomain part) use hosts like acme.groundup.test.
      allowedHosts: ['localhost', '.groundup.test'],
      proxy: Object.fromEntries(
        proxiedPrefixes.map((prefix) => [prefix, { target, changeOrigin: false, xfwd: true }]),
      ),
    },
    build: {
      // MUI and React make the main chunk about 550 kB minified (175 kB gzipped). Fine for a
      // development app; revisit if this becomes a deployed template.
      chunkSizeWarningLimit: 800,
    },
    test: {
      environment: 'node',
      include: ['src/**/*.test.ts'],
    },
  };
});
