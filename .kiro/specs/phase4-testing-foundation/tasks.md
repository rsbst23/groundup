# Implementation Plan: Phase 4 — Testing Foundation (HTTP Integration Test Infrastructure)

## Overview

Build the three-tier HTTP integration test infrastructure: a NuGet-distributable `GroundUp.Tests.Common` project with generic base classes, and a `GroundUp.Sample.Tests.Integration` project demonstrating how consuming apps use it. Tests exercise the full HTTP stack against a real Testcontainers Postgres database via the Sample app's TodoItem endpoints.

## Tasks

- [x] 1. Create branch and set up GroundUp.Tests.Common project
  - [x] 1.1 Create feature branch `phase-4/testing-foundation` from main
    - Run `git checkout -b phase-4/testing-foundation` from main
  - [x] 1.2 Create `src/GroundUp.Tests.Common/GroundUp.Tests.Common.csproj`
    - Target `net8.0`, enable nullable and implicit usings
    - Add `FrameworkReference` for `Microsoft.AspNetCore.App`
    - Add package references: `Microsoft.AspNetCore.Mvc.Testing` 8.*, `Testcontainers.PostgreSql` 3.*, `xunit` 2.5.3
    - Add project references: `GroundUp.Core`, `GroundUp.Data.Postgres`
    - _Requirements: 1.1, 2.4_
  - [x] 1.3 Add `GroundUp.Tests.Common` to `groundup.sln` under the `src` solution folder
    - Use `dotnet sln add` with `--solution-folder src`
    - _Requirements: 1.1_

- [x] 2. Implement GroundUp.Tests.Common base classes
  - [x] 2.1 Create `src/GroundUp.Tests.Common/Fixtures/TestAuthHandler.cs`
    - Implement `AuthenticationHandler<AuthenticationSchemeOptions>` with `SchemeName = "TestScheme"`
    - `HandleAuthenticateAsync()` returns `AuthenticateResult.Success()` with a `ClaimsPrincipal` containing claims: `sub=test-user-id`, `name=Test User`, `email=test@example.com`
    - Mark class as `sealed`
    - _Requirements: 3.1, 3.2, 3.3_
  - [x] 2.2 Create `src/GroundUp.Tests.Common/Fixtures/GroundUpWebApplicationFactory.cs`
    - Generic abstract class `GroundUpWebApplicationFactory<TEntryPoint, TContext>` extending `WebApplicationFactory<TEntryPoint>` implementing `IAsyncLifetime`
    - Constraints: `where TEntryPoint : class`, `where TContext : GroundUpDbContext`
    - `InitializeAsync()`: start `postgres:16-alpine` Testcontainers container
    - `ConfigureWebHost()`: call `ConfigureTestServices` to remove `DbContextOptions<TContext>` via `RemoveAll`, re-register `TContext` with `AddDbContext<TContext>()` using Testcontainers connection string (NOT `AddGroundUpPostgres` — avoids double-registering interceptors/hosted services). Reuse already-registered `AuditableInterceptor` and `SoftDeleteInterceptor` from the service provider
    - After building the host, resolve `TContext` and call `context.Database.Migrate()` to apply EF Core migrations
    - Register `TestAuthHandler` as an authentication scheme (not enforced)
    - Provide `virtual void ConfigureTestServices(IServiceCollection services)` hook for subclasses
    - `DisposeAsync()`: stop and dispose the Testcontainers container
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 3.3, 13.1, 13.2, 13.4_
  - [x] 2.3 Create `src/GroundUp.Tests.Common/Fixtures/IntegrationTestBase.cs`
    - Abstract class with `protected HttpClient Client` property
    - Constructor accepts `HttpClient` parameter
    - `ToJsonContent<T>(T obj)` — serializes to `StringContent` with `application/json` using `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`
    - `ReadResultAsync<T>(HttpResponseMessage)` — deserializes to `OperationResult<T>` with camelCase + case-insensitive options
    - `ReadResultAsync(HttpResponseMessage)` — deserializes to non-generic `OperationResult`
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 3. Checkpoint — Verify GroundUp.Tests.Common compiles
  - Build the solution with `dotnet build` to confirm the new project compiles cleanly
  - Commit: "Add GroundUp.Tests.Common with generic factory, base test class, and auth handler"
  - Push with `-u` flag
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Create GroundUp.Sample.Tests.Integration project and fixtures
  - [x] 4.1 Add `public partial class Program { }` at the bottom of `samples/GroundUp.Sample/Program.cs`
    - This makes the top-level-statements-generated `Program` class accessible to `WebApplicationFactory<Program>`
    - _Requirements: 14.1_
  - [x] 4.2 Create `samples/GroundUp.Sample.Tests.Integration/GroundUp.Sample.Tests.Integration.csproj`
    - Target `net8.0`, enable nullable and implicit usings, set `IsPackable=false`, `IsTestProject=true`
    - Add `FrameworkReference` for `Microsoft.AspNetCore.App`
    - Add package references: `coverlet.collector` 6.0.0, `FluentAssertions` 6.*, `FsCheck.Xunit` 3.*, `Microsoft.NET.Test.Sdk` 17.8.0, `xunit` 2.5.3, `xunit.runner.visualstudio` 2.5.3
    - Add project references: `GroundUp.Tests.Common`, `GroundUp.Sample`
    - Add global using for `Xunit`
    - _Requirements: 13.4_
  - [x] 4.3 Add `GroundUp.Sample.Tests.Integration` to `groundup.sln` under the `samples` solution folder
    - Use `dotnet sln add` with `--solution-folder samples`
  - [x] 4.4 Create `samples/GroundUp.Sample.Tests.Integration/Fixtures/SampleApiFactory.cs`
    - `public sealed class SampleApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>`
    - Inherits all lifecycle management from the base class — intentionally minimal
    - _Requirements: 1.1, 13.1_
  - [x] 4.5 Create `samples/GroundUp.Sample.Tests.Integration/Fixtures/ApiCollection.cs`
    - `[CollectionDefinition("Api")]` class implementing `ICollectionFixture<SampleApiFactory>`
    - _Requirements: 13.1_

- [x] 5. Checkpoint — Verify sample test project compiles
  - Build the solution with `dotnet build` to confirm both new projects compile cleanly
  - Commit: "Add GroundUp.Sample.Tests.Integration project with SampleApiFactory and ApiCollection"
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement TodoItem CRUD integration tests
  - [x] 6.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/TodoItemCrudTests.cs`
    - Inherit from `IntegrationTestBase`, use `[Collection("Api")]`, accept `SampleApiFactory` in constructor and pass `factory.CreateClient()` to base
    - `Create_ValidTodoItem_Returns201WithGuidAndLocation` — POST a TodoItem with GUID-suffixed title, assert 201 status, non-empty GUID Id in response, Location header present
    - `GetById_ExistingItem_Returns200WithMatchingData` — POST to create, then GET by returned Id, assert 200 status, matching Title and Description
    - `Update_ExistingItem_Returns200WithUpdatedValues` — POST to create, PUT with modified title, assert 200 with updated Title, then GET to confirm persistence
    - `Delete_ExistingItem_Returns200AndSubsequentGetReturns404` — POST to create, DELETE, assert 200, then GET same Id returns 404
    - All test data uses GUID-suffixed titles for isolation
    - _Requirements: 4.1, 4.2, 4.3, 6.1, 6.2, 6.3, 7.1, 7.2, 7.3, 8.1, 8.2, 13.3_
  - [ ]* 6.2 Write property test for CRUD round-trip preservation
    - **Property 1: CRUD Round-Trip Preservation**
    - Create `CrudRoundTripPropertyTests` class (or add `[Property]` methods to `TodoItemCrudTests`)
    - Use FsCheck.Xunit `[Property]` attribute with custom `Arbitrary<T>` generating valid TodoItem payloads (non-empty titles, optional descriptions)
    - For any valid TodoItem: POST → GET by Id → assert Title and Description match. Then PUT with different title → GET → assert updated values match.
    - Tag: `Feature: phase4-testing-foundation, Property 1: CRUD Round-Trip Preservation`
    - **Validates: Requirements 4.1, 4.2, 6.1, 6.2, 6.3, 7.1, 7.2, 7.3**

- [x] 7. Implement TodoItem filtering integration tests
  - [x] 7.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/TodoItemFilterTests.cs`
    - Inherit from `IntegrationTestBase`, use `[Collection("Api")]`
    - `GetAll_WithTitleFilter_ReturnsOnlyMatchingItems` — Create multiple items with distinct GUID-suffixed titles, GET with `Filters[Title]` matching one unique title, assert only matching items returned
    - `GetAll_WithNonMatchingTitleFilter_ReturnsEmptyList` — GET with `Filters[Title]` matching no items, assert empty Items list
    - All queries use unique title prefixes for data isolation
    - _Requirements: 9.1, 9.2, 13.3_

- [x] 8. Implement TodoItem paging integration tests
  - [x] 8.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/TodoItemPagingTests.cs`
    - Inherit from `IntegrationTestBase`, use `[Collection("Api")]`
    - `GetAll_WithPageSize2_ReturnsCorrectPageAndHeaders` — Create 5+ items with a unique title prefix, GET with `PageSize=2`, `PageNumber=1`, `ContainsFilters[Title]` matching the prefix, assert exactly 2 items returned
    - Assert `X-Total-Count` header reflects total matching items
    - Assert `X-Total-Pages` header equals ceiling of total / 2
    - Assert `X-Page-Number` and `X-Page-Size` headers have valid values
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 10.1, 10.2, 10.3, 13.3_

- [x] 9. Implement TodoItem sorting integration tests
  - [x] 9.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/TodoItemSortingTests.cs`
    - Inherit from `IntegrationTestBase`, use `[Collection("Api")]`
    - `GetAll_WithSortByTitle_ReturnsItemsInAscendingOrder` — Create multiple items with distinct titles sharing a unique prefix, GET with `SortBy=Title` and `ContainsFilters[Title]` matching the prefix, assert Items ordered alphabetically ascending
    - `GetAll_WithSortByTitleDesc_ReturnsItemsInDescendingOrder` — Same setup, GET with `SortBy=Title desc`, assert Items ordered alphabetically descending
    - _Requirements: 11.1, 11.2, 13.3_

- [x] 10. Implement Correlation ID integration tests
  - [x] 10.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/CorrelationIdTests.cs`
    - Inherit from `IntegrationTestBase`, use `[Collection("Api")]`
    - `Request_WithCorrelationId_EchoesCorrelationIdInResponse` — Send GET to `api/todoitems` with `X-Correlation-Id` header set to a known value, assert response includes same `X-Correlation-Id` header value
    - `Request_WithoutCorrelationId_GeneratesCorrelationIdInResponse` — Send GET to `api/todoitems` without `X-Correlation-Id` header, assert response includes a non-empty `X-Correlation-Id` header
    - _Requirements: 12.1, 12.2_
  - [ ]* 10.2 Write property test for Correlation ID echo
    - **Property 2: Correlation ID Echo**
    - Use FsCheck.Xunit `[Property]` attribute with `Arbitrary<string>` generating non-empty strings
    - For any non-empty correlation ID string: send request with `X-Correlation-Id` header → assert response `X-Correlation-Id` header matches exactly
    - Tag: `Feature: phase4-testing-foundation, Property 2: Correlation ID Echo`
    - **Validates: Requirements 12.1**

- [x] 11. Checkpoint — Verify delete isolation and full test suite
  - [x] 11.1 Verify soft-delete isolation in CRUD tests
    - Confirm that after DELETE, a GET-all with `ContainsFilters[Title]` matching the deleted item's unique title returns an empty Items list (soft-deleted items excluded by global query filter)
    - _Requirements: 8.3, 13.3_

- [x] 12. Final checkpoint — Build, run all tests, commit
  - Build the entire solution with `dotnet build`
  - Run all tests with `dotnet test` (unit, existing integration, and new HTTP integration tests)
  - Verify all existing 189 unit tests and 27 repository-level integration tests still pass (no regressions)
  - Commit: "Complete Phase 4 — HTTP integration test infrastructure with TodoItem endpoint tests"
  - Push to remote
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The design uses C# — all code examples and implementations use C# with .NET 8
- `GroundUp.Tests.Common` goes under `src/` solution folder (framework infrastructure, NuGet-distributable)
- `GroundUp.Sample.Tests.Integration` goes under `samples/` solution folder (consuming app pattern)
- Existing `tests/` projects are NOT modified — the 189 unit tests and 27 integration tests remain unchanged
- Connection string replacement removes ONLY `DbContextOptions<TContext>` and re-registers DbContext directly (NOT via `AddGroundUpPostgres`) to avoid double-registering interceptors
- All test data uses GUID-suffixed titles and `ContainsFilters[Title]` for isolation in the shared database
- Property tests use FsCheck.Xunit with custom Arbitrary generators for valid TodoItem payloads
