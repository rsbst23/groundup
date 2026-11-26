# Requirements: Test Debt (GU-73)

## Context

The suites have carried known failures for several phases: 12 unit tests, 48 integration tests, plus 1 flaky property test. The backlog review found the causes. All but one are test bugs: a duplicate auth scheme registration, a level-name casing mismatch, shared-database state, and stale expectations. One is a production bug: `MarkFailedAsync` can overwrite a Consumed auth flow with Failed. The story also covers Keycloak test gaps from Phase 10b and two small hardening items.

The suites will become a required quality gate in a GitHub Actions pipeline, so they must pass reliably on a clean Linux runner, not only on the development machine.

## Scope

**In scope:**
- every known failure
- the `MarkFailedAsync` bug
- correlation ID validation
- configurable exempt paths for the host/token check
- the Keycloak fixture version
- the Keycloak test gaps, including authorization-code exchange
- stale checkboxes in the Phase 10b `tasks.md`

**Out of scope (decided):**
- **Leftover `UserRole` rows from the old leave behaviour.** Nothing is released, so only development databases can contain them. A manual SQL statement is documented instead of a migration.
- **Moving the remaining EF InMemory unit tests to SQLite.** CLAUDE.md allows InMemory for unit tests. They stay for now.
- **Building the CI pipeline itself.** Requirement 7 only makes the suites ready for it.

**Delivery:** three PRs.
- PR 1 covers Requirements 1 and 2.
- PR 2 covers Requirements 3 and 4.
- PR 3 covers Requirements 5 and 6.

Requirement 7 applies to all three.

## Requirements

### Requirement 1: No known failures

1. The setup integration tests SHALL NOT register the `BootstrapAdminToken` scheme a second time. The application's `AddGroundUpSetup` already registers it.
2. Tests and test factories SHALL seed and look up setting levels by the canonical names (`DefaultSettingLevelSeeder.SystemLevelName` and `TenantLevelName`), not by lowercase literals.
3. The Keycloak startup validator keeps its current behaviour. While setup is incomplete it skips validation unless every setting is populated, because the seeders fill some defaults before the wizard runs. The class's doc comment and the property test SHALL describe that behaviour. A test SHALL check that partial settings throw once setup is complete.
4. Tests that share a database SHALL NOT depend on data left by other tests or on test order:
   - Permission keys are unique per test.
   - The auth-flow-state tests clear that table before each test, or assert only on their own rows.
5. The callback cookie test SHALL expect the current 200 JSON response. It SHALL still check that the `AuthToken` cookie carries the dot-prefixed default domain.
6. The unit, integration and sample suites SHALL each pass with no failures. The flaky correlation ID property test is fixed by Requirement 3.

### Requirement 2: Auth flow terminal states are final

1. `MarkFailedAsync` SHALL change a flow to Failed only if it is still Pending at the moment of the write, as a single conditional update. It SHALL NOT use a read-then-save on a possibly stale tracked entity.
2. Results SHALL stay the same:
   - Ok with the updated flow
   - 404 when the flow does not exist
   - 409 `CONFLICT` when the flow is already terminal (Consumed, Failed or Expired)
   - the failure reason is truncated to 1024 characters
3. A regression test SHALL check that marking a consumed flow as failed returns 409 and leaves the stored row Consumed. It SHALL use the same repository instance and context, which is the case that failed. A second test SHALL do the same for an expired flow.

### Requirement 3: Correlation ID validation

The correlation ID comes from a client header and is written to logs, error responses, the setup transaction log and the response header. Accepting it unchecked allows log injection and header manipulation. This conflicts with input validation (NIST 800-53 SI-10) and audit record integrity (AU-3, AU-9), which FedRAMP and ISO 27001 controls require.

1. An incoming `X-Correlation-Id` SHALL be accepted only when it is 1 to 128 characters long and every character is in `A-Z a-z 0-9 . _ : -`. This covers GUIDs, W3C trace and span ids, and common request-id formats.
2. Otherwise, including empty, whitespace-only, too long, or containing control or other characters, the middleware SHALL generate a new id (GUID, as today) and use it everywhere: the context item, the response header and logs.
3. A replaced value SHALL NOT be logged or echoed. At most, a Debug message says that an invalid id was replaced.
4. Property tests SHALL check that a valid id is propagated unchanged and that an invalid id is replaced with a GUID. Generators SHALL produce both classes explicitly, so the tests are deterministic in what they cover.

### Requirement 4: Host/token check exemptions and Keycloak version

1. The host/token reconciliation middleware SHALL take its exempt path prefixes from `AuthOptions`. The default stays `/auth` and `/tenant-selection`, and behaviour is unchanged when nothing is configured.
2. Prefixes match case-insensitively on path segment boundaries, so `/auth` exempts `/auth` and `/auth/x` but not `/authx`.
3. The sample app SHALL add `/swagger` in Development configuration only. Production configuration SHALL NOT exempt it.
4. The Keycloak test fixture SHALL use the same image version as `docker-compose.keycloak.yml` (26.3). The integration suite SHALL pass on it.

### Requirement 5: Keycloak unit test gaps

1. `PollyRetryPolicyPropertyTests`:
   - 5xx responses and transient failures are retried up to the configured count.
   - 4xx responses are not retried.
   - Exhausted retries return a failure result without throwing.
   - The tests run with no real delays.
2. `KeycloakOptionsSetupTests`:
   - Each Keycloak setting maps to its `KeycloakOptions` property.
   - Options reflect a changed setting after a refresh.

### Requirement 6: Keycloak integration test gaps

1. An authorization-code exchange test against the Testcontainers Keycloak SHALL obtain a real code:
   - drive the login form with an HTTP client, then follow the redirect to capture the code
   - exchange it through `KeycloakIdentityProviderService`
   - validate the returned tokens against the container's JWKS
2. A failure case: an invalid or reused code returns null, as the provider contract specifies, without throwing.
3. The Phase 10b `tasks.md` checkboxes SHALL match the tests that exist.

### Requirement 7: Ready for a CI quality gate

1. Every test SHALL pass on a clean Linux machine with Docker and the .NET 8 SDK, with no local state:
   - no developer database
   - no running sample app
   - no machine-specific paths
   - no Windows-only APIs
2. No test SHALL depend on test order or on data from another test. Tests that share a container SHALL isolate their data as in 1.4.
3. Tests SHALL NOT need network access beyond Docker image pulls.

### Requirement 8: Documentation

1. GU-71's notes (or the manual testing guide) SHALL include the manual SQL statement that removes leftover `UserRole` rows for inactive memberships in a development database, with a warning to back up first.
2. The baselines in CLAUDE.md and the steering rules, if they are recorded there, SHALL be updated to "no known failures".
