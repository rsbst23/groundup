# Requirements Document

## Introduction

Phase 3E of the GroundUp framework builds the API layer in GroundUp.Api — the thin HTTP adapter that sits on top of the service layer. This phase delivers `BaseController<TDto>`, an abstract generic controller wrapping `BaseService<TDto>` with standard CRUD endpoints (GET all, GET by id, POST, PUT, DELETE), two middleware components (`ExceptionHandlingMiddleware` and `CorrelationIdMiddleware`), and the `AddGroundUpApi()` DI extension method.

GroundUp.Api is a **class library, not a web project** — it provides reusable HTTP adapter components that a consuming web application references. It needs a `FrameworkReference` to `Microsoft.AspNetCore.App` to access ASP.NET Core types (ControllerBase, ActionResult, middleware pipeline) without being a web project itself.

Controllers are thin — zero business logic, zero security checks. They convert between HTTP (query strings, route parameters, request bodies) and `OperationResult<T>` from the service layer. A private helper method maps `OperationResult.StatusCode` to the appropriate `ActionResult` (200 → Ok, 201 → Created, 400 → BadRequest, 404 → NotFound, etc.).

`ExceptionHandlingMiddleware` catches unhandled exceptions that escape the controller layer and maps them to structured JSON error responses using the typed exception hierarchy — never string matching on exception messages. `CorrelationIdMiddleware` reads or generates a correlation ID per request, stores it in `HttpContext.Items`, and adds it to response headers for end-to-end traceability.

Pagination response headers (X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages) are added on list endpoints so clients can implement pagination without parsing the response body.

Health checks are deferred to Phase 3F (sample app) since they need a running host to test.

## Glossary

- **Api_Project**: The GroundUp.Api class library project containing base controllers, middleware, and API infrastructure. Depends on Core_Project and Services_Project. Uses a FrameworkReference to Microsoft.AspNetCore.App.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types (OperationResult, FilterParams, PaginatedData, PaginationParams, ErrorCodes, GroundUpException, NotFoundException).
- **Services_Project**: The GroundUp.Services class library project containing BaseService&lt;TDto&gt; and the service layer abstractions.
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **BaseController**: An abstract generic controller (BaseController&lt;TDto&gt;) in Api_Project that wraps BaseService&lt;TDto&gt; with standard CRUD endpoints and OperationResult-to-ActionResult conversion.
- **BaseService**: The abstract generic service class (BaseService&lt;TDto&gt;) in Services_Project that BaseController delegates all operations to.
- **OperationResult**: The generic (OperationResult&lt;T&gt;) and non-generic (OperationResult) result types in Core_Project used as the single standardized return type.
- **ExceptionHandlingMiddleware**: A middleware class in Api_Project that catches unhandled exceptions and maps typed exceptions to structured JSON error responses with appropriate HTTP status codes.
- **CorrelationIdMiddleware**: A middleware class in Api_Project that reads or generates a correlation ID per request, stores it in HttpContext.Items, and adds it to response headers.
- **AddGroundUpApi**: A static extension method on IServiceCollection in Api_Project that registers API-layer services (middleware, etc.) in the DI container.
- **FilterParams**: The parameter class in Core_Project carrying filtering, sorting, and pagination criteria.
- **PaginatedData**: The generic wrapper (PaginatedData&lt;T&gt;) in Core_Project that holds a page of results with pagination metadata (Items, PageNumber, PageSize, TotalRecords, TotalPages).
- **GroundUpException**: The base exception class in Core_Project for infrastructure and cross-cutting errors. Maps to HTTP 500 in ExceptionHandlingMiddleware.
- **NotFoundException**: A typed exception in Core_Project extending GroundUpException. Maps to HTTP 404 in ExceptionHandlingMiddleware.
- **CorrelationIdHeaderName**: The HTTP header name "X-Correlation-Id" used by CorrelationIdMiddleware to read and write correlation IDs.
- **ErrorResponse**: The structured JSON object returned by ExceptionHandlingMiddleware containing message, errorCode, and correlationId fields.

## Requirements

### Requirement 1: GroundUp.Api Project Configuration

**User Story:** As a framework developer, I want the GroundUp.Api project configured as a class library with a FrameworkReference to Microsoft.AspNetCore.App, so that it can use ASP.NET Core types without being a web project.

#### Acceptance Criteria

1. THE Api_Project SHALL target net8.0 with nullable reference types enabled and implicit usings enabled.
2. THE Api_Project SHALL include a FrameworkReference to Microsoft.AspNetCore.App so that ASP.NET Core types (ControllerBase, ActionResult, middleware pipeline types) are available without NuGet package references.
3. THE Api_Project SHALL retain its existing project references to Core_Project and Services_Project.
4. THE Api_Project SHALL NOT reference GroundUp.Repositories, GroundUp.Data.Abstractions, GroundUp.Data.Postgres, or any provider-specific packages.
5. THE Api_Project SHALL NOT be configured as a web project (no Sdk="Microsoft.NET.Sdk.Web").

### Requirement 2: BaseController Constructor and Class Structure

**User Story:** As a framework developer, I want an abstract generic base controller that accepts a BaseService&lt;TDto&gt; via constructor injection, so that derived controllers inherit CRUD endpoints without boilerplate.

#### Acceptance Criteria

1. THE Api_Project SHALL contain an abstract BaseController&lt;TDto&gt; class in the GroundUp.Api.Controllers namespace where TDto : class.
2. THE BaseController SHALL inherit from ControllerBase.
3. THE BaseController SHALL be decorated with [ApiController] and [Route("api/[controller]")] attributes.
4. THE BaseController SHALL accept a BaseService&lt;TDto&gt; via constructor injection and expose it as a protected property named Service.
5. THE BaseController SHALL NOT be decorated with [Authorize] or any authorization attribute.
6. THE BaseController SHALL NOT use the sealed modifier because it is designed for inheritance by derived controllers.
7. THE BaseController SHALL use a file-scoped namespace.
8. THE BaseController SHALL have XML documentation comments on the class, constructor, and all public and protected members.

### Requirement 3: BaseController GET All Endpoint

**User Story:** As a framework developer, I want a GET endpoint that retrieves a paginated list of DTOs with pagination metadata in response headers, so that API consumers can list and page through resources.

#### Acceptance Criteria

1. THE BaseController SHALL define a public virtual async method decorated with [HttpGet] that accepts FilterParams bound from the query string and a CancellationToken and returns Task&lt;ActionResult&lt;OperationResult&lt;PaginatedData&lt;TDto&gt;&gt;&gt;&gt;.
2. WHEN the GET all endpoint is called, THE BaseController SHALL delegate to BaseService&lt;TDto&gt;.GetAllAsync with the FilterParams and CancellationToken.
3. WHEN the service returns a successful OperationResult containing PaginatedData, THE BaseController SHALL add the following response headers: X-Total-Count set to TotalRecords, X-Page-Number set to PageNumber, X-Page-Size set to PageSize, and X-Total-Pages set to TotalPages.
4. WHEN the service returns a successful OperationResult, THE BaseController SHALL return the OperationResult converted to the appropriate ActionResult using the OperationResult-to-ActionResult helper.
5. WHEN the service returns a failed OperationResult, THE BaseController SHALL return the OperationResult converted to the appropriate ActionResult using the OperationResult-to-ActionResult helper without adding pagination headers.

### Requirement 4: BaseController GET By Id Endpoint

**User Story:** As a framework developer, I want a GET endpoint that retrieves a single DTO by its unique identifier, so that API consumers can fetch individual resources.

#### Acceptance Criteria

1. THE BaseController SHALL define a public virtual async method decorated with [HttpGet("{id}")] that accepts a Guid id route parameter and a CancellationToken and returns Task&lt;ActionResult&lt;OperationResult&lt;TDto&gt;&gt;&gt;.
2. WHEN the GET by id endpoint is called, THE BaseController SHALL delegate to BaseService&lt;TDto&gt;.GetByIdAsync with the Guid and CancellationToken.
3. WHEN the service returns a successful OperationResult, THE BaseController SHALL return Ok with the OperationResult.
4. WHEN the service returns a failed OperationResult with StatusCode 404, THE BaseController SHALL return NotFound with the OperationResult.

### Requirement 5: BaseController POST Endpoint

**User Story:** As a framework developer, I want a POST endpoint that creates a new resource, so that API consumers can add new entities.

#### Acceptance Criteria

1. THE BaseController SHALL define a public virtual async method decorated with [HttpPost] that accepts a TDto from the request body and a CancellationToken and returns Task&lt;ActionResult&lt;OperationResult&lt;TDto&gt;&gt;&gt;.
2. WHEN the POST endpoint is called, THE BaseController SHALL delegate to BaseService&lt;TDto&gt;.AddAsync with the TDto and CancellationToken.
3. WHEN the service returns a successful OperationResult with StatusCode 201, THE BaseController SHALL return CreatedAtAction pointing to the GET by id endpoint with the created resource data.
4. WHEN the service returns a failed OperationResult with StatusCode 400, THE BaseController SHALL return BadRequest with the OperationResult.

### Requirement 6: BaseController PUT Endpoint

**User Story:** As a framework developer, I want a PUT endpoint that updates an existing resource by id, so that API consumers can modify entities.

#### Acceptance Criteria

1. THE BaseController SHALL define a public virtual async method decorated with [HttpPut("{id}")] that accepts a Guid id route parameter and a TDto from the request body and a CancellationToken and returns Task&lt;ActionResult&lt;OperationResult&lt;TDto&gt;&gt;&gt;.
2. WHEN the PUT endpoint is called, THE BaseController SHALL delegate to BaseService&lt;TDto&gt;.UpdateAsync with the Guid, TDto, and CancellationToken.
3. WHEN the service returns a successful OperationResult, THE BaseController SHALL return Ok with the OperationResult.
4. WHEN the service returns a failed OperationResult with StatusCode 404, THE BaseController SHALL return NotFound with the OperationResult.
5. WHEN the service returns a failed OperationResult with StatusCode 400, THE BaseController SHALL return BadRequest with the OperationResult.

### Requirement 7: BaseController DELETE Endpoint

**User Story:** As a framework developer, I want a DELETE endpoint that removes a resource by id, so that API consumers can delete entities.

#### Acceptance Criteria

1. THE BaseController SHALL define a public virtual async method decorated with [HttpDelete("{id}")] that accepts a Guid id route parameter and a CancellationToken and returns Task&lt;ActionResult&lt;OperationResult&gt;&gt;.
2. WHEN the DELETE endpoint is called, THE BaseController SHALL delegate to BaseService&lt;TDto&gt;.DeleteAsync with the Guid and CancellationToken.
3. WHEN the service returns a successful OperationResult, THE BaseController SHALL return Ok with the OperationResult.
4. WHEN the service returns a failed OperationResult with StatusCode 404, THE BaseController SHALL return NotFound with the OperationResult.

### Requirement 8: OperationResult to ActionResult Conversion

**User Story:** As a framework developer, I want a private helper method that maps OperationResult.StatusCode to the appropriate ActionResult, so that all endpoints use consistent HTTP response mapping.

#### Acceptance Criteria

1. THE BaseController SHALL contain a private helper method that accepts an OperationResult&lt;T&gt; and returns an ActionResult.
2. WHEN the OperationResult has StatusCode 200, THE helper SHALL return Ok with the OperationResult as the response body.
3. WHEN the OperationResult has StatusCode 201, THE helper SHALL return a 201 Created response with the OperationResult as the response body.
4. WHEN the OperationResult has StatusCode 400, THE helper SHALL return BadRequest with the OperationResult as the response body.
5. WHEN the OperationResult has StatusCode 401, THE helper SHALL return Unauthorized.
6. WHEN the OperationResult has StatusCode 403, THE helper SHALL return a 403 Forbidden response with the OperationResult as the response body.
7. WHEN the OperationResult has StatusCode 404, THE helper SHALL return NotFound with the OperationResult as the response body.
8. IF the OperationResult has a StatusCode not explicitly mapped (any other value), THEN THE helper SHALL return an ObjectResult with the StatusCode set to the OperationResult.StatusCode and the OperationResult as the response body.
9. THE BaseController SHALL also contain a private helper method overload that accepts a non-generic OperationResult and returns an ActionResult, following the same StatusCode-to-ActionResult mapping.

### Requirement 9: ExceptionHandlingMiddleware

**User Story:** As a framework developer, I want middleware that catches unhandled exceptions and returns structured JSON error responses with appropriate HTTP status codes, so that API consumers receive consistent error responses instead of raw exception details.

#### Acceptance Criteria

1. THE Api_Project SHALL contain a sealed ExceptionHandlingMiddleware class in the GroundUp.Api.Middleware namespace.
2. THE ExceptionHandlingMiddleware SHALL accept a RequestDelegate and an ILogger&lt;ExceptionHandlingMiddleware&gt; via constructor injection.
3. WHEN an unhandled NotFoundException is thrown, THE ExceptionHandlingMiddleware SHALL return HTTP 404 with a JSON response containing the exception message, the ErrorCode "NOT_FOUND", and the correlation ID from HttpContext.Items.
4. WHEN an unhandled GroundUpException is thrown, THE ExceptionHandlingMiddleware SHALL return HTTP 500 with a JSON response containing the exception message, the ErrorCode "INTERNAL_ERROR", and the correlation ID from HttpContext.Items.
5. WHEN an unhandled Exception (not a GroundUpException subclass) is thrown, THE ExceptionHandlingMiddleware SHALL return HTTP 500 with a JSON response containing the message "An unexpected error occurred", the ErrorCode "INTERNAL_ERROR", and the correlation ID from HttpContext.Items.
6. WHEN an unhandled Exception (not a GroundUpException subclass) is thrown, THE ExceptionHandlingMiddleware SHALL NOT include the raw exception message or stack trace in the response body to prevent information leakage.
7. THE ExceptionHandlingMiddleware SHALL log the full exception details (message, stack trace) using the injected ILogger before returning the error response.
8. THE ExceptionHandlingMiddleware SHALL set the response Content-Type to "application/json".
9. THE ExceptionHandlingMiddleware SHALL use typed exception checks (is NotFoundException, is GroundUpException) and NOT string matching on exception messages.
10. THE ExceptionHandlingMiddleware SHALL use a file-scoped namespace.
11. THE ExceptionHandlingMiddleware SHALL have XML documentation comments on the class and the InvokeAsync method.

### Requirement 10: CorrelationIdMiddleware

**User Story:** As a framework developer, I want middleware that reads or generates a correlation ID per request and makes it available throughout the request pipeline, so that all log entries and error responses can be traced back to a specific request.

#### Acceptance Criteria

1. THE Api_Project SHALL contain a sealed CorrelationIdMiddleware class in the GroundUp.Api.Middleware namespace.
2. THE CorrelationIdMiddleware SHALL accept a RequestDelegate via constructor injection.
3. WHEN an incoming request contains an "X-Correlation-Id" header, THE CorrelationIdMiddleware SHALL use the value from that header as the correlation ID for the request.
4. WHEN an incoming request does NOT contain an "X-Correlation-Id" header, THE CorrelationIdMiddleware SHALL generate a new GUID as the correlation ID.
5. THE CorrelationIdMiddleware SHALL store the correlation ID in HttpContext.Items with the key "CorrelationId" so that other middleware and services can access it.
6. THE CorrelationIdMiddleware SHALL add the correlation ID to the response headers as "X-Correlation-Id" before the response is sent.
7. THE CorrelationIdMiddleware SHALL use a file-scoped namespace.
8. THE CorrelationIdMiddleware SHALL have XML documentation comments on the class and the InvokeAsync method.
9. THE CorrelationIdMiddleware SHALL define the header name "X-Correlation-Id" as a public constant to avoid magic strings.

### Requirement 11: AddGroundUpApi DI Extension Method

**User Story:** As a framework developer, I want an AddGroundUpApi extension method on IServiceCollection that registers API-layer services, so that consuming applications can wire up the API layer with a single method call.

#### Acceptance Criteria

1. THE Api_Project SHALL contain a static ApiServiceCollectionExtensions class in the GroundUp.Api namespace.
2. THE ApiServiceCollectionExtensions SHALL define a public static AddGroundUpApi extension method on IServiceCollection.
3. THE AddGroundUpApi method SHALL return the IServiceCollection for method chaining.
4. THE ApiServiceCollectionExtensions SHALL use a file-scoped namespace.
5. THE ApiServiceCollectionExtensions SHALL have XML documentation comments on the class and the method.
6. THE ApiServiceCollectionExtensions class SHALL use the static modifier.

### Requirement 12: UseGroundUpMiddleware Extension Method

**User Story:** As a framework developer, I want a UseGroundUpMiddleware extension method on IApplicationBuilder that registers the middleware pipeline in the correct order, so that consuming applications can wire up middleware with a single method call.

#### Acceptance Criteria

1. THE Api_Project SHALL contain a static GroundUpApplicationBuilderExtensions class in the GroundUp.Api namespace.
2. THE GroundUpApplicationBuilderExtensions SHALL define a public static UseGroundUpMiddleware extension method on IApplicationBuilder.
3. WHEN UseGroundUpMiddleware is called, THE method SHALL register CorrelationIdMiddleware first and ExceptionHandlingMiddleware second, so that the correlation ID is available when exceptions are caught.
4. THE UseGroundUpMiddleware method SHALL return the IApplicationBuilder for method chaining.
5. THE GroundUpApplicationBuilderExtensions SHALL use a file-scoped namespace.
6. THE GroundUpApplicationBuilderExtensions SHALL have XML documentation comments on the class and the method.

### Requirement 13: Pagination Response Headers

**User Story:** As a framework developer, I want pagination metadata included in response headers on list endpoints, so that API consumers can implement pagination without parsing the response body.

#### Acceptance Criteria

1. WHEN the GET all endpoint returns a successful OperationResult with PaginatedData, THE BaseController SHALL add an "X-Total-Count" response header set to the TotalRecords value from PaginatedData.
2. WHEN the GET all endpoint returns a successful OperationResult with PaginatedData, THE BaseController SHALL add an "X-Page-Number" response header set to the PageNumber value from PaginatedData.
3. WHEN the GET all endpoint returns a successful OperationResult with PaginatedData, THE BaseController SHALL add an "X-Page-Size" response header set to the PageSize value from PaginatedData.
4. WHEN the GET all endpoint returns a successful OperationResult with PaginatedData, THE BaseController SHALL add an "X-Total-Pages" response header set to the TotalPages value from PaginatedData.
5. WHEN the GET all endpoint returns a failed OperationResult, THE BaseController SHALL NOT add pagination response headers.

### Requirement 14: ExceptionHandlingMiddleware Error Response Structure

**User Story:** As a framework developer, I want exception error responses to follow a consistent JSON structure with message, error code, and correlation ID, so that API consumers can programmatically handle errors.

#### Acceptance Criteria

1. THE ExceptionHandlingMiddleware error response JSON SHALL contain a "message" field with a human-readable error description.
2. THE ExceptionHandlingMiddleware error response JSON SHALL contain an "errorCode" field with a machine-readable error code from ErrorCodes (e.g., "NOT_FOUND", "INTERNAL_ERROR").
3. THE ExceptionHandlingMiddleware error response JSON SHALL contain a "correlationId" field with the correlation ID from HttpContext.Items, or null if no correlation ID is available.
4. THE ExceptionHandlingMiddleware error response JSON SHALL use camelCase property naming consistent with ASP.NET Core JSON serialization defaults.

### Requirement 15: Unit Tests for BaseController OperationResult-to-ActionResult Mapping

**User Story:** As a framework developer, I want unit tests verifying that the OperationResult-to-ActionResult helper correctly maps each StatusCode to the appropriate ActionResult type, so that I have confidence in the HTTP response mapping.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 200 produces an OkObjectResult.
2. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 201 produces a 201 status code result.
3. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 400 produces a BadRequestObjectResult.
4. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 401 produces an UnauthorizedResult.
5. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 403 produces a 403 status code result.
6. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with StatusCode 404 produces a NotFoundObjectResult.
7. THE Unit_Test_Project SHALL contain a test verifying that an OperationResult with an unmapped StatusCode (e.g., 409) produces an ObjectResult with the correct StatusCode.
8. THE Unit_Test_Project SHALL use xUnit and NSubstitute, consistent with existing test conventions.

### Requirement 16: Unit Tests for BaseController CRUD Endpoints

**User Story:** As a framework developer, I want unit tests verifying that each BaseController endpoint delegates to the correct service method and returns the correct ActionResult, so that I have confidence the controller is a pure pass-through.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that the GET all endpoint delegates to BaseService.GetAllAsync and returns Ok with pagination headers on success.
2. THE Unit_Test_Project SHALL contain a test verifying that the GET all endpoint does NOT add pagination headers when the service returns a failed OperationResult.
3. THE Unit_Test_Project SHALL contain a test verifying that the GET by id endpoint delegates to BaseService.GetByIdAsync and returns Ok on success.
4. THE Unit_Test_Project SHALL contain a test verifying that the GET by id endpoint returns NotFound when the service returns a 404 OperationResult.
5. THE Unit_Test_Project SHALL contain a test verifying that the POST endpoint delegates to BaseService.AddAsync and returns Created on success.
6. THE Unit_Test_Project SHALL contain a test verifying that the POST endpoint returns BadRequest when the service returns a 400 OperationResult.
7. THE Unit_Test_Project SHALL contain a test verifying that the PUT endpoint delegates to BaseService.UpdateAsync and returns Ok on success.
8. THE Unit_Test_Project SHALL contain a test verifying that the PUT endpoint returns NotFound when the service returns a 404 OperationResult.
9. THE Unit_Test_Project SHALL contain a test verifying that the DELETE endpoint delegates to BaseService.DeleteAsync and returns Ok on success.
10. THE Unit_Test_Project SHALL contain a test verifying that the DELETE endpoint returns NotFound when the service returns a 404 OperationResult.

### Requirement 17: Unit Tests for ExceptionHandlingMiddleware

**User Story:** As a framework developer, I want unit tests verifying that ExceptionHandlingMiddleware correctly maps each typed exception to the appropriate HTTP status code and error response structure, so that I have confidence in the error handling pipeline.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that NotFoundException produces HTTP 404 with ErrorCode "NOT_FOUND".
2. THE Unit_Test_Project SHALL contain a test verifying that GroundUpException produces HTTP 500 with ErrorCode "INTERNAL_ERROR".
3. THE Unit_Test_Project SHALL contain a test verifying that a generic Exception produces HTTP 500 with the message "An unexpected error occurred" and ErrorCode "INTERNAL_ERROR".
4. THE Unit_Test_Project SHALL contain a test verifying that a generic Exception does NOT include the raw exception message in the response body.
5. THE Unit_Test_Project SHALL contain a test verifying that the error response JSON contains the correlationId from HttpContext.Items.
6. THE Unit_Test_Project SHALL contain a test verifying that the response Content-Type is "application/json".
7. THE Unit_Test_Project SHALL contain a test verifying that the middleware calls the next delegate when no exception is thrown.

### Requirement 18: Unit Tests for CorrelationIdMiddleware

**User Story:** As a framework developer, I want unit tests verifying that CorrelationIdMiddleware correctly reads, generates, stores, and propagates correlation IDs, so that I have confidence in the request tracing pipeline.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that when an incoming request contains an "X-Correlation-Id" header, the middleware uses that value as the correlation ID.
2. THE Unit_Test_Project SHALL contain a test verifying that when an incoming request does NOT contain an "X-Correlation-Id" header, the middleware generates a new GUID as the correlation ID.
3. THE Unit_Test_Project SHALL contain a test verifying that the correlation ID is stored in HttpContext.Items with the key "CorrelationId".
4. THE Unit_Test_Project SHALL contain a test verifying that the correlation ID is added to the response headers as "X-Correlation-Id".
5. THE Unit_Test_Project SHALL contain a test verifying that the middleware calls the next delegate.

### Requirement 19: Property-Based Test for OperationResult-to-ActionResult Mapping

**User Story:** As a framework developer, I want a property-based test verifying that for all valid OperationResult StatusCode values, the helper method always produces an ActionResult with a matching HTTP status code, so that no status code mapping is lost or incorrect.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL OperationResult&lt;T&gt; instances with StatusCode values in the range 200-599, the OperationResult-to-ActionResult helper produces an ActionResult whose HTTP status code matches the OperationResult.StatusCode.
2. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL non-generic OperationResult instances with StatusCode values in the range 200-599, the OperationResult-to-ActionResult helper produces an ActionResult whose HTTP status code matches the OperationResult.StatusCode.
3. THE property-based tests SHALL use xUnit and FsCheck, consistent with existing test conventions.

### Requirement 20: Property-Based Test for CorrelationIdMiddleware

**User Story:** As a framework developer, I want a property-based test verifying that for all possible correlation ID header values, the middleware correctly propagates the value to HttpContext.Items and response headers, so that correlation ID handling is robust.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL non-empty string values provided as the "X-Correlation-Id" request header, the CorrelationIdMiddleware stores the same value in HttpContext.Items["CorrelationId"] and adds the same value to the response "X-Correlation-Id" header.
2. THE property-based test SHALL use xUnit and FsCheck, consistent with existing test conventions.

### Requirement 21: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 3E changes, so that I know the new API layer integrates correctly with the existing codebase.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 3E changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 3E changes, THE Unit_Test_Project SHALL pass all tests including the new BaseController, ExceptionHandlingMiddleware, and CorrelationIdMiddleware tests.

### Requirement 22: Enforce Coding Conventions

**User Story:** As a framework developer, I want all Phase 3E types to follow established coding conventions, so that the API layer code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE Api_Project SHALL use file-scoped namespaces in all source files.
2. THE Api_Project SHALL enable nullable reference types.
3. THE Api_Project SHALL place each class in its own separate file.
4. THE BaseController SHALL NOT use the sealed modifier because it is designed for inheritance by derived controllers.
5. THE ExceptionHandlingMiddleware SHALL use the sealed modifier because it is not designed for inheritance.
6. THE CorrelationIdMiddleware SHALL use the sealed modifier because it is not designed for inheritance.
7. THE ApiServiceCollectionExtensions SHALL use the static modifier.
8. THE GroundUpApplicationBuilderExtensions SHALL use the static modifier.
9. THE Api_Project SHALL have XML documentation comments on all public types and members.
