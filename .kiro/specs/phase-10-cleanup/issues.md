# Phase 10 Cleanup — Known Issues & Fixes

This document captures all issues discovered during manual testing of the Phase 10 auth system. These need to be addressed before Phase 10 can be considered production-ready.

## Issues Found During Manual Testing

### 1. Sample App Missing DI Registrations

**Status:** Partially fixed (code changes made during debugging session)

**Problem:** The sample app's `Program.cs` didn't register all required modules as new ones were added across phases 10A/B/C. It was missing:
- `AddGroundUpAuthKeycloak()` — provides `IIdentityProviderService`
- `AddGroundUpBootstrap(builder.Configuration)` — provides `IBootstrapStateService`
- `AddGroundUpSetup(builder.Configuration)` — provides the `BootstrapAdminToken` auth scheme
- Project reference to `GroundUp.Auth.Keycloak`

**Fix:** Already applied to Program.cs and csproj. Needs a unit test or startup smoke test to prevent regression.

---

### 2. Missing Production `IUnitOfWork` Implementation

**Status:** Fixed

**Problem:** `NewOrganizationFlowHandler` depends on `IUnitOfWork` for transactional operations, but no production implementation existed. Only the integration test fixture had one.

**Fix:** Created `AuthDbContextUnitOfWork` in `GroundUp.Auth.Data.Postgres` and registered it in `AddGroundUpAuthPostgres()`. Needs a unit test.

---

### 3. Case-Sensitivity Mismatch in Setting Level Names

**Status:** Partially fixed

**Problem:** The `DefaultSettingsSeeder` creates levels as "System" and "Tenant" (PascalCase), but multiple places in the code reference them as "system" (lowercase):
- `SetupWizardService.GetSystemLevelIdAsync` — fixed to "System"
- `SettingsService.EnsureDefinitionAsync` level lookup — fixed with `.ToLower()` comparison
- Various `AllowedLevelNames` arrays in seeder/wizard code use `"system"` lowercase

**Long-term fix:** Either:
- Normalize all references to PascalCase "System"/"Tenant" everywhere, OR
- Make ALL level name comparisons case-insensitive at the query level (preferred — one fix, no regressions)

---

### 4. `DefaultScopeChainProvider` Didn't Include System Level

**Status:** Fixed

**Problem:** When `TenantId == Guid.Empty` (no tenant context — common during setup, bootstrap, and unauthenticated requests), the provider returned an empty scope chain. This made system-level settings unreadable.

**Fix:** Changed to always include the System level with `ScopeId = null`. Only includes Tenant level when a tenant is active.

**Risk:** This changes the scope chain for ALL settings resolution, not just auth. Needs regression testing against the settings service tests.

---

### 5. `KeycloakStartupValidator` Too Aggressive

**Status:** Fixed

**Problem:** Two issues:
1. Used `GetRequiredService<IBootstrapStateService>()` — crashes if bootstrap module isn't registered
2. Original "skip validation" logic only skipped when ALL settings were empty. But seeders populate some defaults (public-base-url, shared-realm-name), so it thought settings were "partially configured" and ran validation (which failed because admin settings weren't set yet).

**Fix:** 
- Changed to `GetService<IBootstrapStateService>()` (nullable)
- Changed skip logic to: skip validation when bootstrap is not complete AND settings are not ALL populated (instead of ALL empty)

---

### 6. Setup Wizard `BootstrapKeycloakAsync` Fails with 502

**Status:** NOT FIXED (bypassed with direct DB inserts)

**Problem:** The wizard's Step 4 (Keycloak Bootstrap) authenticates to the master realm with admin/admin, then tries to create a service-account client. It fails with a generic 502 "Keycloak responded with an error during bootstrap."

**Root cause:** Unknown — the error is caught and wrapped generically. No logs are visible in the debug console because Serilog isn't configured to write to console in the dev environment.

**Investigation needed:**
- Add console logging for Keycloak HTTP calls in development
- Check if the `KeycloakAdminHttpClient.AcquireAdminTokenAsync` call itself fails (401 from Keycloak?) or if it's a later call (CreateServiceAccountClient, GetServiceAccountRoles)
- May be a Keycloak 26.x API change — the code was written against the Keycloak admin REST API, but responses/endpoints may differ from what the code expects
- May also be that `public-base-url` is being used for the admin API when it should use a separate internal URL, or the realm context is wrong

**Workaround applied:** Direct DB inserts of the admin-client-id, admin-client-secret, and app-client-id settings, then manually marking `BootstrapState.IsComplete = true`.

---

### 7. Missing Keycloak Setting Definitions in Auth Seeder

**Status:** NOT FIXED

**Problem:** `DefaultAuthSettingsSeeder` only creates definitions for:
- `auth.application.default-domain`
- `auth.keycloak.shared-realm-name`
- `auth.keycloak.public-base-url`

It does NOT create definitions for:
- `auth.keycloak.internal-base-url`
- `auth.keycloak.admin-client-id`
- `auth.keycloak.admin-client-secret`
- `auth.keycloak.app-client-id`

These are created lazily by the setup wizard's `EnsureDefinitionAsync` calls. This is fine for the wizard flow, but means a fresh DB + app restart won't have all definitions until the wizard runs.

**Fix:** Add all Keycloak setting definitions to `DefaultAuthSettingsSeeder` so they exist on first startup regardless of whether the wizard has run.

---

### 8. Keycloak Redirect URI Only Allows HTTPS

**Status:** Fixed in realm.json (not deployed to running container)

**Problem:** The `keycloak/realm.json` only had `https://localhost:*/auth/callback` but the sample app runs on `http://localhost:5000`.

**Fix:** Added `http://localhost:*/auth/callback` and `http://localhost:*` to realm.json. For existing containers, must be updated manually in Keycloak admin UI or containers recreated with `docker compose down -v && up -d`.

---

### 9. No Console Logging in Debug Mode

**Status:** NOT FIXED

**Problem:** When running the app with F5 in Kiro, there's no visible logging output in the Debug Console. This made debugging the Keycloak 502 and other issues extremely difficult. Only "Now listening on: http://localhost:5000" appears.

**Fix:** Configure Serilog (or the default logger) to write to Console in Development environment, with at minimum Warning level for Microsoft.* and Information for GroundUp.* namespaces.

---

### 10. `BootstrapOptions` Token Length Requirement Not Documented

**Status:** Documented in testing guide

**Problem:** `BootstrapAdminToken` must be ≥32 characters but this isn't obvious. The error message is generic ("OptionsValidationException").

**Fix:** Better error message in `BootstrapOptionsValidator`, or document the minimum length prominently.

---

## Summary of Code Changes Made (Need Review)

These changes were made during the debugging session and should be reviewed, tested, and committed properly:

| File | Change |
|------|--------|
| `samples/GroundUp.Sample/Program.cs` | Added `AddGroundUpAuthKeycloak()`, `AddGroundUpBootstrap()`, `AddGroundUpSetup()` |
| `samples/GroundUp.Sample/GroundUp.Sample.csproj` | Added Keycloak project reference |
| `samples/GroundUp.Sample/appsettings.Development.json` | Added BootstrapAdminToken, DatabaseConnection, MasterKey |
| `src/GroundUp.Auth.Data.Postgres/AuthDbContextUnitOfWork.cs` | NEW — production IUnitOfWork |
| `src/GroundUp.Auth.Data.Postgres/AuthServiceCollectionExtensions.cs` | Register IUnitOfWork |
| `src/GroundUp.Auth.Keycloak/KeycloakStartupValidator.cs` | Graceful IBootstrapStateService, lenient validation |
| `src/GroundUp.Services/Settings/DefaultScopeChainProvider.cs` | Always include System level |
| `src/GroundUp.Services/Settings/SettingsService.cs` | Case-insensitive level name lookup |
| `src/GroundUp.Services/Setup/SetupWizardService.cs` | Fixed "System" vs "system" casing |
| `keycloak/realm.json` | Added http redirect URIs |

---

## Items to Add During Manual Auth Testing

_(Add new issues here as they're discovered)_

### 11. AuthController Returns 302 Redirects (Incompatible with Pure API)

**Status:** NOT FIXED — Design issue

**Problem:** The `AuthController` returns HTTP 302 redirects from `GET /auth/login` and `GET /auth/register`. For a pure API (no server-rendered pages), this doesn't make sense. The API should return JSON with the URL and let the SPA/client decide how to redirect.

**Current behavior:** `GET /auth/register?organizationName=Foo` → 302 redirect to Keycloak
**Expected behavior:** `POST /auth/register` → 200 JSON `{ "redirectUrl": "http://keycloak/..." }` or similar

**Fix:** Change auth endpoints to return JSON responses with redirect URLs. The client app is responsible for navigation.

---

### 12. Keycloak Registration Configuration Not Automated

**Status:** NOT FIXED

**Problem:** Several Keycloak configurations must be done manually:
- Enable "User registration" on the groundup realm
- Add `http://localhost:*/auth/callback` to redirect URIs (the baked realm.json only had https)
- Create the `groundup-admin` confidential client with service account roles

**Fix:** Either:
- Update `keycloak/realm.json` to include all required configuration (registration enabled, http redirect URIs, admin client pre-created)
- OR have the setup wizard's `keycloak-bootstrap` step configure these via the admin API

---

### 13. Registration Flow Should Not Delegate User Creation to Keycloak UI

**Status:** NOT FIXED — Architecture decision needed

**Problem:** The current NewOrganization flow redirects the user to Keycloak's login/registration page to create their account. This means:
- User has to click "Register" a second time on Keycloak's page (bad UX)
- GroundUp has no control over the registration form (can't add org name, terms, captcha)
- Different pattern between shared realm (self-register at Keycloak) and enterprise realm (admin creates user)

**Proposed fix:** GroundUp should own user registration for both shared and enterprise realms:
1. Client app collects registration info (name, email, password, org name) in its own form
2. Client POSTs to GroundUp API (`POST /auth/register`)
3. GroundUp creates the user in Keycloak via admin API (`IIdentityProviderAdminService`)
4. GroundUp creates the tenant + membership + TenantAdmin atomically
5. GroundUp issues a token directly (no OAuth redirect needed since we just created/verified the user)

**Benefits:**
- Consistent UX for shared and enterprise tenants
- GroundUp controls the registration experience
- No double-redirect or double-registration-click
- Can add business logic (email validation, captcha, terms) at the API level
- Client app owns the form UI

**Trade-offs:**
- Requires the admin client credentials to be available (already seeded)
- Password must be sent to the API (HTTPS required)
- Loses Keycloak's built-in features (social login during registration, custom themes)

**Note:** The LOGIN flow still uses OAuth redirect (user authenticates at Keycloak, comes back via callback). Only REGISTRATION changes — because for registration we need to create the user AND the org atomically, and we want to control the experience.

---

### 14. `kc_action=register` Missing from Authorize URL (Interim Fix)

**Status:** NOT FIXED

**Problem:** Even if we keep the current redirect-to-Keycloak pattern temporarily, the authorize URL should include `kc_action=register` for the NewOrganization flow so users land directly on the registration form instead of the login form.

**Fix:** In `AuthUrlBuilderService`, when the flow type is `NewOrganization`, append `&kc_action=register` to the authorize URL. This is a one-line change that improves UX immediately while the longer-term API-owned registration (issue 13) is designed.

