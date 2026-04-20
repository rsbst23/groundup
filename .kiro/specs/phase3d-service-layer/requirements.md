# Requirements Document

## Introduction

Phase 3D of the GroundUp framework builds the base service layer in GroundUp.Services — the business logic and security boundary of the framework. This phase delivers `BaseService<TDto>`, an abstract generic service that wraps `IBaseRepository<TDto>` with pass-through CRUD, a FluentValidation pipeline that runs before mutating repository calls, and entity lifecycle event publishing via `IEventBus` after successful operations. It also delivers the `AddGroundUpServices()` DI extension method for registering FluentValidation validators.

BaseService is the central orchestration point between the API layer above and the repository layer below. All public methods return `OperationResult<T>` or `OperationResult` (non-generic). Validation runs BEFORE repository calls (fail fast), and events publish AFTER successful repository operations (fire-and-forget side effects). BaseService does NOT access HttpContext — it uses `ICurrentUser` and `ITenantContext` from GroundUp.Core for identity and tenant context, ensuring it works identically whether called from a controller (API) or directly (SDK).

FluentValidation validators are optional — if no `IValidator<TDto>` is registered in DI for a given TDto, validation is skipped. The FluentValidation NuGet package is already referenced in GroundUp.Services.csproj from Phase 1.

## Glossary

- **Services_Project**: The GroundUp.Services class library project containing the base service layer. Depends on Core_Project, Data_Abstractions, and Events_Project. References the FluentValidation NuGet package.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types (OperationResult, FilterParams, PaginatedData, ICurrentUser, ITenantContext, ErrorCodes).
- **Data_Abstractions**: The GroundUp.Data.Abstractions class library project containing repository interfaces (IBaseRepository&lt;TDto&gt;).
- **Events_Project**: The GroundUp.Events class library project containing IEventBus, BaseEvent, and entity lifecycle event records (EntityCreatedEvent&lt;T&gt;, EntityUpdatedEvent&lt;T&gt;, EntityDeletedEvent&lt;T&gt;).
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project for unit tests.
- **BaseService**: An abstract generic class (BaseService&lt;TDto&gt;) in Services_Project that wraps IBaseRepository&lt;TDto&gt; with validation, event publishing, and pass-through CRUD.
- **IBaseRepository**: The generic repository interface (IBaseRepository&lt;TDto&gt;) in Data_Abstractions defining the standard CRUD contract.
- **IEventBus**: The event publishing interface in Events_Project used by BaseService to publish entity lifecycle events after successful operations.
- **IValidator**: The FluentValidation interface (IValidator&lt;T&gt;) used to validate DTOs before mutating operations. Resolved from DI; optional per TDto.
- **OperationResult**: The generic (OperationResult&lt;T&gt;) and non-generic (OperationResult) result types in Core_Project used as the single standardized return type for all service methods.
- **FilterParams**: The parameter class in Core_Project carrying filtering, sorting, and pagination criteria.
- **PaginatedData**: The generic wrapper (PaginatedData&lt;T&gt;) in Core_Project that holds a page of results with pagination metadata.
- **EntityCreatedEvent**: A generic event record (EntityCreatedEvent&lt;T&gt;) in Events_Project published after a successful AddAsync operation. Carries the created entity data.
- **EntityUpdatedEvent**: A generic event record (EntityUpdatedEvent&lt;T&gt;) in Events_Project published after a successful UpdateAsync operation. Carries the updated entity data.
- **EntityDeletedEvent**: A generic event record (EntityDeletedEvent&lt;T&gt;) in Events_Project published after a successful DeleteAsync operation. Carries the deleted entity's Guid identifier.
- **AddGroundUpServices**: A static extension method on IServiceCollection in Services_Project that registers FluentValidation validators from the calling assembly.
- **ICurrentUser**: An abstraction in Core_Project providing the authenticated user's identity (UserId, Email, DisplayName) without depending on the authentication module.
- **ITenantContext**: An abstraction in Core_Project providing the current tenant's identifier for multi-tenant operations.

## Requirements

### Requirement 1: BaseService Constructor and Dependencies

**User Story:** As a framework developer, I want an abstract generic base service class that accepts a repository, event bus, and optional validator via constructor injection, so that derived services inherit CRUD orchestration, validation, and event publishing without boilerplate.

#### Acceptance Criteria

1. THE Services_Project SHALL contain an abstract BaseService&lt;TDto&gt; class in the GroundUp.Services namespace where TDto : class.
2. THE BaseService SHALL accept an IBaseRepository&lt;TDto&gt; via constructor injection.
3. THE BaseService SHALL accept an IEventBus via constructor injection.
4. THE BaseService SHALL accept an IValidator&lt;TDto&gt;? (nullable) via constructor injection, allowing validation to be optional when no validator is registered for the TDto type.
5. THE BaseService SHALL expose the IBaseRepository&lt;TDto&gt; as a protected property for derived service access.
6. THE BaseService SHALL use a file-scoped namespace.
7. THE BaseService SHALL have XML documentation comments on the class, constructor, and all public and protected members.
8. THE BaseService SHALL NOT use the sealed modifier because it is designed for inheritance by derived services.

### Requirement 2: BaseService GetAllAsync — Pass-Through Read

**User Story:** As a framework developer, I want GetAllAsync to delegate directly to the repository without validation or events, so that read operations have minimal overhead.

#### Acceptance Criteria

1. THE BaseService SHALL define a public virtual method GetAllAsync that accepts a FilterParams parameter and a CancellationToken and returns Task&lt;OperationResult&lt;PaginatedData&lt;TDto&gt;&gt;&gt;.
2. WHEN GetAllAsync is called, THE BaseService SHALL delegate directly to IBaseRepository&lt;TDto&gt;.GetAllAsync with the same FilterParams and CancellationToken.
3. WHEN GetAllAsync is called, THE BaseService SHALL NOT run validation.
4. WHEN GetAllAsync is called, THE BaseService SHALL NOT publish any events.
5. WHEN GetAllAsync completes, THE BaseService SHALL return the OperationResult from the repository unchanged.

### Requirement 3: BaseService GetByIdAsync — Pass-Through Read

**User Story:** As a framework developer, I want GetByIdAsync to delegate directly to the repository without validation or events, so that single-entity reads have minimal overhead.

#### Acceptance Criteria

1. THE BaseService SHALL define a public virtual method GetByIdAsync that accepts a Guid parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
2. WHEN GetByIdAsync is called, THE BaseService SHALL delegate directly to IBaseRepository&lt;TDto&gt;.GetByIdAsync with the same Guid and CancellationToken.
3. WHEN GetByIdAsync is called, THE BaseService SHALL NOT run validation.
4. WHEN GetByIdAsync is called, THE BaseService SHALL NOT publish any events.
5. WHEN GetByIdAsync completes, THE BaseService SHALL return the OperationResult from the repository unchanged.

### Requirement 4: BaseService AddAsync — Validate, Persist, Publish

**User Story:** As a framework developer, I want AddAsync to validate the DTO before persisting and publish an EntityCreatedEvent after success, so that all create operations enforce validation and enable event-driven side effects.

#### Acceptance Criteria

1. THE BaseService SHALL define a public virtual method AddAsync that accepts a TDto parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
2. WHEN AddAsync is called and an IValidator&lt;TDto&gt; is available, THE BaseService SHALL validate the TDto using the validator before calling the repository.
3. WHEN validation fails, THE BaseService SHALL return OperationResult&lt;TDto&gt;.BadRequest with the message "Validation failed" and a list of validation error messages extracted from the FluentValidation ValidationResult.Errors collection.
4. WHEN validation fails, THE BaseService SHALL NOT call the repository AddAsync method.
5. WHEN AddAsync is called and no IValidator&lt;TDto&gt; is available (null), THE BaseService SHALL skip validation and proceed to the repository call.
6. WHEN the repository AddAsync call succeeds, THE BaseService SHALL publish an EntityCreatedEvent&lt;TDto&gt; via IEventBus with the Entity property set to the created TDto from the repository result.
7. WHEN the repository AddAsync call fails (Success is false), THE BaseService SHALL NOT publish any event.
8. WHEN the repository AddAsync call fails, THE BaseService SHALL return the failed OperationResult from the repository unchanged.
9. WHEN AddAsync completes successfully, THE BaseService SHALL return the successful OperationResult from the repository.

### Requirement 5: BaseService UpdateAsync — Validate, Persist, Publish

**User Story:** As a framework developer, I want UpdateAsync to validate the DTO before persisting and publish an EntityUpdatedEvent after success, so that all update operations enforce validation and enable event-driven side effects.

#### Acceptance Criteria

1. THE BaseService SHALL define a public virtual method UpdateAsync that accepts a Guid parameter and a TDto parameter and a CancellationToken and returns Task&lt;OperationResult&lt;TDto&gt;&gt;.
2. WHEN UpdateAsync is called and an IValidator&lt;TDto&gt; is available, THE BaseService SHALL validate the TDto using the validator before calling the repository.
3. WHEN validation fails, THE BaseService SHALL return OperationResult&lt;TDto&gt;.BadRequest with the message "Validation failed" and a list of validation error messages extracted from the FluentValidation ValidationResult.Errors collection.
4. WHEN validation fails, THE BaseService SHALL NOT call the repository UpdateAsync method.
5. WHEN UpdateAsync is called and no IValidator&lt;TDto&gt; is available (null), THE BaseService SHALL skip validation and proceed to the repository call.
6. WHEN the repository UpdateAsync call succeeds, THE BaseService SHALL publish an EntityUpdatedEvent&lt;TDto&gt; via IEventBus with the Entity property set to the updated TDto from the repository result.
7. WHEN the repository UpdateAsync call fails (Success is false), THE BaseService SHALL NOT publish any event.
8. WHEN the repository UpdateAsync call fails, THE BaseService SHALL return the failed OperationResult from the repository unchanged.
9. WHEN UpdateAsync completes successfully, THE BaseService SHALL return the successful OperationResult from the repository.

### Requirement 6: BaseService DeleteAsync — Persist, Publish

**User Story:** As a framework developer, I want DeleteAsync to delegate to the repository and publish an EntityDeletedEvent after success, so that all delete operations enable event-driven side effects without requiring validation.

#### Acceptance Criteria

1. THE BaseService SHALL define a public virtual method DeleteAsync that accepts a Guid parameter and a CancellationToken and returns Task&lt;OperationResult&gt; (non-generic).
2. WHEN DeleteAsync is called, THE BaseService SHALL NOT run validation (delete operations do not validate a DTO).
3. WHEN DeleteAsync is called, THE BaseService SHALL delegate to IBaseRepository&lt;TDto&gt;.DeleteAsync with the same Guid and CancellationToken.
4. WHEN the repository DeleteAsync call succeeds, THE BaseService SHALL publish an EntityDeletedEvent&lt;TDto&gt; via IEventBus with the EntityId property set to the Guid that was deleted.
5. WHEN the repository DeleteAsync call fails (Success is false), THE BaseService SHALL NOT publish any event.
6. WHEN the repository DeleteAsync call fails, THE BaseService SHALL return the failed OperationResult from the repository unchanged.
7. WHEN DeleteAsync completes successfully, THE BaseService SHALL return the successful OperationResult from the repository.

### Requirement 7: Validation Error Mapping

**User Story:** As a framework developer, I want FluentValidation errors to be mapped into OperationResult.BadRequest with individual error messages, so that API consumers receive structured, actionable validation feedback.

#### Acceptance Criteria

1. WHEN validation fails, THE BaseService SHALL extract the ErrorMessage from each ValidationFailure in the FluentValidation ValidationResult.Errors collection.
2. WHEN validation fails, THE BaseService SHALL pass the extracted error messages as the errors list parameter to OperationResult&lt;TDto&gt;.BadRequest.
3. WHEN validation fails, THE BaseService SHALL use the message "Validation failed" as the message parameter to OperationResult&lt;TDto&gt;.BadRequest.
4. THE OperationResult returned on validation failure SHALL have StatusCode 400 and ErrorCode equal to ErrorCodes.ValidationFailed.

### Requirement 8: Event Publishing Is Fire-and-Forget

**User Story:** As a framework developer, I want event publishing failures to not affect the service operation result, so that side effects (event handlers) cannot break the primary CRUD flow.

#### Acceptance Criteria

1. WHEN the IEventBus.PublishAsync call throws an exception during AddAsync, THE BaseService SHALL NOT propagate the exception to the caller.
2. WHEN the IEventBus.PublishAsync call throws an exception during UpdateAsync, THE BaseService SHALL NOT propagate the exception to the caller.
3. WHEN the IEventBus.PublishAsync call throws an exception during DeleteAsync, THE BaseService SHALL NOT propagate the exception to the caller.
4. WHEN event publishing fails, THE BaseService SHALL still return the successful OperationResult from the repository operation.

### Requirement 9: AddGroundUpServices DI Extension Method

**User Story:** As a framework developer, I want an AddGroundUpServices extension method that registers FluentValidation validators from the calling assembly, so that consuming applications can wire up the service layer with a single method call.

#### Acceptance Criteria

1. THE Services_Project SHALL contain a static ServicesServiceCollectionExtensions class in the GroundUp.Services namespace.
2. THE ServicesServiceCollectionExtensions SHALL define a public static AddGroundUpServices extension method on IServiceCollection that accepts an Assembly parameter for the assembly to scan for validators.
3. WHEN AddGroundUpServices is called, THE method SHALL register all FluentValidation IValidator&lt;T&gt; implementations found in the provided assembly as scoped services in the DI container.
4. THE AddGroundUpServices method SHALL return the IServiceCollection for method chaining.
5. THE ServicesServiceCollectionExtensions SHALL use a file-scoped namespace.
6. THE ServicesServiceCollectionExtensions SHALL have XML documentation comments on the class and the method.
7. THE ServicesServiceCollectionExtensions class SHALL use the sealed modifier (static classes are implicitly sealed in C#, but the class declaration SHALL use the static modifier).

### Requirement 10: NuGet Dependencies for Services Project

**User Story:** As a framework developer, I want the Services project to have the correct NuGet and project references, so that BaseService can use FluentValidation, IEventBus, and IBaseRepository without introducing unnecessary dependencies.

#### Acceptance Criteria

1. THE Services_Project SHALL retain its existing NuGet package reference for FluentValidation.
2. THE Services_Project SHALL include a NuGet package reference for Microsoft.Extensions.DependencyInjection.Abstractions to support the IServiceCollection extension method.
3. THE Services_Project SHALL retain its existing project references to Core_Project, Data_Abstractions, and Events_Project.
4. THE Services_Project SHALL NOT reference GroundUp.Repositories, GroundUp.Data.Postgres, or any provider-specific packages.

### Requirement 11: Unit Tests for BaseService AddAsync

**User Story:** As a framework developer, I want unit tests verifying BaseService AddAsync behavior across validation success, validation failure, repository failure, and no-validator scenarios, so that I have confidence the validate-persist-publish pipeline works correctly.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that AddAsync returns OperationResult.Ok with the created TDto when validation passes and the repository succeeds.
2. THE Unit_Test_Project SHALL contain a test verifying that AddAsync publishes an EntityCreatedEvent&lt;TDto&gt; via IEventBus after a successful repository call.
3. THE Unit_Test_Project SHALL contain a test verifying that AddAsync returns OperationResult.BadRequest with validation error messages when validation fails.
4. THE Unit_Test_Project SHALL contain a test verifying that AddAsync does NOT call the repository when validation fails.
5. THE Unit_Test_Project SHALL contain a test verifying that AddAsync does NOT publish an event when validation fails.
6. THE Unit_Test_Project SHALL contain a test verifying that AddAsync skips validation and calls the repository when no IValidator&lt;TDto&gt; is provided (null).
7. THE Unit_Test_Project SHALL contain a test verifying that AddAsync does NOT publish an event when the repository call fails.
8. THE Unit_Test_Project SHALL contain a test verifying that AddAsync returns the successful OperationResult even when IEventBus.PublishAsync throws an exception.
9. THE Unit_Test_Project SHALL use xUnit and NSubstitute for mocking, consistent with existing test conventions.

### Requirement 12: Unit Tests for BaseService UpdateAsync

**User Story:** As a framework developer, I want unit tests verifying BaseService UpdateAsync behavior across validation success, validation failure, repository failure, and no-validator scenarios, so that I have confidence the validate-persist-publish pipeline works correctly for updates.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns OperationResult.Ok with the updated TDto when validation passes and the repository succeeds.
2. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync publishes an EntityUpdatedEvent&lt;TDto&gt; via IEventBus after a successful repository call.
3. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns OperationResult.BadRequest with validation error messages when validation fails.
4. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync does NOT call the repository when validation fails.
5. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync does NOT publish an event when validation fails.
6. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync skips validation and calls the repository when no IValidator&lt;TDto&gt; is provided (null).
7. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync does NOT publish an event when the repository call fails.
8. THE Unit_Test_Project SHALL contain a test verifying that UpdateAsync returns the successful OperationResult even when IEventBus.PublishAsync throws an exception.

### Requirement 13: Unit Tests for BaseService DeleteAsync

**User Story:** As a framework developer, I want unit tests verifying BaseService DeleteAsync behavior for repository success, repository failure, and event publishing failure, so that I have confidence the persist-publish pipeline works correctly for deletes.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns OperationResult.Ok when the repository succeeds.
2. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync publishes an EntityDeletedEvent&lt;TDto&gt; via IEventBus with the correct EntityId after a successful repository call.
3. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync does NOT publish an event when the repository call fails.
4. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns OperationResult.NotFound when the repository returns NotFound.
5. THE Unit_Test_Project SHALL contain a test verifying that DeleteAsync returns the successful OperationResult even when IEventBus.PublishAsync throws an exception.

### Requirement 14: Unit Tests for BaseService Read Operations

**User Story:** As a framework developer, I want unit tests verifying that GetAllAsync and GetByIdAsync delegate directly to the repository without validation or events, so that I have confidence read operations are pure pass-through.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync returns the OperationResult from the repository unchanged.
2. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync does NOT invoke the validator.
3. THE Unit_Test_Project SHALL contain a test verifying that GetAllAsync does NOT invoke the event bus.
4. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync returns the OperationResult from the repository unchanged.
5. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync does NOT invoke the validator.
6. THE Unit_Test_Project SHALL contain a test verifying that GetByIdAsync does NOT invoke the event bus.

### Requirement 15: Unit Tests for AddGroundUpServices Extension Method

**User Story:** As a framework developer, I want unit tests verifying that AddGroundUpServices correctly registers FluentValidation validators from a given assembly, so that I have confidence the DI wiring works.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that AddGroundUpServices registers IValidator&lt;T&gt; implementations found in the provided assembly as resolvable services.
2. THE Unit_Test_Project SHALL contain a test verifying that AddGroundUpServices returns the IServiceCollection for method chaining.
3. THE Unit_Test_Project SHALL contain a test verifying that when no validators exist in the provided assembly, AddGroundUpServices completes without error and no IValidator services are registered.

### Requirement 16: Property-Based Test for Validation Error Mapping

**User Story:** As a framework developer, I want a property-based test verifying that for all possible sets of FluentValidation errors, BaseService correctly maps every error message into the OperationResult errors list, so that no validation feedback is lost.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL non-empty lists of validation error messages, AddAsync returns an OperationResult with an Errors list containing exactly the same error messages in the same order.
2. THE Unit_Test_Project SHALL contain a property-based test verifying that FOR ALL non-empty lists of validation error messages, UpdateAsync returns an OperationResult with an Errors list containing exactly the same error messages in the same order.
3. THE property-based tests SHALL use xUnit and FsCheck, consistent with existing test conventions.

### Requirement 17: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 3D changes, so that I know the new service layer integrates correctly with the existing codebase.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 3D changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 3D changes, THE Unit_Test_Project SHALL pass all tests including the new BaseService and AddGroundUpServices tests.

### Requirement 18: Enforce Coding Conventions

**User Story:** As a framework developer, I want all Phase 3D types to follow established coding conventions, so that the service layer code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE Services_Project SHALL use file-scoped namespaces in all source files.
2. THE Services_Project SHALL enable nullable reference types.
3. THE Services_Project SHALL place each class in its own separate file.
4. THE BaseService SHALL NOT use the sealed modifier because it is designed for inheritance by derived services.
5. THE ServicesServiceCollectionExtensions SHALL use the static modifier.
6. THE Services_Project SHALL have XML documentation comments on all public types and members.
