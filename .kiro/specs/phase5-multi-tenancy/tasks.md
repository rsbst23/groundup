# Implementation Plan: Phase 5 — Multi-Tenancy

## Overview

Prove that `BaseTenantRepository` correctly isolates data by tenant end-to-end. This phase delivers a concrete `TenantContext` implementation, `TenantResolutionMiddleware` for HTTP header-based tenant hydration, a tenant-scoped `Project` entity exercising the full stack, and integration tests proving cross-tenant data is invisible, unmodifiable, and undeletable. All code is C# targeting .NET 8.

Implementation follows: feature branch → TenantContext + DI registration → TenantResolutionMiddleware + pipeline update → Project full stack (entity → DTO → mapper → repository → service → controller → DbContext + migration) → Program.cs DI → unit tests → property-based tests → integration tests → final verification.

## Tasks

- [x] 1. Create feature branch and verify clean starting point
  - [x] 1.1 Create and checkout branch `phase-5/multi-tenancy` from `main`
    - Run `git checkout -b phase-5/multi-tenancy`
    - Run `dotnet build groundup.sln` to verify clean starting point
    - Run `dotnet test` to verify all existing tests pass
    - _Requirements: 1.1_

- [x] 2. Implement TenantContext and DI registration
  - [x] 2.1 Create `TenantContext` in `src/GroundUp.Core/TenantContext.cs`
    - Sealed class in `GroundUp.Core` namespace implementing `GroundUp.Core.Abstractions.ITenantContext`
    - Settable `TenantId` property of type `Guid` — defaults to `Guid.Empty` (C# struct default)
    - No constructor parameters, no dependencies, no HTTP-specific types
    - Add XML documentation: class-level doc explaining it's a scoped value holder, property-level doc
    - _Requirements: 1.1, 1.2, 1.4_
  - [x] 2.2 Update `AddGroundUpApi()` in `src/GroundUp.Api/ApiServiceCollectionExtensions.cs`
    - Add `using GroundUp.Core;` and `using GroundUp.Core.Abstractions;`
    - Register dual DI: `services.AddScoped<TenantContext>()` and `services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>())`
    - This ensures middleware resolves concrete `TenantContext` to set TenantId, repositories resolve `ITenantContext` to read it, and both get the same scoped instance
    - Update XML documentation to reflect the new registrations
    - _Requirements: 1.3_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add TenantContext implementation and dual DI registration"

- [x] 3. Implement TenantResolutionMiddleware and update pipeline
  - [x] 3.1 Create `TenantResolutionMiddleware` in `src/GroundUp.Api/Middleware/TenantResolutionMiddleware.cs`
    - Sealed class in `GroundUp.Api.Middleware` namespace
    - Define `public const string HeaderName = "X-Tenant-Id"`
    - Constructor: accept `RequestDelegate next`, store in private readonly field
    - `InvokeAsync(HttpContext context, TenantContext tenantContext)` — method injection for scoped `TenantContext`
      - If `context.Request.Headers.TryGetValue(HeaderName, out var values)` and `Guid.TryParse(values.FirstOrDefault(), out var tenantId)`, set `tenantContext.TenantId = tenantId`
      - If header is missing or invalid, TenantId stays `Guid.Empty` — no 400/401 response
      - Call `await _next(context)`
    - Add XML documentation: class-level doc clearly stating this is temporary (Phase 9 replaces with JWT), method-level doc
    - _Requirements: 1a.1, 1a.2, 1a.3, 1a.5_
  - [x] 3.2 Update `UseGroundUpMiddleware()` in `src/GroundUp.Api/GroundUpApplicationBuilderExtensions.cs`
    - Add `TenantResolutionMiddleware` between `CorrelationIdMiddleware` and `ExceptionHandlingMiddleware`
    - Order: CorrelationId → TenantResolution → ExceptionHandling
    - Update XML documentation to reflect the new middleware ordering
    - _Requirements: 1a.4_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add TenantResolutionMiddleware with X-Tenant-Id header parsing"

- [x] 4. Checkpoint — Verify framework changes compile and existing tests pass
  - Run `dotnet build groundup.sln` — zero errors
  - Run `dotnet test` — all existing tests pass (no regressions from middleware insertion)
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement Project entity full stack in Sample app
  - [x] 5.1 Create `Project` entity in `samples/GroundUp.Sample/Entities/Project.cs`
    - Extends `BaseEntity`, implements `ITenantEntity` and `IAuditable`
    - Properties: `Name` (string, default empty), `Description` (string?, nullable), `TenantId` (Guid), `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`
    - Same pattern as `TodoItem` but with `ITenantEntity` instead of `ISoftDeletable`
    - _Requirements: 2.1, 2.2_
  - [x] 5.2 Create `ProjectDto` in `samples/GroundUp.Sample/Dtos/ProjectDto.cs`
    - Properties: `Id` (Guid), `Name` (string, default empty), `Description` (string?, nullable)
    - No `TenantId` in the DTO — repository auto-stamps it
    - _Requirements: 2.3_
  - [x] 5.3 Create `ProjectMapper` in `samples/GroundUp.Sample/Mappers/ProjectMapper.cs`
    - Mapperly `[Mapper]` attribute, static partial class
    - `public static partial ProjectDto ToDto(Project entity)`
    - `public static partial Project ToEntity(ProjectDto dto)`
    - Same pattern as `TodoItemMapper`
    - _Requirements: 2.4_
  - [x] 5.4 Create `ProjectRepository` in `samples/GroundUp.Sample/Repositories/ProjectRepository.cs`
    - Extends `BaseTenantRepository<Project, ProjectDto>` (NOT `BaseRepository`)
    - Constructor: accept `SampleDbContext context` and `ITenantContext tenantContext`
    - Pass `context`, `tenantContext`, `ProjectMapper.ToDto`, `ProjectMapper.ToEntity` to base
    - _Requirements: 2.5_
  - [x] 5.5 Create `ProjectService` in `samples/GroundUp.Sample/Services/ProjectService.cs`
    - Extends `BaseService<ProjectDto>`
    - Constructor: accept `IBaseRepository<ProjectDto> repository`, `IEventBus eventBus`, optional `IValidator<ProjectDto>? validator = null`
    - Same pattern as `TodoItemService`
    - _Requirements: 2.6_
  - [x] 5.6 Create `ProjectsController` in `samples/GroundUp.Sample/Controllers/ProjectsController.cs`
    - Extends `BaseController<ProjectDto>`
    - Constructor: accept `BaseService<ProjectDto> service`
    - One-line overrides with HTTP attributes: `[HttpGet]` GetAll, `[HttpGet("{id}")]` GetById, `[HttpPost]` Create, `[HttpPut("{id}")]` Update, `[HttpDelete("{id}")]` Delete
    - Same pattern as `TodoItemsController`
    - _Requirements: 2.7_
  - [x] 5.7 Update `SampleDbContext` in `samples/GroundUp.Sample/Data/SampleDbContext.cs`
    - Add `public DbSet<Project> Projects => Set<Project>();`
    - Add Fluent API configuration in `OnModelCreating`:
      - `entity.HasKey(e => e.Id)`
      - `entity.Property(e => e.Name).IsRequired().HasMaxLength(200)`
      - `entity.Property(e => e.Description).HasMaxLength(1000)`
      - `entity.HasIndex(e => e.TenantId)` — important for query performance
    - _Requirements: 2.8_
  - [x] 5.8 Generate EF Core migration for the Project table
    - Run `dotnet ef migrations add AddProject --project samples/GroundUp.Sample --context SampleDbContext`
    - Verify migration creates Projects table with Id, Name, Description, TenantId, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy columns and TenantId index
    - _Requirements: 2.9_
  - [x] 5.9 Register Project services in `samples/GroundUp.Sample/Program.cs`
    - Add `using GroundUp.Sample.Dtos;` and `using GroundUp.Sample.Repositories;` and `using GroundUp.Sample.Services;` (if not already present)
    - Add DI registrations after existing registrations:
      - `builder.Services.AddScoped<IBaseRepository<ProjectDto>, ProjectRepository>();`
      - `builder.Services.AddScoped<BaseService<ProjectDto>, ProjectService>();`
    - Note: `TenantContext`/`ITenantContext` registration happens inside `AddGroundUpApi()`, not here
    - _Requirements: 2.10_
  - Run `dotnet build groundup.sln` to verify compilation
  - Commit: "Add Project entity full stack — entity, DTO, mapper, repository, service, controller, migration"

- [x] 6. Checkpoint — Verify Project full stack compiles and existing tests pass
  - Run `dotnet build groundup.sln` — zero errors
  - Run `dotnet test` — all existing tests pass
  - Verify all 9 new files exist: Project.cs, ProjectDto.cs, ProjectMapper.cs, ProjectRepository.cs, ProjectService.cs, ProjectsController.cs, updated SampleDbContext.cs, migration files, updated Program.cs
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Write unit tests for TenantContext and TenantResolutionMiddleware
  - [x] 7.1 Create `tests/GroundUp.Tests.Unit/Core/TenantContextTests.cs`
    - `TenantContext_DefaultTenantId_IsGuidEmpty` — new instance has `TenantId == Guid.Empty`
    - `TenantContext_SetTenantId_ReturnsSameValue` — set a known GUID, read it back, assert equal
    - _Requirements: 1.1, 1.2_
  - [x] 7.2 Create `tests/GroundUp.Tests.Unit/Api/TenantResolutionMiddlewareTests.cs`
    - `InvokeAsync_ValidGuidHeader_SetsTenantId` — create `DefaultHttpContext` with `X-Tenant-Id` header set to a known GUID string, create `TenantContext` instance, invoke middleware, assert `tenantContext.TenantId` equals the known GUID
    - `InvokeAsync_InvalidHeader_LeavesGuidEmpty` — set `X-Tenant-Id` to `"not-a-guid"`, invoke middleware, assert `tenantContext.TenantId == Guid.Empty`
    - `InvokeAsync_MissingHeader_LeavesGuidEmpty` — no `X-Tenant-Id` header, invoke middleware, assert `tenantContext.TenantId == Guid.Empty`
    - `InvokeAsync_CallsNextDelegate` — track with boolean flag, assert next was called
    - Use `DefaultHttpContext` and instantiate `TenantContext` directly (not mocked)
    - _Requirements: 1a.2, 1a.3_
  - Run `dotnet test` to verify all tests pass
  - Commit: "Add TenantContext and TenantResolutionMiddleware unit tests"

- [ ] 8. Write property-based tests for TenantResolutionMiddleware
  - [ ]* 8.1 Write property test for valid GUID header parsing
    - Create `tests/GroundUp.Tests.Unit/Api/TenantResolutionMiddlewarePropertyTests.cs`
    - **Property 1: Middleware parses valid GUID headers**
    - For any valid `Guid` generated by FsCheck, convert to string, set as `X-Tenant-Id` header on `DefaultHttpContext`, create `TenantContext` instance, invoke middleware, assert `tenantContext.TenantId` equals the generated GUID
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag: `Feature: phase5-multi-tenancy, Property 1: Middleware parses valid GUID headers`
    - **Validates: Requirements 1a.2**
  - [ ]* 8.2 Write property test for invalid header handling
    - **Property 2: Middleware ignores invalid headers**
    - For any `NonNull<string>` generated by FsCheck that is NOT a valid GUID (filter with `Guid.TryParse` returning false), set as `X-Tenant-Id` header on `DefaultHttpContext`, create `TenantContext` instance, invoke middleware, assert `tenantContext.TenantId == Guid.Empty`
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag: `Feature: phase5-multi-tenancy, Property 2: Middleware ignores invalid headers`
    - **Validates: Requirements 1a.3**
  - Run `dotnet test` to verify property tests pass
  - Commit: "Add TenantResolutionMiddleware property-based tests (2 properties)"

- [x] 9. Checkpoint — Verify all unit and property tests pass
  - Run `dotnet test` — all tests pass including new TenantContext tests, middleware tests, and property tests
  - Ensure all existing tests from previous phases still pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement tenant isolation integration tests
  - [x] 10.1 Create `samples/GroundUp.Sample.Tests.Integration/Http/TenantIsolationTests.cs`
    - `[Collection("Api")]`, sealed class extending `IntegrationTestBase`
    - Define `private static readonly Guid TenantA = Guid.NewGuid()` and `private static readonly Guid TenantB = Guid.NewGuid()` — unique per test run
    - Define `private const string Endpoint = "/api/projects"`
    - Constructor: accept `SampleApiFactory factory`, pass `factory.CreateClient()` to base
    - Store `factory` reference for creating additional clients if needed
    - Create private helper method `CreateTenantRequest(HttpMethod method, string url, Guid tenantId)` that returns `HttpRequestMessage` with `X-Tenant-Id` header set
    - _Requirements: 5.7, 5.8, 5.9_
  - [x] 10.2 Write test: `SameTenant_Create_ThenGetAll_ReturnsProject`
    - Create a Project as TenantA (POST with X-Tenant-Id header), assert 201
    - GetAll as TenantA (GET with X-Tenant-Id header), assert the created project is in the results
    - Use GUID-suffixed Name for test isolation
    - _Requirements: 3.1, 5.1_
  - [x] 10.3 Write test: `CrossTenant_GetAll_ReturnsEmpty`
    - Create a Project as TenantA, assert 201
    - GetAll as TenantB, filter by the unique Name, assert zero items returned
    - _Requirements: 3.2, 5.2_
  - [x] 10.4 Write test: `CrossTenant_GetById_Returns404`
    - Create a Project as TenantA, extract the Id
    - GetById as TenantB using that Id, assert 404 NotFound
    - _Requirements: 3.3, 5.3_
  - [x] 10.5 Write test: `CrossTenant_Update_Returns404_DataUnchanged`
    - Create a Project as TenantA with known Name, extract the Id
    - Attempt PUT as TenantB with modified Name, assert 404 NotFound
    - GetById as TenantA, assert original Name is unchanged
    - _Requirements: 4.1, 5.4_
  - [x] 10.6 Write test: `CrossTenant_Delete_Returns404_DataStillExists`
    - Create a Project as TenantA, extract the Id
    - Attempt DELETE as TenantB, assert 404 NotFound
    - GetById as TenantA, assert Project still exists
    - _Requirements: 4.2, 5.5_
  - [x] 10.7 Write test: `MultiTenant_GetAll_ReturnsOnlyOwnData`
    - Create 2 Projects as TenantA with unique prefix, create 2 Projects as TenantB with different prefix
    - GetAll as TenantA filtered by TenantA's prefix, assert exactly 2 items returned and none of TenantB's
    - _Requirements: 3.4, 5.6_
  - Run `dotnet test` to verify all integration tests pass
  - Commit: "Add tenant isolation integration tests (6 tests)"

- [x] 11. Checkpoint — Verify all tests pass including integration tests
  - Run `dotnet test` — all tests pass (unit, property, and integration)
  - Ensure all existing tests from previous phases still pass
  - Ensure all tests pass, ask the user if questions arise.

- [-] 12. Final verification — Full solution build and test
  - Run `dotnet build groundup.sln` — zero errors
  - Run `dotnet test` — all tests pass
  - Verify coding conventions: file-scoped namespaces, nullable reference types, XML documentation, sealed modifiers, one-class-per-file across all new files
  - Verify no `[Authorize]` attributes, no direct HttpContext access in services/repositories
  - Commit: "Phase 5 complete — multi-tenancy proven end-to-end"
  - Push branch with `-u` flag: `git push -u origin phase-5/multi-tenancy`

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component
- Property tests (tasks 8.1, 8.2) validate correctness properties 1 and 2 from the design document using FsCheck.Xunit
- Properties 3, 4, and 5 (tenant isolation) are validated by the integration tests — each test uses unique GUID pairs per test run
- The `TenantResolutionMiddleware` is temporary — Phase 9 replaces it with JWT-based tenant resolution
- `TenantContext` lives in `GroundUp.Core` (not `GroundUp.Api`) so it works in SDK, console, and background job contexts
- The `Project` entity does NOT implement `ISoftDeletable` — delete is a hard delete, keeping it simple for proving tenant isolation
- All integration test data uses GUID-suffixed names and unique tenant GUIDs for isolation in the shared database
- Git workflow: feature branch `phase-5/multi-tenancy`, commit after each compilable step, push with `-u` on first push
