import { apiFetch } from './client';
import { withQuery } from './query';
import type { SignInStartDto, TokenLookupDto } from './types';

/** How an accept or join sign-in starts. `returnUrl` is a path on this site the callback returns to (GU-87). */
export interface StartOptions {
  forceLogin?: boolean;
  returnUrl: string;
}

/**
 * POST /auth/invitations/lookup (no sign-in needed): whether the invitation can be used, and the
 * organization and role names, or the reason it cannot.
 */
export async function lookupInvitation(token: string, signal?: AbortSignal): Promise<TokenLookupDto> {
  return (await apiFetch<TokenLookupDto>('/auth/invitations/lookup', { method: 'POST', body: { token }, signal })).data;
}

/** POST /auth/invitations/accept: the Keycloak address to send the whole page to. */
export async function acceptInvitation(token: string, options: StartOptions): Promise<SignInStartDto> {
  return (await apiFetch<SignInStartDto>(withQuery('/auth/invitations/accept', { returnUrl: options.returnUrl }), {
      method: 'POST',
      body: { token, forceLogin: options.forceLogin ?? false },
    })).data;
}

/** POST /auth/join-links/lookup (no sign-in needed). */
export async function lookupJoinLink(token: string, signal?: AbortSignal): Promise<TokenLookupDto> {
  return (await apiFetch<TokenLookupDto>('/auth/join-links/lookup', { method: 'POST', body: { token }, signal })).data;
}

/** POST /auth/join-links/join: the Keycloak address to send the whole page to. */
export async function joinWithLink(token: string, options: StartOptions): Promise<SignInStartDto> {
  return (await apiFetch<SignInStartDto>(withQuery('/auth/join-links/join', { returnUrl: options.returnUrl }), {
      method: 'POST',
      body: { token, forceLogin: options.forceLogin ?? false },
    })).data;
}
