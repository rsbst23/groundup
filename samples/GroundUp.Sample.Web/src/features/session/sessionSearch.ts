import { z } from 'zod';

/**
 * The query values the sign-in callback adds when it returns to the app (GU-87):
 * `result=signed_in|tenant_picker|no_tenant`, `alreadyMember=true`, `error=CODE`, `idpError=code`.
 * Anything that does not match is dropped, so a crafted link cannot put arbitrary text on the page.
 */
export const sessionSearchSchema = z.object({
  result: z.enum(['signed_in', 'tenant_picker', 'no_tenant']).optional().catch(undefined),
  // The router parses `true` in the query string to a boolean; accept the string form too.
  alreadyMember: z
    .preprocess((value) => (value === true || value === 'true' ? true : undefined), z.literal(true).optional())
    .catch(undefined),
  error: z.string().regex(/^[A-Z_]{1,64}$/).optional().catch(undefined),
  idpError: z.string().regex(/^[a-z_]{1,64}$/).optional().catch(undefined),
});

export type SessionSearch = z.infer<typeof sessionSearchSchema>;

/** True when the page was opened by the callback (a sign-in just happened or failed). */
export function isCallbackLanding(search: SessionSearch): boolean {
  return search.result !== undefined || search.error !== undefined;
}
