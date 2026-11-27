import { useMutation } from '@tanstack/react-query';
import { login, signup } from '../../api/auth';
import { buildReturnUrl } from './returnTo';

export type SignInRequest =
  | { kind: 'login'; forceLogin?: boolean }
  | { kind: 'signup'; organizationName?: string };

/**
 * Starts a sign-in or sign-up: asks the API for the Keycloak address (with the return URL built
 * from the current page) and sends the whole page there. The mutation stays pending until the
 * page unloads, so buttons keep showing progress.
 */
export function useStartSignIn() {
  return useMutation({
    mutationFn: async (request: SignInRequest) => {
      const returnUrl = buildReturnUrl(window.location);
      const { redirectUrl } =
        request.kind === 'login'
          ? await login({ returnUrl, forceLogin: request.forceLogin })
          : await signup({ returnUrl, organizationName: request.organizationName });
      window.location.assign(redirectUrl);
      // Never settles: the page is navigating away
      await new Promise<never>(() => undefined);
    },
  });
}
