# Requirements Document

## Introduction

This document specifies the requirements for the HTTP-level integration test infrastructure in the GroundUp framework (Phase 4, GU-30). The unit test infrastructure (GU-29) is already complete with 189 unit tests. This phase builds the WebApplicationFactory-based integration test infrastructure that exercises the full HTTP stack — from HTTP request through controllers, services, and repositories down to a real Postgres database via Testcontainers — and validates the Sample app's TodoItem CRUD endpoints end-to-end.

## Glossary

- **GroundUp_Tests_Common**: A new framework project at `src/GroundUp.Tests.Common/` published as a NuGet package containing reusable base test classes (`GroundUpWebApplicationFactory`, `IntegrationTestBase`, `TestAuthHandler`) that consuming applications reference to set up their own integration tests.
- **Custom_Web_Application_Factory**: A generic abstract test fixture `GroundUpWebApplicationFactory<TEntryPoint, TContext>` extending `WebApplicationFactory<TEntryPoint>` that boots a consuming app's test host with a Testcontainers Postgres instance, replaces the connection string, and runs EF Core migrations automatically. Consuming apps subclass this with their own entry point and DbContext types.
- **Base_Integration_Test**: An abstract base class that accepts an `HttpClient`, provides JSON serialization helpers, and manages scope and cleanup for each test class.
- **Test_Auth_Handler**: A scaffolded ASP.NET Core authentication handler that auto-authenticates all requests. Not actively enforced yet (no auth in the system), but prepared for Phase 9 when authentication is introduced.
- **Sample_Test_Project**: The new `GroundUp.Sample.Tests.Integration` xUnit test project at `samples/GroundUp.Sample.Tests.Integration/` that demonstrates how a consuming application uses `GroundUp_Tests_Common` to build its own integration test suite.
- **Framework_Test_Projects**: The existing `GroundUp.Tests.Unit` and `GroundUp.Tests.Integration` projects at `tests/` that test the framework's own code. Not distributed as NuGet packages. Unchanged by this phase.
- **Sample_App**: The `GroundUp.Sample` ASP.NET Core web application at `samples/GroundUp.Sample` that hosts TodoItem, Customer, and Order CRUD endpoints.
- **Testcontainers**: A library that manages Docker containers (Postgres) for integration tests, providing real database instances that start and stop automatically.
- **OperationResult**: The framework's standard response wrapper (`OperationResult<T>`) returned by all API endpoints.
- **Correlation_ID**: A unique identifier propagated via the `X-Correlation-Id` HTTP header for request tracing.
- **Pagination_Headers**: HTTP response headers (`X-Total-Count`, `X-Page-Number`, `X-Page-Size`, `X-Total-Pages`) added by the BaseController on paginated list responses.
- **TodoItem_Endpoint**: The REST endpoint at `api/todoitems` exposing CRUD operations for the TodoItem entity.

## Requirements

### Requirement 1: Custom Web Application Factory

**User Story:** As a framework developer, I want a reusable test fixture that boots the Sample app with a real Testcontainers Postgres instance, so that integration tests exercise the full HTTP stack against a real database.

#### Acceptance Criteria

1. THE Custom_Web_Application_Factory SHALL be a generic abstract class `GroundUpWebApplicationFactory<TEntryPoint, TContext>` extending `WebApplicationFactory<TEntryPoint>` where `TEntryPoint` is the consuming app's entry point class and `TContext` is the consuming app's DbContext inheriting from `GroundUpDbContext`.
2. WHEN the Custom_Web_Application_Factory is initialized, THE Custom_Web_Application_Factory SHALL start a Testcontainers Postgres container using the `postgres:16-alpine` image.
3. WHEN the test host is configured, THE Custom_Web_Application_Factory SHALL replace the DbContext registration with one pointing to the Testcontainers Postgres connection string, without double-registering interceptors or hosted services already registered by the consuming app.
4. WHEN the test host starts, THE Custom_Web_Application_Factory SHALL run EF Core migrations automatically against the Testcontainers Postgres instance.
5. WHEN the Custom_Web_Application_Factory is disposed, THE Custom_Web_Application_Factory SHALL stop and dispose the Testcontainers Postgres container.
6. THE Custom_Web_Application_Factory SHALL implement `IAsyncLifetime` so that xUnit manages the container lifecycle per test collection.

### Requirement 2: Base Integration Test Class

**User Story:** As a framework developer, I want a base class for HTTP integration tests that provides an HttpClient and JSON helpers, so that individual test classes have minimal boilerplate.

#### Acceptance Criteria

1. THE Base_Integration_Test SHALL accept an `HttpClient` (created from the factory by the test class constructor) for sending HTTP requests to the test host.
2. THE Base_Integration_Test SHALL provide a helper method for deserializing JSON response bodies into `OperationResult<T>` instances.
3. THE Base_Integration_Test SHALL provide a helper method for serializing objects to `StringContent` with `application/json` media type.
4. THE Base_Integration_Test SHALL be an abstract class that test classes inherit from, receiving the `HttpClient` via constructor parameter.

### Requirement 3: Test Authentication Handler Scaffold

**User Story:** As a framework developer, I want a scaffolded authentication handler that auto-authenticates all test requests, so that the infrastructure is ready for Phase 9 when authentication is introduced.

#### Acceptance Criteria

1. THE Test_Auth_Handler SHALL implement `AuthenticationHandler<AuthenticationSchemeOptions>` from ASP.NET Core.
2. WHEN a request is authenticated, THE Test_Auth_Handler SHALL return a successful `AuthenticateResult` with a `ClaimsPrincipal` containing a default test user identity.
3. THE Test_Auth_Handler SHALL be registered in the Custom_Web_Application_Factory service configuration but not actively enforced until authentication is added to the system.

### Requirement 4: TodoItem Create Integration Test

**User Story:** As a framework developer, I want to verify that creating a TodoItem via HTTP returns a 201 response with a GUID identifier and Location header, so that the full create flow is validated end-to-end.

#### Acceptance Criteria

1. WHEN a valid TodoItem JSON payload is sent via POST to the TodoItem_Endpoint, THE Sample_App SHALL return HTTP status code 201.
2. WHEN a valid TodoItem JSON payload is sent via POST to the TodoItem_Endpoint, THE response body SHALL contain an OperationResult with a `Data` object that includes a non-empty GUID `Id` field.
3. WHEN a valid TodoItem JSON payload is sent via POST to the TodoItem_Endpoint, THE response SHALL include a `Location` header containing a URI for the created resource.

### Requirement 5: TodoItem Get All Integration Test

**User Story:** As a framework developer, I want to verify that retrieving all TodoItems via HTTP returns paginated results with pagination headers, so that the list endpoint is validated end-to-end.

#### Acceptance Criteria

1. WHEN a GET request is sent to the TodoItem_Endpoint, THE Sample_App SHALL return HTTP status code 200.
2. WHEN a GET request is sent to the TodoItem_Endpoint, THE response body SHALL contain an OperationResult wrapping a PaginatedData object with an `Items` list.
3. WHEN a GET request is sent to the TodoItem_Endpoint after creating one or more TodoItems, THE response SHALL include an `X-Total-Count` header with a value matching the number of created items.
4. WHEN a GET request is sent to the TodoItem_Endpoint, THE response SHALL include `X-Page-Number`, `X-Page-Size`, and `X-Total-Pages` headers with valid integer values.

### Requirement 6: TodoItem Get By Id Integration Test

**User Story:** As a framework developer, I want to verify that retrieving a specific TodoItem by its GUID returns the correct item, so that the single-item retrieval flow is validated end-to-end.

#### Acceptance Criteria

1. WHEN a GET request is sent to the TodoItem_Endpoint with a valid existing GUID, THE Sample_App SHALL return HTTP status code 200.
2. WHEN a GET request is sent to the TodoItem_Endpoint with a valid existing GUID, THE response body SHALL contain an OperationResult with a `Data` object whose `Id` matches the requested GUID.
3. WHEN a GET request is sent to the TodoItem_Endpoint with a valid existing GUID, THE response body `Data` object SHALL contain the same `Title` and `Description` values that were used during creation.

### Requirement 7: TodoItem Update Integration Test

**User Story:** As a framework developer, I want to verify that updating a TodoItem via HTTP returns the updated item, so that the update flow is validated end-to-end.

#### Acceptance Criteria

1. WHEN a PUT request with a modified TodoItem JSON payload is sent to the TodoItem_Endpoint with a valid existing GUID, THE Sample_App SHALL return HTTP status code 200.
2. WHEN a PUT request with a modified `Title` is sent to the TodoItem_Endpoint, THE response body SHALL contain an OperationResult with a `Data` object reflecting the updated `Title` value.
3. WHEN a TodoItem is updated via PUT and subsequently retrieved via GET, THE retrieved TodoItem SHALL reflect the updated values.

### Requirement 8: TodoItem Delete Integration Test

**User Story:** As a framework developer, I want to verify that deleting a TodoItem via HTTP removes it from subsequent GET responses, so that the delete flow is validated end-to-end.

#### Acceptance Criteria

1. WHEN a DELETE request is sent to the TodoItem_Endpoint with a valid existing GUID, THE Sample_App SHALL return HTTP status code 200.
2. WHEN a TodoItem is deleted via DELETE and subsequently a GET request is sent to the TodoItem_Endpoint for the same GUID, THE Sample_App SHALL return HTTP status code 404.
3. WHEN a TodoItem is deleted via DELETE and subsequently a GET-all request is sent to the TodoItem_Endpoint, THE deleted TodoItem SHALL not appear in the response `Items` list.

### Requirement 9: TodoItem Filtering Integration Test

**User Story:** As a framework developer, I want to verify that filtering TodoItems by title via query parameters returns only matching items, so that the filtering pipeline is validated end-to-end.

#### Acceptance Criteria

1. WHEN multiple TodoItems with distinct, uniquely-generated titles are created and a GET request is sent to the TodoItem_Endpoint with a `Filters[Title]` query parameter matching one of those unique titles, THE response `Items` list SHALL contain only TodoItems whose `Title` matches the filter value.
2. WHEN a GET request is sent to the TodoItem_Endpoint with a `Filters[Title]` query parameter that matches no items, THE response `Items` list SHALL be empty.

### Requirement 10: TodoItem Paging Integration Test

**User Story:** As a framework developer, I want to verify that paging TodoItems via query parameters returns the correct page of results, so that the pagination pipeline is validated end-to-end.

#### Acceptance Criteria

1. WHEN five or more TodoItems with a unique title prefix are created and a GET request is sent to the TodoItem_Endpoint with `PageSize=2`, `PageNumber=1`, and a `ContainsFilters[Title]` matching the unique prefix, THE response `Items` list SHALL contain exactly 2 items.
2. WHEN five or more TodoItems with a unique title prefix are created and a GET request is sent to the TodoItem_Endpoint with `PageSize=2` and a `ContainsFilters[Title]` matching the unique prefix, THE `X-Total-Count` header SHALL reflect the total number of items matching the prefix.
3. WHEN five or more TodoItems with a unique title prefix are created and a GET request is sent to the TodoItem_Endpoint with `PageSize=2` and a `ContainsFilters[Title]` matching the unique prefix, THE `X-Total-Pages` header SHALL equal the ceiling of matching items divided by 2.

### Requirement 11: TodoItem Sorting Integration Test

**User Story:** As a framework developer, I want to verify that sorting TodoItems via query parameters returns items in the correct order, so that the sorting pipeline is validated end-to-end.

#### Acceptance Criteria

1. WHEN multiple TodoItems with distinct, uniquely-generated titles sharing a common prefix are created and a GET request is sent to the TodoItem_Endpoint with `SortBy=Title` and a `ContainsFilters[Title]` matching the common prefix, THE response `Items` list SHALL be ordered alphabetically by `Title` in ascending order.
2. WHEN multiple TodoItems with distinct, uniquely-generated titles sharing a common prefix are created and a GET request is sent to the TodoItem_Endpoint with `SortBy=Title desc` and a `ContainsFilters[Title]` matching the common prefix, THE response `Items` list SHALL be ordered alphabetically by `Title` in descending order.

### Requirement 12: Correlation ID Integration Tests

**User Story:** As a framework developer, I want to verify that the Correlation ID middleware correctly echoes provided correlation IDs and generates new ones when absent, so that request tracing is validated end-to-end.

#### Acceptance Criteria

1. WHEN an HTTP request is sent to the TodoItem_Endpoint with an `X-Correlation-Id` header value, THE response SHALL include an `X-Correlation-Id` header with the same value that was sent.
2. WHEN an HTTP request is sent to the TodoItem_Endpoint without an `X-Correlation-Id` header, THE response SHALL include an `X-Correlation-Id` header with a non-empty generated value.

### Requirement 13: Test Independence and Isolation

**User Story:** As a framework developer, I want all integration tests to be independent and runnable in any order, so that test results are deterministic and reliable.

#### Acceptance Criteria

1. THE Sample_Test_Project SHALL use a shared Testcontainers Postgres container across all HTTP integration tests within the same xUnit collection to minimize container startup overhead.
2. WHEN the Custom_Web_Application_Factory applies migrations, THE database schema SHALL be created fresh so that each test collection starts with an empty database.
3. THE integration tests SHALL not depend on execution order — each test SHALL create its own test data with uniquely-generated identifiers (e.g., GUID-suffixed titles) before asserting, and SHALL filter queries to only match its own data.
4. THE Sample_Test_Project SHALL use real Postgres via Testcontainers and SHALL NOT use EF Core InMemory provider.

### Requirement 14: Program Class Accessibility

**User Story:** As a framework developer, I want the Sample app's `Program` class to be accessible from the test project, so that `WebApplicationFactory<Program>` can boot the test host.

#### Acceptance Criteria

1. THE Sample_App SHALL expose its `Program` class as public by adding `public partial class Program { }` at the bottom of `Program.cs`, following the standard ASP.NET Core pattern for `WebApplicationFactory` compatibility.
2. THE GroundUp_Tests_Common documentation SHALL note that consuming applications using top-level statements must expose their `Program` class for `WebApplicationFactory` to work.
