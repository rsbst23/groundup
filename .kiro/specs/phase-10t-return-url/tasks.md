# Tasks: Post-Sign-In Redirect Back to the Client App (GU-87)

Execute one top-level task at a time. After each: build, run all three .NET suites (and the web app's checks where it changes), commit, stop for review.

Baselines (main after #54): unit 1942 / 0; integration 525 / 0; sample 22/22; web 64 / 0.

Requirements: `requirements.md`. Design: `design.md`. One PR (branch `phase-10/return-url`).

## Gap analysis (before tasks)

- **Existing unit tests mock the state repository.** The cookie-mismatch path now looks the state up. An NSubstitute mock that was never set up returns a completed task with a null result, so the lookup must be null-safe (`lookup is { Success: true, Data: not null }`) and the affected `AuthFlowServiceTests` get an explicit "not found" setup where they care.
- **Tenantless results are also `IsSuccess`.** `CallbackRedirect` checks tenantless first (design 4); a test covers the order.
- **Signup has two flow types** (NewOrganization with a name, SimpleSignup without). Both take the return URL; tests cover both.
- **The Swagger CSRF interceptor** only adds a header; a `returnUrl` query parameter does not affect it.
- **Host-pinned (subdomain) sign-in.** The relative `Location` keeps the browser on the tenant's host; nothing else changes.
- **Logout** is untouched (Decision 2).

## Tasks

- [x] 1. Return URL rules and initiation (design 1, 2)
  - [x] 1.1 `ReturnUrlRules.TryNormalize` in `GroundUp.Auth.Services`; `AuthErrorCodes.InvalidReturnUrl`, `AuthErrorCodes.IdpError`
  - [x] 1.2 `AuthFlowService.InitiateFlowAsync` refuses an invalid return URL before building the URL or saving state; stores the normalized value
  - [x] 1.3 `returnUrl` query parameter on `POST /auth/login` and `/auth/signup` (their error responses now also carry `errorCode`, additive); `StartAsync(token, forceLogin, returnUrl, httpContext, ct)` on the invitation and join-link acceptance services and their controllers
  - [x] 1.4 Tests: `ReturnUrlRulesTests` (design 6 list, plus the length boundary and a custom callback path); `AuthFlowServiceTests` (refused before the URL builder, the state service and the cookie; valid value stored; blank stored as none); `AuthControllerTests` (login and both signup flow types pass it through; the refusal keeps status and code). The acceptance services have no unit tests, so their pass-through is covered by integration tests: accept and join store the return URL on the flow state, and an invalid one is refused with no flow state row
  - [x] 1.5 Check: build, unit 1980 / 0, integration 529 / 0, sample 22/22

- [ ] 2. Callback redirect and provider errors (design 3, 4)
  - [ ] 2.1 `FlowResult.ReturnUrl` and `FlowResult.IdpError`
  - [ ] 2.2 `HandleCallbackAsync` sets the return URL on every result once the state is known, including cookie mismatch (read-only lookup), consumed, expired, failed, consume race and no handler
  - [ ] 2.3 `IAuthFlowService.HandleProviderErrorAsync` (mark failed only with a matching cookie and a Pending state; `IdpError` only when it matches `^[a-z_]{1,64}$`; description truncated in the log)
  - [ ] 2.4 `CallbackRedirect.Build` in `GroundUp.Auth.Api`; `AuthController.Callback` reads `error` and `error_description`, redirects when there is a return URL, otherwise answers today's JSON (`IDP_ERROR` as 400 JSON)
  - [ ] 2.5 Unit tests: `CallbackRedirectTests`; `AuthFlowServiceTests` for the return URL on each result and the provider error; `AuthControllerTests` for redirect versus JSON and the provider error path
  - [ ] 2.6 Integration tests: login with a return URL ends in 302 with the cookie; invalid return URL leaves no flow state row; invitation email mismatch redirects with its code; provider error redirects and fails the row; existing callback tests unchanged
  - [ ] 2.7 Check: build, all three suites

- [ ] 3. Web app check, docs, PR
  - [ ] 3.1 `features/session/sessionSearch.ts` (zod schema) with a unit test; `routes/session.tsx` shows the landing values and refreshes the session on a result
  - [ ] 3.2 The web app's TESTING.md Part 2 (the return URL); the API guide's note on `returnUrl`; ARCHITECTURE.md "Returning to the app after sign-in"
  - [ ] 3.3 Run TESTING.md Part 2 by hand (or record what is left for Rob)
  - [ ] 3.4 Full check (.NET suites; web build, lint, test); push; PR info
  - [ ] 3.5 Jira: GU-87 comment; Done after merge
