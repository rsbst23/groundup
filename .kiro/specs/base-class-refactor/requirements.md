# Requirements Document

## Introduction

This document specifies the requirements for refactoring the GroundUp framework's base classes (BaseController, BaseService, BaseRepository) to eliminate the single-DTO generic constraint that forces all CRUD operations to use the same DTO type. The current design breaks down for real-world entities that need different DTOs per operation (CreateDto, UpdateDto, ListDto, DetailDto), leading to workarounds like NonAction hacks, ambiguous method names, and incorrect return types.

The refactor preserves BaseRepository<TEntity> with its generic CRUD methods (entities are uniform), while transforming BaseService and BaseController into infrastructure-only base classes that provide shared helpers (validation pipeline, event publishing, ToActionResult, pagination headers) without imposing DTO type constraints on CRUD method signatures. Derived services and controllers define their own methods with the correct DTOs per operation.

## Glossary

- **BaseController**: The abstract base controller class in GroundUp.Api that derived controllers extend. Currently `BaseController<TDto>` with generic CRUD endpoints. After refactor, provides infrastructure helpers (ToActionResult, pagination headers) without generic CRUD methods.
- **BaseService**: The abstract base service class in GroundUp.Services that derived services extend. Currently `BaseService<TDto>` with generic CRUD methods. After refactor, provides infrastructure helpers (validation pipeline, event publishing, error handling) without generic CRUD methods.
- **BaseRepository**: The abstract base repository class in GroundUp.Repositories. `BaseRepository<TEntity, TDto>` with generic CRUD methods. Unchanged by this refactor — entities are uniform and the generic CRUD pattern works correctly at this layer.
- **BaseTenantRepository**: Extends BaseRepository with automatic tenant isolation. `BaseTenantRepository<TEntity, TDto>`. Unchanged by this refactor.
- **OperationResult**: The standardized result wrapper (`OperationResult<T>` / `OperationResult`) used across all layers. Returns success/failure with data, status codes, and error details.
- **FilterParams**: Query parameters object carrying filtering, sorting, and pagination criteria.
- **PaginatedData**: Wrapper for paginated query results containing items, page number, page size, and total records.
- **QueryShaper**: A `Func<IQueryable<T>, IQueryable<T>>` hook that derived repositories use to customize queries (e.g., Include navigation properties).
- **Mapperly**: Source-generated mapper library used for entity ↔ DTO conversion. NOT AutoMapper.
- **IEventBus**: Abstraction for publishing domain events. Used by services for entity lifecycle events.
- **FluentValidation**: Validation library used in the service layer to validate DTOs before persistence.
- **Simple_Entity**: An entity where a single DTO suffices for all operations (e.g., TodoItem, Customer).
- **Complex_Entity**: An entity requiring different DTOs per operation — CreateDto, UpdateDto, ListDto, DetailDto (e.g., Order).
- **Tenant_Scoped_Entity**: An entity implementing ITenantEntity, automatically filtered by tenant in BaseTenantRepository.
- **NonAction_Hack**: The current workaround where base controller methods without HTTP attributes are technically still visible to ASP.NET routing, causing Swagger ambiguity errors.
- **ToActionResult**: Protected helper method on BaseController that maps OperationResult status codes to appropriate HTTP ActionResult responses.
- **Pagination_Headers**: HTTP response headers (X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages) added by controllers for paginated responses.

## Requirements

### Requirement 1: BaseController Becomes Infrastructure-Only

**User Story:** As a framework consumer, I want BaseController to provide only shared HTTP infrastructure (ToActionResult, pagination headers, route conventions) without imposing generic CRUD methods, so that my derived controllers can define endpoints with the correct DTO types per operation.

#### Acceptance Criteria

1. THE BaseController SHALL provide a protected `ToActionResult<T>(OperationResult<T>)` method that maps OperationResult status codes to ActionResult responses.
2. THE BaseController SHALL provide a protected `ToActionResult(OperationResult)` method that maps non-generic OperationResult status codes to ActionResult responses.
3. THE BaseController SHALL provide a protected method for adding Pagination_Headers (X-Total-Count, X-Page-Number, X-Page-Size, X-Total-Pages) to the HTTP response.
4. THE BaseController SHALL retain the `[ApiController]` and `[Route("api/[controller]")]` attributes.
5. THE BaseController SHALL NOT define any virtual CRUD methods (GetAll, GetById, Create, Update, Delete).
6. THE BaseController SHALL NOT have a generic type parameter for a DTO type.
7. WHEN a derived controller extends BaseController, THE derived controller SHALL define its own endpoint methods with explicit HTTP attributes and operation-specific DTO types.

### Requirement 2: BaseService Becomes Infrastructure-Only

**User Story:** As a framework consumer, I want BaseService to provide shared service infrastructure (validation pipeline, event publishing, error handling) without imposing generic CRUD methods, so that my derived services can define methods with the correct input and output DTO types per operation.

#### Acceptance Criteria

1. THE BaseService SHALL provide a protected `ValidateAsync<TDto>(TDto, CancellationToken)` method that runs FluentValidation and returns `OperationResult<TDto>?` (null on success, BadRequest on failure).
2. THE BaseService SHALL provide a protected `PublishEventSafelyAsync<TEvent>(TEvent, CancellationToken)` method that publishes events via IEventBus with fire-and-forget error handling.
3. THE BaseService SHALL accept an IEventBus via its constructor and expose it as a protected property.
4. THE BaseService SHALL NOT define any virtual CRUD methods (GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync).
5. THE BaseService SHALL NOT have a generic type parameter for a DTO type.
6. WHEN a derived service extends BaseService, THE derived service SHALL define its own methods with explicit input/output DTO types.
7. THE BaseService SHALL support resolving FluentValidation validators from the DI container so that derived services can validate any DTO type, not just a single pre-registered validator.

### Requirement 3: BaseRepository Remains Unchanged

**User Story:** As a framework consumer, I want BaseRepository<TEntity, TDto> to continue providing generic CRUD operations with filtering, sorting, paging, and queryShaper hooks, because entities are uniform at the data layer and the current pattern works correctly.

#### Acceptance Criteria

1. THE BaseRepository SHALL retain its `BaseRepository<TEntity, TDto>` generic signature with CRUD methods (GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync).
2. THE BaseRepository SHALL retain the queryShaper hook pattern (`Func<IQueryable<TEntity>, IQueryable<TEntity>>?`) for derived repositories to customize queries.
3. THE BaseRepository SHALL retain FilterParams-based filtering, sorting, and paging via ExpressionHelper.
4. THE BaseRepository SHALL retain Mapperly-based entity ↔ DTO mapping via constructor-injected delegates.
5. THE BaseRepository SHALL retain soft delete support for entities implementing ISoftDeletable.
6. THE IBaseRepository<TDto> interface SHALL remain unchanged.
7. THE BaseTenantRepository SHALL retain its `BaseTenantRepository<TEntity, TDto>` generic signature with automatic tenant isolation.

### Requirement 4: Simple Entity Pattern — Minimal Boilerplate

**User Story:** As a framework consumer building a simple entity (e.g., TodoItem, Customer) where one DTO works for all operations, I want to write minimal code — a few one-liner methods in my controller and service — without losing the benefits of the base class infrastructure.

#### Acceptance Criteria

1. WHEN a Simple_Entity uses one DTO for all operations, THE derived service SHALL define CRUD methods that delegate to the repository with no more than one line of logic per method (plus validation/event calls via base helpers).
2. WHEN a Simple_Entity uses one DTO for all operations, THE derived controller SHALL define endpoint methods that delegate to the service with no more than one line of logic per method (plus ToActionResult conversion).
3. THE Simple_Entity pattern SHALL NOT require the developer to write custom mapping logic, custom validation orchestration, or custom event publishing code beyond calling the base class helpers.
4. THE Simple_Entity controller SHALL produce correct Swagger documentation with unambiguous HTTP methods and correct DTO types.
5. THE Simple_Entity pattern SHALL require no more boilerplate than the current pattern where derived controllers override base methods and add HTTP attributes.

### Requirement 5: Complex Entity Pattern — Correct DTOs Per Operation

**User Story:** As a framework consumer building a complex entity (e.g., Order) with different DTOs per operation, I want each endpoint to use the correct DTO type (CreateOrderDto for POST, UpdateOrderDto for PUT, OrderListDto for GET list, OrderDetailDto for GET by ID) without any workarounds.

#### Acceptance Criteria

1. WHEN a Complex_Entity has different DTOs per operation, THE derived controller SHALL define each endpoint with the correct input and output DTO types.
2. WHEN a Complex_Entity has different DTOs per operation, THE derived service SHALL define each method with the correct input and output DTO types.
3. THE Complex_Entity controller SHALL NOT require NonAction_Hack attributes to hide unwanted base methods.
4. THE Complex_Entity controller SHALL NOT require disambiguated method names (e.g., CreateOrder instead of Create) to avoid routing conflicts.
5. THE Complex_Entity controller SHALL use standard method names (Create, Update, GetById, GetAll, Delete) with standard HTTP attributes.
6. THE Complex_Entity controller SHALL produce correct Swagger documentation with each endpoint showing its specific DTO types.
7. WHEN GetById is called on a Complex_Entity, THE controller SHALL return the detail DTO type (e.g., OrderDetailDto), not the list DTO type.

### Requirement 6: Tenant-Scoped Entity Support

**User Story:** As a framework consumer building a tenant-scoped entity, I want the refactored base classes to work seamlessly with BaseTenantRepository so that tenant isolation remains automatic and transparent.

#### Acceptance Criteria

1. WHEN a Tenant_Scoped_Entity service extends BaseService, THE service SHALL be able to use BaseTenantRepository for automatic tenant filtering without any changes to the repository layer.
2. THE refactored BaseService SHALL NOT interfere with the BaseTenantRepository tenant isolation mechanism.
3. WHEN a Tenant_Scoped_Entity controller extends BaseController, THE controller SHALL define its own endpoints that delegate to the tenant-scoped service.
4. THE Tenant_Scoped_Entity pattern SHALL work identically for both Simple_Entity and Complex_Entity scenarios.

### Requirement 7: DI Registration Pattern

**User Story:** As a framework consumer, I want a clear and consistent DI registration pattern for the refactored services and controllers, so that wiring up simple and complex entities is straightforward.

#### Acceptance Criteria

1. WHEN registering a Simple_Entity, THE consuming application SHALL register the repository as `IBaseRepository<TDto>` and the service by its concrete type.
2. WHEN registering a Complex_Entity, THE consuming application SHALL register the repository by its concrete type (for custom methods) and the service by its concrete type.
3. THE DI registration pattern SHALL NOT require registering services as `BaseService<TDto>` since BaseService no longer has a DTO type parameter.
4. THE DI registration pattern SHALL support FluentValidation validator auto-discovery for any DTO type used by a service.
5. IF a service needs to validate multiple DTO types, THEN THE service SHALL resolve validators from the DI container at validation time rather than accepting a single validator in the constructor.

### Requirement 8: Swagger/OpenAPI Compatibility

**User Story:** As a framework consumer, I want all controller endpoints to produce correct Swagger documentation without ambiguous HTTP method errors, so that API consumers can discover and test endpoints reliably.

#### Acceptance Criteria

1. THE refactored controllers SHALL NOT produce "Ambiguous HTTP method" errors in Swagger.
2. WHEN Swagger generates documentation for a controller, each endpoint SHALL show the correct request and response DTO types.
3. THE BaseController SHALL NOT expose any methods that ASP.NET routing could interpret as endpoints (no virtual methods without HTTP attributes).
4. WHEN a Complex_Entity controller defines Create with CreateOrderDto and GetById returning OrderDetailDto, THE Swagger documentation SHALL reflect those specific types.

### Requirement 9: Backward Compatibility and Migration

**User Story:** As a framework consumer with existing code, I want a clear migration path from the current base class pattern to the refactored pattern, so that I can update my code without breaking existing functionality.

#### Acceptance Criteria

1. THE refactored BaseRepository and BaseTenantRepository SHALL maintain their existing public API — no breaking changes at the repository layer.
2. WHEN migrating a Simple_Entity from the current pattern, THE developer SHALL move CRUD method definitions from base class overrides to explicit method definitions in the derived service and controller.
3. WHEN migrating a Complex_Entity from the current pattern, THE developer SHALL remove NonAction_Hack workarounds and disambiguated method names, replacing them with standard method names and correct DTO types.
4. THE refactored framework SHALL compile and pass all existing tests after migration (tests may need updates to match new method signatures).
5. IF existing integration tests call endpoints by HTTP method and route, THEN those tests SHALL continue to work without route changes.

### Requirement 10: Event Publishing in Derived Services

**User Story:** As a framework consumer, I want to publish entity lifecycle events (Created, Updated, Deleted) from my derived services using the base class helper, so that event-driven patterns continue to work after the refactor.

#### Acceptance Criteria

1. THE BaseService SHALL provide the `PublishEventSafelyAsync` method as a protected helper that derived services call explicitly after successful operations.
2. WHEN a derived service successfully creates an entity, THE service SHALL be able to publish an `EntityCreatedEvent<TDto>` using the base helper, where TDto is the operation-specific output DTO type.
3. WHEN a derived service successfully updates an entity, THE service SHALL be able to publish an `EntityUpdatedEvent<TDto>` using the base helper.
4. WHEN a derived service successfully deletes an entity, THE service SHALL be able to publish an `EntityDeletedEvent<TDto>` using the base helper.
5. IF event publishing fails, THEN THE BaseService PublishEventSafelyAsync method SHALL catch the exception and continue without affecting the operation result.

### Requirement 11: Validation Pipeline in Derived Services

**User Story:** As a framework consumer, I want to validate any DTO type in my service methods using FluentValidation, not just a single DTO type bound to the base class generic parameter.

#### Acceptance Criteria

1. THE BaseService SHALL provide a protected validation method that accepts any DTO type and resolves the appropriate FluentValidation validator from the DI container.
2. WHEN a derived service validates a CreateOrderDto, THE validation method SHALL resolve `IValidator<CreateOrderDto>` from the DI container and run validation.
3. WHEN a derived service validates an UpdateOrderDto in the same service, THE validation method SHALL resolve `IValidator<UpdateOrderDto>` from the DI container and run validation.
4. IF no validator is registered for a DTO type, THEN THE validation method SHALL skip validation and return success (null).
5. IF validation fails, THEN THE validation method SHALL return an `OperationResult.BadRequest` with the list of validation error messages.

### Requirement 12: Sample Application Demonstrates Both Patterns

**User Story:** As a framework consumer learning the patterns, I want the sample application to demonstrate both the simple entity pattern and the complex entity pattern clearly, so that I can use them as reference implementations.

#### Acceptance Criteria

1. THE sample application SHALL include TodoItem as a Simple_Entity example with a single DTO (TodoItemDto) used for all operations.
2. THE sample application SHALL include Customer as a Simple_Entity example with a single DTO (CustomerDto) used for all operations.
3. THE sample application SHALL include Order as a Complex_Entity example with CreateOrderDto, UpdateOrderDto, OrderListDto, and OrderDetailDto.
4. THE sample application SHALL include Project as a Tenant_Scoped_Entity example demonstrating tenant isolation with the refactored base classes.
5. WHEN the sample application is run, all Swagger endpoints SHALL display correctly with the right DTO types and no ambiguity errors.
6. THE sample application DI registration in Program.cs SHALL demonstrate the registration pattern for both Simple_Entity and Complex_Entity scenarios.
