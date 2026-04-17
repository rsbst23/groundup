# Requirements Document

## Introduction

Phase 1 of the GroundUp framework establishes the solution structure and foundational types. This phase creates the monorepo solution file, all initial project files with correct inter-project references, the ARCHITECTURE.md documentation, and the core types in GroundUp.Core that downstream phases (Event Bus, Base Repository, Data Layer) depend on. Everything must compile. No business logic yet — only the structural skeleton and shared type definitions.

Note on naming: Only the API layer project uses `.Api` in its name (`GroundUp.Api`). All other projects use `GroundUp.*` without `.Api`, because they work equally well in SDK-only scenarios where no API layer is present.

## Glossary

- **Solution**: The Visual Studio solution file (groundup.sln) that references all projects in the monorepo.
- **Core_Project**: The GroundUp.Core class library project containing foundational types shared by all layers with zero external NuGet dependencies.
- **Events_Project**: The GroundUp.Events class library project containing event abstractions and in-process event bus support.
- **Data_Abstractions**: The GroundUp.Data.Abstractions class library project containing repository and data access interfaces.
- **Repositories_Project**: The GroundUp.Repositories class library project containing base repository implementations.
- **Data_Postgres**: The GroundUp.Data.Postgres class library project containing Postgres-specific EF Core setup.
- **Services_Project**: The GroundUp.Services class library project containing the base service layer.
- **Api_Project**: The GroundUp.Api class library project (not a web project) containing base controllers and middleware.
- **Sample_App**: The GroundUp.Sample ASP.NET Core web application used for manual testing and as a reference for consuming GroundUp.
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **Integration_Test_Project**: The GroundUp.Tests.Integration xUnit test project for integration tests.
- **BaseEntity**: An abstract class providing a Guid Id property as the root entity type for all framework entities.
- **OperationResult**: A generic result wrapper (OperationResult&lt;T&gt;) used as the single standardized return type across all layers.
- **FilterParams**: A parameter class extending PaginationParams that carries filtering, searching, and sorting criteria for repository queries.
- **PaginatedData**: A generic wrapper (PaginatedData&lt;T&gt;) that holds a page of results along with pagination metadata.
- **ARCHITECTURE_MD**: The ARCHITECTURE.md file in the repository root documenting project structure, dependency rules, and design decisions.

## Requirements

### Requirement 1: Create Solution File

**User Story:** As a framework developer, I want a single solution file that references all initial projects, so that I can build and navigate the entire monorepo from one entry point.

#### Acceptance Criteria

1. THE Solution SHALL contain a groundup.sln file in the repository root that references all ten initial projects (Core_Project, Events_Project, Data_Abstractions, Repositories_Project, Data_Postgres, Services_Project, Api_Project, Sample_App, Unit_Test_Project, Integration_Test_Project).
2. THE Solution SHALL organize projects into solution folders: "src" for library projects, "samples" for the Sample_App, and "tests" for test projects.
3. WHEN `dotnet build groundup.sln` is executed, THE Solution SHALL compile with zero errors and zero warnings treated as errors.

### Requirement 2: Create Core Project

**User Story:** As a framework developer, I want a foundational class library with zero external NuGet dependencies, so that all layers can reference shared types without pulling in transitive dependencies.

#### Acceptance Criteria

1. THE Core_Project SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Core_Project SHALL have zero NuGet package dependencies.
3. THE Core_Project SHALL have zero project references to other GroundUp projects.
4. THE Core_Project SHALL enable XML documentation generation in its project file.

### Requirement 3: Create Events Project

**User Story:** As a framework developer, I want an events project that depends only on Api.Core, so that event abstractions remain decoupled from all other layers.

#### Acceptance Criteria

1. THE Events_Project SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Events_Project SHALL reference Core_Project as its only project dependency.
3. THE Events_Project SHALL have zero NuGet package dependencies.

### Requirement 4: Create Data Abstractions Project

**User Story:** As a framework developer, I want a data abstractions project containing repository interfaces, so that the service layer remains database-agnostic.

#### Acceptance Criteria

1. THE Data_Abstractions SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Data_Abstractions SHALL reference Core_Project as its only project dependency.

### Requirement 5: Create Repositories Project

**User Story:** As a framework developer, I want a repositories project that references Api.Core, Data.Abstractions, and Events, so that base repository implementations can use shared types, implement data interfaces, and publish domain events.

#### Acceptance Criteria

1. THE Repositories_Project SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Repositories_Project SHALL reference exactly three projects: Core_Project, Data_Abstractions, and Events_Project.

### Requirement 6: Create Data.Postgres Project

**User Story:** As a framework developer, I want a Postgres-specific data project with EF Core and Npgsql dependencies, so that Postgres-specific setup is isolated from database-agnostic code.

#### Acceptance Criteria

1. THE Data_Postgres SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Data_Postgres SHALL reference Core_Project, Data_Abstractions, and Repositories_Project as project dependencies.
3. THE Data_Postgres SHALL include NuGet package references for EF Core and the Npgsql EF Core provider.

### Requirement 7: Create Services Project

**User Story:** As a framework developer, I want a services project that references Api.Core, Data.Abstractions, and Events, so that the service layer can orchestrate business logic using repository interfaces and publish events.

#### Acceptance Criteria

1. THE Services_Project SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Services_Project SHALL reference exactly three projects: Core_Project, Data_Abstractions, and Events_Project.
3. THE Services_Project SHALL include a NuGet package reference for FluentValidation.

### Requirement 8: Create Api Project

**User Story:** As a framework developer, I want an Api class library (not a web project) that references Api.Core and Services, so that base controllers and middleware are reusable without forcing a web host dependency.

#### Acceptance Criteria

1. THE Api_Project SHALL be a class library targeting net8.0 with nullable reference types enabled.
2. THE Api_Project SHALL reference exactly two projects: Core_Project and Services_Project.
3. THE Api_Project SHALL be a class library, not an ASP.NET Core web application project.

### Requirement 9: Create Sample Application

**User Story:** As a framework consumer, I want a sample ASP.NET Core web application that references all framework projects, so that I can see how to wire up and use GroundUp modules.

#### Acceptance Criteria

1. THE Sample_App SHALL be an ASP.NET Core web application targeting net8.0 with nullable reference types enabled.
2. THE Sample_App SHALL reference all seven src projects (Core_Project, Events_Project, Data_Abstractions, Repositories_Project, Data_Postgres, Services_Project, Api_Project).

### Requirement 10: Create Test Projects

**User Story:** As a framework developer, I want xUnit test projects with appropriate testing dependencies, so that I have infrastructure ready for unit and integration tests.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL be an xUnit test project targeting net8.0 with nullable reference types enabled.
2. THE Unit_Test_Project SHALL include NuGet package references for xUnit, NSubstitute, and the xUnit runner.
3. THE Integration_Test_Project SHALL be an xUnit test project targeting net8.0 with nullable reference types enabled.
4. THE Integration_Test_Project SHALL include NuGet package references for xUnit, Testcontainers for PostgreSQL, and the ASP.NET Core test host.

### Requirement 11: Create ARCHITECTURE.md Documentation

**User Story:** As a framework developer or consumer, I want an ARCHITECTURE.md file documenting the project structure and design decisions, so that I can understand the monorepo layout and dependency rules without reading source code.

#### Acceptance Criteria

1. THE ARCHITECTURE_MD SHALL exist in the repository root.
2. THE ARCHITECTURE_MD SHALL document the purpose and responsibility of each project in the solution.
3. THE ARCHITECTURE_MD SHALL document the dependency graph and layering rules (API → Services → Repositories → Data).
4. THE ARCHITECTURE_MD SHALL document key design decisions including: framework not application, SDK-first design, and multi-tenancy at the repository layer.
5. THE ARCHITECTURE_MD SHALL document how consuming applications use the framework.

### Requirement 12: Build BaseEntity

**User Story:** As a framework developer, I want an abstract base entity class with a Guid Id property, so that all framework entities share a consistent identity type using UUID v7.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an abstract BaseEntity class with a single public Guid property named Id.
2. THE BaseEntity SHALL have an XML documentation comment describing its purpose.
3. THE BaseEntity SHALL use a file-scoped namespace.

### Requirement 13: Build Auditable Interface

**User Story:** As a framework developer, I want an IAuditable interface, so that entities can opt in to automatic audit field population by the EF Core SaveChanges interceptor.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an IAuditable interface with properties: DateTime CreatedAt, string? CreatedBy, DateTime? UpdatedAt, string? UpdatedBy.
2. THE IAuditable interface SHALL have an XML documentation comment describing its purpose and opt-in nature.

### Requirement 14: Build Soft-Deletable Interface

**User Story:** As a framework developer, I want an ISoftDeletable interface, so that entities can opt in to soft delete behavior enforced by the EF Core interceptor and global query filters.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an ISoftDeletable interface with properties: bool IsDeleted, DateTime? DeletedAt, string? DeletedBy.
2. THE ISoftDeletable interface SHALL have an XML documentation comment describing its purpose and opt-in nature.

### Requirement 15: Build Tenant Entity Interface

**User Story:** As a framework developer, I want an ITenantEntity interface, so that entities can declare tenant ownership for automatic tenant filtering in BaseTenantRepository.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an ITenantEntity interface with a single Guid TenantId property.
2. THE ITenantEntity interface SHALL have an XML documentation comment describing its purpose.

### Requirement 16: Build Current User Abstraction

**User Story:** As a framework developer, I want an ICurrentUser interface, so that all layers can access the authenticated user's identity without depending on the authentication module.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an ICurrentUser interface with properties: Guid UserId, string? Email, string? DisplayName.
2. THE ICurrentUser interface SHALL have an XML documentation comment describing its purpose.

### Requirement 17: Build Tenant Context Abstraction

**User Story:** As a framework developer, I want an ITenantContext interface, so that all layers can access the current tenant identity for automatic tenant filtering.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an ITenantContext interface with a single Guid TenantId property.
2. THE ITenantContext interface SHALL have an XML documentation comment describing its purpose.

### Requirement 18: Build OperationResult

**User Story:** As a framework developer, I want a generic OperationResult&lt;T&gt; class with static factory methods, so that all layers use a single standardized result type instead of throwing exceptions for business logic.

#### Acceptance Criteria

1. THE Core_Project SHALL contain an OperationResult&lt;T&gt; class with properties: T? Data, bool Success, string Message, List&lt;string&gt;? Errors, int StatusCode, string? ErrorCode.
2. THE OperationResult SHALL provide a static Ok factory method that creates a successful result with data, message, and status code.
3. THE OperationResult SHALL provide a static Fail factory method that creates a failure result with message, status code, optional error code, and optional error list.
4. THE OperationResult SHALL provide a static NotFound factory method that creates a 404 failure result.
5. THE OperationResult SHALL provide a static BadRequest factory method that creates a 400 failure result with an optional error list.
6. THE OperationResult SHALL provide a static Unauthorized factory method that creates a 401 failure result.
7. THE OperationResult SHALL provide a static Forbidden factory method that creates a 403 failure result.
8. THE OperationResult SHALL have XML documentation comments on the class and all public members.

### Requirement 19: Build Exception Hierarchy

**User Story:** As a framework developer, I want a typed exception hierarchy, so that cross-cutting infrastructure errors map to specific HTTP status codes in the exception handling middleware.

#### Acceptance Criteria

1. THE Core_Project SHALL contain a GroundUpException class that extends Exception and accepts a message and optional inner exception.
2. THE Core_Project SHALL contain a NotFoundException class that extends GroundUpException, used when an entity is not found by ID.
3. WHEN a GroundUpException or NotFoundException is constructed, THE exception SHALL store the provided message accessible via the Message property.
4. THE GroundUpException and NotFoundException SHALL have XML documentation comments describing their purpose and intended HTTP status code mapping.

### Requirement 20: Build Pagination Types

**User Story:** As a framework developer, I want PaginationParams and PaginatedData types, so that repositories have a standardized way to accept paging input and return paged results.

#### Acceptance Criteria

1. THE Core_Project SHALL contain a PaginationParams class with properties: int PageNumber, int PageSize, string? SortBy.
2. WHEN PageSize is set to a value exceeding the maximum allowed page size, THE PaginationParams SHALL cap PageSize at the maximum allowed value (default 100).
3. WHEN PageNumber is set to a value less than 1, THE PaginationParams SHALL default PageNumber to 1.
4. THE Core_Project SHALL contain a PaginatedData&lt;T&gt; record with properties: List&lt;T&gt; Items, int PageNumber, int PageSize, int TotalRecords, int TotalPages.
5. THE PaginatedData SHALL compute TotalPages from TotalRecords and PageSize.

### Requirement 21: Build FilterParams

**User Story:** As a framework developer, I want a FilterParams class extending PaginationParams, so that repositories support exact match, contains, range, multi-value, and search term filtering.

#### Acceptance Criteria

1. THE Core_Project SHALL contain a FilterParams class that extends PaginationParams.
2. THE FilterParams SHALL include a Filters property (Dictionary&lt;string, string&gt;) for exact-match filtering.
3. THE FilterParams SHALL include a ContainsFilters property (Dictionary&lt;string, string&gt;) for substring-match filtering.
4. THE FilterParams SHALL include MinFilters and MaxFilters properties (Dictionary&lt;string, string&gt;) for range filtering.
5. THE FilterParams SHALL include a MultiValueFilters property (Dictionary&lt;string, List&lt;string&gt;&gt;) for IN-clause filtering.
6. THE FilterParams SHALL include a SearchTerm property (string?) for free-text search.

### Requirement 22: Build ErrorCodes

**User Story:** As a framework developer, I want a static ErrorCodes class with string constants, so that error codes are standardized and discoverable across all modules.

#### Acceptance Criteria

1. THE Core_Project SHALL contain a static ErrorCodes class with string constants for common error codes (e.g., NotFound, ValidationFailed, Unauthorized, Forbidden, Conflict, InternalError).
2. THE ErrorCodes class SHALL have XML documentation comments on the class and each constant.

### Requirement 23: Enforce Coding Conventions

**User Story:** As a framework developer, I want all source files to follow the project's coding conventions, so that the codebase is consistent from the first commit.

#### Acceptance Criteria

1. THE Core_Project SHALL use file-scoped namespaces in all source files.
2. THE Core_Project SHALL enable nullable reference types in its project file.
3. THE Core_Project SHALL place each class and interface in its own separate file.
4. THE Core_Project SHALL use records for value objects and DTOs (PaginatedData, PaginationParams, FilterParams).
5. THE Core_Project SHALL use the sealed modifier on classes not designed for inheritance (OperationResult, ErrorCodes, NotFoundException).
