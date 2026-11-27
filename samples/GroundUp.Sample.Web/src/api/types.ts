/** DTO types for the endpoints the app calls. Written by hand for now (generating them from the OpenAPI document is a later decision). */

/** GET /auth/me. Times are UTC ISO 8601 strings, or null when the token lacks the claim. */
export interface MeDto {
  /** The GroundUp user id, or the identity provider's subject for a token on the tenant picker. */
  userId: string;
  email: string | null;
  displayName: string | null;
  /** Null when the session has no current tenant. */
  tenantId: string | null;
  roles: string[];
  issuedAt: string | null;
  expiresAt: string | null;
  authTime: string | null;
}

/** One entry of GET /auth/me/tenants. Standard tenants carry `current`; enterprise tenants carry `subdomain`. */
export interface MyTenantDto {
  id: string;
  name: string;
  slug: string;
  type: 'standard' | 'enterprise' | string;
  switchable: boolean;
  current?: boolean;
  subdomain?: string | null;
}
