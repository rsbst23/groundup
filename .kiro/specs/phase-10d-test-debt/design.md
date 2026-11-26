# Design: Test Debt (GU-73)

Requirements: [requirements.md](requirements.md).

## PR 1: No known failures (Requirements 1, 2)

### Setup integration factories (1.1, 1.2)

- `SetupWizardApiFactory`, `SetupRateLimitApiFactory` and `FirstAdminIdempotencyApiFactory`: delete the `AddAuthentication().AddScheme<…BootstrapAdminTokenAuthenticationHandler>` block. A comment says `AddGroundUpSetup` (called by the app) registers it.
- Seed and look up levels with `DefaultSettingLevelSeeder.SystemLevelName` everywhere these factories and tests use `"system"`. That covers the seed, the `AnyAsync` check, `SetupWizardTestScopeChainProvider` and the `FirstOrDefaultAsync` lookups. `AllowedLevelNames: { "system" }` already matches case-insensitively, but it changes too for consistency.
- `SetupWizardApiFactory._systemLevelSeeded` is `static`, so it is shared across factories and containers. It becomes an instance field.
- After the scheme fix, failures in these 41 tests that have a different cause get fixed in the same group if they are test bugs. Production bugs are reported to you before anything changes.

### Setup wizard unit tests (1.2)

`SetupWizardServiceTests` and `WizardIdempotencyAndRotationPropertyTests` seed `Name = "system"`. They change to `DefaultSettingLevelSeeder.SystemLevelName`. They keep EF InMemory (decided).

### Keycloak startup validator (1.3)

- Fix the class doc comment. While setup is incomplete (or unknown), validation runs only when all six settings are populated. Once setup is complete it always runs.
- `StartAsync_BootstrapIncompletePartialSettings_ThrowsForMissing` becomes `..._DoesNotThrow`, with the same generator.
- New property `StartAsync_BootstrapCompletePartialSettings_ThrowsNamingEachMissingKey`.

### Shared-database auth tests (1.4)

- The three `orders.read` call sites use `$"orders.read.{Guid.NewGuid():N}"`. Assertions compare against that variable.
- `AuthFlowStateCleanupSweeperTests`, `AuthFlowStateRepositoryBulkOperationsTests` and `AuthFlowStateRepositoryMarkFailedTests` implement `IAsyncLifetime`. `InitializeAsync` runs `DbContext.AuthFlowStates.ExecuteDeleteAsync()`.
  - These classes run serially (one xUnit collection), so clearing before each test is safe.
  - The exact-count assertions stay, because each test now starts from an empty table.
  - Other classes that write auth flow states (`AuthFlowStateRepositoryCrud/MarkConsumed`, the sweeper property tests) are checked for count assertions and get the same treatment if needed.

### Cookie-domain test (1.5)

Assert `HttpStatusCode.OK` and that the body has `redirectUrl`. The `Set-Cookie` check stays and becomes case-insensitive on the `domain=` attribute, because ASP.NET writes it in lowercase.

### MarkFailedAsync (2)

Same shape as `MarkConsumedAsync`:

```csharp
var rows = await DbSet
    .Where(x => x.Id == id && x.Status == FlowStatus.Pending)
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.Status, FlowStatus.Failed)
        .SetProperty(x => x.FailureReason, reason)
        .SetProperty(x => x.TerminatedAt, now)
        .SetProperty(x => x.UpdatedAt, now), ct);
```

- `now` is `DateTime.UtcNow`, captured once.
- If `rows == 1`, reload with `AsNoTracking` and return Ok.
- Otherwise reload with `AsNoTracking`: null returns 404, and a terminal flow returns 409 `CONFLICT` with the same message.
- The update bypasses the change tracker. A tracked copy of the row in the same context would still show the old status, as it already does after `MarkConsumedAsync`.
- Unlike `MarkConsumedAsync`, an expired-but-still-Pending row can be marked Failed. That matches today's behaviour.
- New tests in `AuthFlowStateRepositoryMarkFailedTests`: consumed then failed, and expired (status Expired) then failed. Both use one repository and one context and expect 409 with the stored row unchanged.

## PR 2: Hardening (Requirements 3, 4)

### CorrelationIdMiddleware (3)

```csharp
public static bool IsValid(string? value) =>
    value is { Length: > 0 and <= MaxLength } && value.All(IsAllowedChar);
private static bool IsAllowedChar(char c) =>
    c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-';
public const int MaxLength = 128;
```

- The header's first value is used only if `IsValid`. Otherwise a new GUID is generated. The rest of the middleware is unchanged.
- No logging. The middleware has no logger today, and a replaced value must not be logged anyway.
- `IsValid` and `MaxLength` are public on the middleware, so tests can use them. `GroundUp.Api` has no `InternalsVisibleTo`, and adding one isn't worth it for a single helper.
- Tests:
  - Property: valid ids are propagated unchanged. The generator is non-empty strings of 1 to 128 allowed characters.
  - Property: invalid ids are replaced by a parseable GUID. The generator is a union of: allowed text containing at least one disallowed character (control characters, space, `/`, `<`, non-ASCII); a 129 to 300 character allowed string; whitespace-only.
  - Facts: exactly 128 characters is accepted, 129 is replaced, and `"a\tb"` and `"a\r\nb"` are replaced.

### Host/token exemptions (4.1-4.3)

- `AuthOptions.HostTokenCheckExemptPathPrefixes`, a `List<string>`, defaults to `["/auth", "/tenant-selection"]`.
  - Configuration binding appends to a list's defaults instead of replacing them. So the property defaults to empty, and the middleware always adds the two built-in prefixes.
  - In other words, configuration can only add exemptions. `/auth` can never be un-exempted, which would break login.
- `IsExemptPath` matches on segment boundaries, ignoring case: `path == prefix` or `path` starts with `prefix + "/"`.
  - `/auth` already worked this way.
  - `/tenant-selection` used a plain prefix match, so `/tenant-selectionX` was exempt too. That narrows to the segment rule. It is a small tightening, and the rule is documented in the XML docs.
- Sample `appsettings.Development.json`: `"GroundUp:Auth:HostTokenCheckExemptPathPrefixes": ["/swagger"]`. Production `appsettings.json` is unchanged.
- Unit tests for the middleware:
  - a configured prefix is exempt
  - the segment boundary is respected
  - the built-in prefixes still apply with an empty list
  - non-exempt paths are still checked

### Keycloak fixture (4.4)

`KeycloakFixture` uses `quay.io/keycloak/keycloak:26.3`, matching the compose file. A comment points at the compose file so the two stay together.

## PR 3: Keycloak test gaps (Requirements 5, 6)

### Retry policy (5.1)

The policy is a private static method with real delays of 1 s, 2 s and 4 s plus jitter. It moves to an internal class:

```csharp
internal static class KeycloakRetryPolicy
{
    public const int RetryCount = 3;
    public static IAsyncPolicy<HttpResponseMessage> Create(ILogger logger, Func<int, TimeSpan>? sleepDurationProvider = null);
}
```

- Production calls it without the delay function, so behaviour is unchanged.
- Tests pass `_ => TimeSpan.Zero` and drive it through a stub `HttpMessageHandler`.
- `GroundUp.Auth.Keycloak` already has `InternalsVisibleTo` for both test projects.
- `PollyRetryPolicyPropertyTests`:
  - Property: for any 5xx status, the handler is called `RetryCount + 1` times, and the last response is returned without throwing.
  - Property: for any 4xx status, the handler is called once.
  - Fact: 408 (handled by `HandleTransientHttpError`) is retried.
  - Fact: an `HttpRequestException` is retried, then rethrown after the last attempt. That is Polly's behaviour, and the Keycloak services catch it.
  - Fact: a success after two failures returns the success.

### KeycloakOptionsSetup (5.2)

`KeycloakOptionsSetupTests` uses a `ServiceCollection` with a substitute `ISettingsService`:
- Property: for generated values of the six keys, `Configure` maps each to its option.
- Fact: a `SettingChangedEvent` for a Keycloak key fires the change token, and a non-Keycloak key does not.
- Fact: after the token fires, `IOptionsMonitor<KeycloakOptions>.CurrentValue` shows the new setting value.

### Code exchange (6.1, 6.2)

`KeycloakCodeExchangeTests` uses the existing `KeycloakFixture` and its `groundup` realm. The `groundup-app` client is public, has the standard flow on, and allows `http://localhost:*/auth/callback`.

1. Create a user with a password through the admin API, using the existing helper pattern.
2. Generate a PKCE verifier and S256 challenge.
3. GET `/realms/groundup/protocol/openid-connect/auth?client_id=groundup-app&response_type=code&scope=openid&redirect_uri=http://localhost:5000/auth/callback&state=…&code_challenge=…&code_challenge_method=S256`. Use an `HttpClient` with a `CookieContainer` and `AllowAutoRedirect = false`, following redirects manually until the login page.
4. Parse the `action` of `form#kc-form-login` with a small regex, then HTML-decode it. No HTML parser package is added. POST `username`, `password` and `credentialId=` to that URL.
5. The 302 `Location` contains `code` and the same `state`.
6. `KeycloakIdentityProviderService.ExchangeCodeForTokensAsync(code, redirectUri, codeVerifier: verifier)` returns tokens. `ValidateTokenAsync(accessToken)` is true, and the ID token's `nonce`/`sub` match the user.
7. A second exchange of the same code returns null without throwing, because Keycloak rejects reuse. An invalid code returns null.

### Tasks checkboxes (6.3)

Mark Phase 10b tasks 11.1-11.8 and 13.1-13.4 as done, with a note that client CRUD lives in `KeycloakRealmCrudTests` and role extraction in `KeycloakUserProvisioningTests`. 13.5 stays optional and open.

## CI readiness (Requirement 7)

For every new or changed test:
- Containers come from Testcontainers with pinned images (postgres:16-alpine, keycloak:26.3).
- No fixed host ports. The redirect URI host and port are never contacted, since the code is read from the `Location` header.
- No `Thread.Sleep` and no wall-clock waits beyond the existing expiry offsets.
- Data is isolated per test, as described in 1.4.
- No Windows-only APIs or paths.

A final check lists the test projects' fixtures and confirms every container image is pinned. Any `latest` tag found is pinned.

## Documentation (Requirement 8)

- `.kiro/specs/phase-10c-token-revocation/` gets a short note with the cleanup SQL for development databases. The SQL deletes `UserRoles` rows that join to an inactive `UserTenants` row, with a back-up warning. Table and column names are checked against the Auth model snapshot first.
- CLAUDE.md and the steering rules: wherever baselines or known failures are mentioned, they change to "all suites pass".
