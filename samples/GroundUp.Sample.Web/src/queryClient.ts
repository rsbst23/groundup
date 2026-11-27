import { QueryClient } from '@tanstack/react-query';

/**
 * The app's one QueryClient. Retry rules and the signed-out handling are added with the API client.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A test tool: refresh on purpose, not when coming back from the Keycloak tab.
      refetchOnWindowFocus: false,
    },
    mutations: { retry: false },
  },
});
