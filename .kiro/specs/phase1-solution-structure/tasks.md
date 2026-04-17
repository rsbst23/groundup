# Implementation Plan: Phase 1 — Solution Structure & Core Types

## Overview

Build the GroundUp framework monorepo skeleton: solution file, ten projects with correct references and NuGet dependencies, ARCHITECTURE.md, and all foundational types in GroundUp.Core. Everything must compile cleanly. No business logic — only structure and shared type definitions needed by Phase 2 (Event Bus) and Phase 3 (Base Repository & Data Layer).

Git workflow: work on branch `phase-1/solution-structure`, commit after each sub-step that compiles.

## Tasks

- [x] 1. Create solution file and all project scaffolds
  - [x] 1.1 Create the feature branch and solution file
    - Create and checkout branch `phase-1/solution-structure`
    - Create `groundup.sln` in the repository root using `dotnet new sln`
    - _Requirements: 1.1_

  - [x] 1.2 Create GroundUp.Core project
    - `dotnet new classlib` at `src/GroundUp.Core`
    - Target `net8.0`, enable nullable reference types, enable XML documentation generation (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
    - Zero NuGet dependencies, zero project references
    - Remove auto-generated `Class1.cs`
    - Add project to solution in `src` solution folder
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 1.2_

  - [x] 1.3 Create GroundUp.Events project
    - `dotnet new classlib` at `src/GroundUp.Events`
    - Target `net8.0`, enable nullable reference types
    - Add project reference to GroundUp.Core only, zero NuGet dependencies
    - Remove `Class1.cs`, add a placeholder (e.g., empty namespace file or `.gitkeep`)
    - Add project to solution in `src` solution folder
    - _Requirements: 3.1, 3.2, 3.3, 1.2_

  - [x] 1.4 Create GroundUp.Data.Abstractions project
    - `dotnet new classlib` at `src/GroundUp.Data.Abstractions`
    - Target `net8.0`, enable nullable reference types
    - Add project reference to GroundUp.Core only
    - Remove `Class1.cs`, add placeholder
    - Add project to solution in `src` solution folder
    - _Requirements: 4.1, 4.2, 1.2_

  - [x] 1.5 Create GroundUp.Repositories project
    - `dotnet new classlib` at `src/GroundUp.Repositories`
    - Target `net8.0`, enable nullable reference types
    - Add project references to exactly: GroundUp.Core, GroundUp.Data.Abstractions, GroundUp.Events
    - Remove `Class1.cs`, add placeholder
    - Add project to solution in `src` solution folder
    - _Requirements: 5.1, 5.2, 1.2_

  - [x] 1.6 Create GroundUp.Data.Postgres project
    - `dotnet new classlib` at `src/GroundUp.Data.Postgres`
    - Target `net8.0`, enable nullable reference types
    - Add project references to: GroundUp.Core, GroundUp.Data.Abstractions, GroundUp.Repositories
    - Add NuGet packages: `Microsoft.EntityFrameworkCore` (8.x), `Npgsql.EntityFrameworkCore.PostgreSQL` (8.x)
    - Remove `Class1.cs`, add placeholder
    - Add project to solution in `src` solution folder
    - _Requirements: 6.1, 6.2, 6.3, 1.2_

  - [x] 1.7 Create GroundUp.Services project
    - `dotnet new classlib` at `src/GroundUp.Services`
    - Target `net8.0`, enable nullable reference types
    - Add project references to exactly: GroundUp.Core, GroundUp.Data.Abstractions, GroundUp.Events
    - Add NuGet package: `FluentValidation` (11.x)
    - Remove `Class1.cs`, add placeholder
    - Add project to solution in `src` solution folder
    - _Requirements: 7.1, 7.2, 7.3, 1.2_

  - [x] 1.8 Create GroundUp.Api project
    - `dotnet new classlib` at `src/GroundUp.Api`
    - Target `net8.0`, enable nullable reference types
    - Add project references to exactly: GroundUp.Core, GroundUp.Services
    - This is a class library, NOT a web project
    - Remove `Class1.cs`, add placeholder
    - Add project to solution in `src` solution folder
    - _Requirements: 8.1, 8.2, 8.3, 1.2_

  - [x] 1.9 Create GroundUp.Sample application
    - `dotnet new web` at `samples/GroundUp.Sample`
    - Target `net8.0`, enable nullable reference types
    - Add project references to all seven src projects: Core, Events, Data.Abstractions, Repositories, Data.Postgres, Services, Api
    - Minimal `Program.cs` that compiles (empty pipeline)
    - Add project to solution in `samples` solution folder
    - _Requirements: 9.1, 9.2, 1.2_

  - [x] 1.10 Create GroundUp.Tests.Unit project
    - `dotnet new xunit` at `tests/GroundUp.Tests.Unit`
    - Target `net8.0`, enable nullable reference types
    - Add NuGet packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `FluentAssertions`, `FsCheck.Xunit`
    - Add project reference to GroundUp.Core
    - Add project to solution in `tests` solution folder
    - _Requirements: 10.1, 10.2, 1.2_

  - [x] 1.11 Create GroundUp.Tests.Integration project
    - `dotnet new xunit` at `tests/GroundUp.Tests.Integration`
    - Target `net8.0`, enable nullable reference types
    - Add NuGet packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`
    - Add project reference to GroundUp.Core
    - Add project to solution in `tests` solution folder
    - _Requirements: 10.3, 10.4, 1.2_

  - [x] 1.12 Verify full solution builds
    - Run `dotnet build groundup.sln` and confirm zero errors, zero warnings treated as errors
    - Commit: "feat: scaffold solution with all 10 projects and dependencies"
    - _Requirements: 1.3_

- [x] 2. Implement Core entity types and interfaces
  - [x] 2.1 Implement BaseEntity
    - Create `src/GroundUp.Core/Entities/BaseEntity.cs`
    - Abstract class with `Guid Id` property, file-scoped namespace `GroundUp.Core.Entities`
    - XML documentation comment on class and property
    - Sealed is NOT applied (designed for inheritance)
    - _Requirements: 12.1, 12.2, 12.3, 23.1_

  - [x] 2.2 Implement IAuditable
    - Create `src/GroundUp.Core/Entities/IAuditable.cs`
    - Interface with `DateTime CreatedAt`, `string? CreatedBy`, `DateTime? UpdatedAt`, `string? UpdatedBy`
    - XML documentation comment describing opt-in nature
    - _Requirements: 13.1, 13.2, 23.1, 23.3_

  - [x] 2.3 Implement ISoftDeletable
    - Create `src/GroundUp.Core/Entities/ISoftDeletable.cs`
    - Interface with `bool IsDeleted`, `DateTime? DeletedAt`, `string? DeletedBy`
    - XML documentation comment describing opt-in nature
    - _Requirements: 14.1, 14.2, 23.1, 23.3_

  - [x] 2.4 Implement ITenantEntity
    - Create `src/GroundUp.Core/Entities/ITenantEntity.cs`
    - Interface with `Guid TenantId` property
    - XML documentation comment
    - _Requirements: 15.1, 15.2, 23.1, 23.3_

  - [x] 2.5 Implement ICurrentUser and ITenantContext
    - Create `src/GroundUp.Core/Abstractions/ICurrentUser.cs` with `Guid UserId`, `string? Email`, `string? DisplayName`
    - Create `src/GroundUp.Core/Abstractions/ITenantContext.cs` with `Guid TenantId`
    - XML documentation comments on both interfaces
    - _Requirements: 16.1, 16.2, 17.1, 17.2, 23.1, 23.3_

  - [x] 2.6 Build and commit entity types
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(core): add BaseEntity, IAuditable, ISoftDeletable, ITenantEntity, ICurrentUser, ITenantContext"
    - _Requirements: 1.3_

- [x] 3. Implement OperationResult and ErrorCodes
  - [x] 3.1 Implement OperationResult&lt;T&gt;
    - Create `src/GroundUp.Core/Results/OperationResult.cs`
    - Sealed class with properties: `T? Data`, `bool Success`, `string Message`, `List<string>? Errors`, `int StatusCode`, `string? ErrorCode`
    - Static factory methods: `Ok`, `Fail`, `NotFound` (404), `BadRequest` (400), `Unauthorized` (401), `Forbidden` (403)
    - XML documentation on class and all public members
    - File-scoped namespace `GroundUp.Core.Results`
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.7, 18.8, 23.1, 23.5_

  - [x] 3.2 Implement ErrorCodes
    - Create `src/GroundUp.Core/ErrorCodes.cs`
    - Static class with string constants: `NotFound`, `ValidationFailed`, `Unauthorized`, `Forbidden`, `Conflict`, `InternalError`
    - XML documentation on class and each constant
    - File-scoped namespace `GroundUp.Core`
    - _Requirements: 22.1, 22.2, 23.1, 23.5_

  - [x] 3.3 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(core): add OperationResult<T> and ErrorCodes"
    - _Requirements: 1.3_

  - [x]* 3.4 Write property test — Ok factory preserves data and marks success
    - **Property 1: Ok factory preserves data and marks success**
    - For any value of type T, `OperationResult<T>.Ok(data, message, statusCode)` produces `Success == true`, `Data` equals input data, `Message` equals input message, `StatusCode` equals input status code
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - Test class: `tests/GroundUp.Tests.Unit/Core/OperationResultPropertyTests.cs`
    - **Validates: Requirements 18.2**

  - [x]* 3.5 Write property test — Fail factory preserves error details and marks failure
    - **Property 2: Fail factory preserves error details and marks failure**
    - For any message, status code, optional error code, and optional error list, `OperationResult<T>.Fail(...)` produces `Success == false` with all inputs preserved
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 18.3**

  - [x]* 3.6 Write property test — Failure shorthand factories produce correct status codes
    - **Property 3: Failure shorthand factories produce correct status codes**
    - `NotFound` → 404, `BadRequest` → 400, `Unauthorized` → 401, `Forbidden` → 403; all have `Success == false`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 18.4, 18.5, 18.6, 18.7**

- [x] 4. Implement exception hierarchy
  - [x] 4.1 Implement GroundUpException and NotFoundException
    - Create `src/GroundUp.Core/Exceptions/GroundUpException.cs` — extends `Exception`, accepts message and optional inner exception
    - Create `src/GroundUp.Core/Exceptions/NotFoundException.cs` — sealed, extends `GroundUpException`, accepts message
    - XML documentation comments describing purpose and HTTP status code mapping
    - File-scoped namespaces
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 23.1, 23.5_

  - [x] 4.2 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(core): add GroundUpException and NotFoundException"
    - _Requirements: 1.3_

  - [x]* 4.3 Write property test — Exception constructors preserve message
    - **Property 4: Exception constructors preserve message**
    - For any non-null string, `GroundUpException(message).Message` and `NotFoundException(message).Message` return the same string
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - Test class: `tests/GroundUp.Tests.Unit/Core/ExceptionPropertyTests.cs`
    - **Validates: Requirements 19.3**

- [x] 5. Checkpoint — Verify core types compile and tests pass
  - Ensure `dotnet build groundup.sln` succeeds with zero errors
  - Run `dotnet test` on the unit test project to verify any property tests written so far pass
  - Ensure all tests pass, ask the user if questions arise.

- [-] 6. Implement pagination and filtering types
  - [x] 6.1 Implement PaginationParams
    - Create `src/GroundUp.Core/Models/PaginationParams.cs`
    - Record with `PageNumber` (defaults to 1, clamped to >= 1), `PageSize` (default 10, capped at `DefaultMaxPageSize` = 100, clamped to >= 1), `string? SortBy`
    - XML documentation comments
    - File-scoped namespace `GroundUp.Core.Models`
    - _Requirements: 20.1, 20.2, 20.3, 23.1, 23.4_

  - [x] 6.2 Implement PaginatedData&lt;T&gt;
    - Create `src/GroundUp.Core/Models/PaginatedData.cs`
    - Sealed record with `List<T> Items`, `int PageNumber`, `int PageSize`, `int TotalRecords`, computed `int TotalPages` (ceiling division, 0 when PageSize is 0)
    - XML documentation comments
    - _Requirements: 20.4, 20.5, 23.1, 23.4, 23.5_

  - [x] 6.3 Implement FilterParams
    - Create `src/GroundUp.Core/Models/FilterParams.cs`
    - Sealed record extending `PaginationParams`
    - Properties: `Dictionary<string, string> Filters`, `ContainsFilters`, `MinFilters`, `MaxFilters`, `Dictionary<string, List<string>> MultiValueFilters`, `string? SearchTerm`
    - All dictionaries initialized to `new()`
    - _Requirements: 21.1, 21.2, 21.3, 21.4, 21.5, 21.6, 23.1, 23.4, 23.5_

  - [x] 6.4 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(core): add PaginationParams, PaginatedData<T>, FilterParams"
    - _Requirements: 1.3_

  - [x]* 6.5 Write property test — PaginationParams clamps values to valid ranges
    - **Property 5: PaginationParams clamps values to valid ranges**
    - For any integer PageNumber and PageSize, resulting `PageNumber >= 1` and `1 <= PageSize <= 100`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - Test class: `tests/GroundUp.Tests.Unit/Core/PaginationParamsPropertyTests.cs`
    - **Validates: Requirements 20.2, 20.3**

  - [-]* 6.6 Write property test — PaginatedData computes TotalPages correctly
    - **Property 6: PaginatedData computes TotalPages correctly**
    - For any positive PageSize and non-negative TotalRecords, `TotalPages == ⌈TotalRecords / PageSize⌉`; when PageSize is 0, `TotalPages == 0`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - Test class: `tests/GroundUp.Tests.Unit/Core/PaginatedDataPropertyTests.cs`
    - **Validates: Requirements 20.5**

- [ ] 7. Create ARCHITECTURE.md documentation
  - [ ] 7.1 Write ARCHITECTURE.md
    - Create `ARCHITECTURE.md` in the repository root
    - Document: purpose and responsibility of each project, dependency graph and layering rules, key design decisions (framework not application, SDK-first, multi-tenancy at repository layer), how consuming applications use the framework
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

  - [ ] 7.2 Commit documentation
    - Commit: "docs: add ARCHITECTURE.md with project structure and design decisions"

- [ ] 8. Final checkpoint — Full build and test verification
  - Run `dotnet build groundup.sln` — zero errors
  - Run `dotnet test` on the unit test project — all property tests pass
  - Verify solution folder organization (src, samples, tests) is correct
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck.Xunit with `[Property]` attribute and minimum 100 iterations
- Git commits happen after each sub-step that compiles — small, frequent commits
- All code uses C# with file-scoped namespaces, nullable reference types, and `sealed` where appropriate
- The design uses specific C# code (not pseudocode), so no language selection was needed
