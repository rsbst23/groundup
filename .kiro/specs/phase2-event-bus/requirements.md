# Requirements Document

## Introduction

Phase 2 of the GroundUp framework builds the event bus abstraction and in-process implementation in the GroundUp.Events project. The event bus provides a side-channel for loose coupling between modules — services publish domain events after completing their work, and other modules (Audit, Notifications, etc.) subscribe without the publisher needing to know about them. This phase also adds a non-generic OperationResult to GroundUp.Core for void operations (e.g., DeleteAsync).

## Glossary

- **Events_Project**: The GroundUp.Events class library project containing event interfaces, base event types, and the in-process event bus implementation.
- **Core_Project**: The GroundUp.Core class library project containing foundational shared types.
- **IEvent**: The base interface for all domain events in the framework, carrying event identity and context metadata.
- **IEventBus**: The interface defining the contract for publishing events to registered handlers.
- **IEventHandler**: The generic interface (IEventHandler&lt;T&gt; where T : IEvent) that event subscribers implement to handle specific event types.
- **BaseEvent**: An abstract record implementing IEvent that provides default values for EventId and OccurredAt.
- **EntityCreatedEvent**: A generic record extending BaseEvent published when an entity is created, carrying the created entity data.
- **EntityUpdatedEvent**: A generic record extending BaseEvent published when an entity is updated, carrying the updated entity data.
- **EntityDeletedEvent**: A generic record extending BaseEvent published when an entity is deleted, carrying the deleted entity identifier.
- **InProcessEventBus**: A sealed class implementing IEventBus that resolves handlers from the DI container and invokes them sequentially within the same process.
- **OperationResult_NonGeneric**: A non-generic OperationResult class in Core_Project for void operations that carry success/failure status without a data payload.
- **EventsServiceCollectionExtensions**: A static extension class providing the AddGroundUpEvents() method for DI registration of event bus services.
- **Unit_Test_Project**: The GroundUp.Tests.Unit xUnit test project.

## Requirements

### Requirement 1: Define IEvent Interface

**User Story:** As a framework developer, I want a base event interface with identity and context properties, so that all domain events carry consistent metadata for tracking and multi-tenant correlation.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an IEvent interface with properties: Guid EventId, DateTime OccurredAt, Guid? TenantId, Guid? UserId.
2. THE IEvent interface SHALL have XML documentation comments on the interface and all properties.
3. THE IEvent interface SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 2: Define IEventBus Interface

**User Story:** As a framework developer, I want an event bus interface with a generic publish method, so that services can publish domain events without depending on a specific bus implementation.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an IEventBus interface with a single method: Task PublishAsync&lt;T&gt;(T @event, CancellationToken cancellationToken = default) where T : IEvent.
2. THE IEventBus interface SHALL have XML documentation comments on the interface and the PublishAsync method.
3. THE IEventBus interface SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 3: Define IEventHandler Interface

**User Story:** As a framework developer, I want a generic event handler interface, so that modules can subscribe to specific event types by implementing a single handler method.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an IEventHandler&lt;T&gt; interface with a generic constraint where T : IEvent and a single method: Task HandleAsync(T @event, CancellationToken cancellationToken = default).
2. THE IEventHandler interface SHALL have XML documentation comments on the interface and the HandleAsync method.
3. THE IEventHandler interface SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 4: Build BaseEvent Abstract Record

**User Story:** As a framework developer, I want an abstract base event record with sensible defaults, so that concrete event types inherit standard metadata without boilerplate.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an abstract record BaseEvent that implements IEvent.
2. THE BaseEvent SHALL initialize EventId to a new Guid value by default.
3. THE BaseEvent SHALL initialize OccurredAt to DateTime.UtcNow by default.
4. THE BaseEvent SHALL initialize TenantId and UserId to null by default.
5. THE BaseEvent SHALL have XML documentation comments on the record and all properties.
6. THE BaseEvent SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 5: Build Entity Lifecycle Events

**User Story:** As a framework developer, I want generic entity lifecycle event records, so that the service layer can publish standardized events when entities are created, updated, or deleted.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an EntityCreatedEvent&lt;T&gt; record extending BaseEvent with a required property of type T named Entity.
2. THE Events_Project SHALL contain an EntityUpdatedEvent&lt;T&gt; record extending BaseEvent with a required property of type T named Entity.
3. THE Events_Project SHALL contain an EntityDeletedEvent&lt;T&gt; record extending BaseEvent with a required property of type Guid named EntityId.
4. THE EntityCreatedEvent, EntityUpdatedEvent, and EntityDeletedEvent SHALL each have XML documentation comments on the record and all properties.
5. THE EntityCreatedEvent, EntityUpdatedEvent, and EntityDeletedEvent SHALL each use a file-scoped namespace under GroundUp.Events.

### Requirement 6: Build InProcessEventBus

**User Story:** As a framework developer, I want an in-process event bus that resolves handlers from DI and invokes them sequentially, so that domain events are dispatched within the same process without requiring external infrastructure.

#### Acceptance Criteria

1. THE Events_Project SHALL contain a sealed class InProcessEventBus that implements IEventBus.
2. WHEN PublishAsync is called, THE InProcessEventBus SHALL resolve all registered IEventHandler&lt;T&gt; instances from the IServiceProvider for the given event type.
3. WHEN PublishAsync is called with one or more registered handlers, THE InProcessEventBus SHALL invoke each handler's HandleAsync method sequentially.
4. IF a handler throws an exception during HandleAsync, THEN THE InProcessEventBus SHALL catch the exception, log it via ILogger&lt;InProcessEventBus&gt;, and continue invoking remaining handlers.
5. IF a handler throws an exception during HandleAsync, THEN THE InProcessEventBus SHALL allow the PublishAsync call to complete without propagating the exception to the publisher.
6. WHEN PublishAsync is called with no registered handlers for the event type, THE InProcessEventBus SHALL complete without error.
7. THE InProcessEventBus SHALL accept IServiceProvider and ILogger&lt;InProcessEventBus&gt; via constructor injection.
8. THE InProcessEventBus SHALL have XML documentation comments on the class and all public members.
9. THE InProcessEventBus SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 7: Build DI Registration Extension Method

**User Story:** As a consuming application developer, I want a single AddGroundUpEvents() extension method, so that I can register the event bus and its dependencies with one call in Program.cs.

#### Acceptance Criteria

1. THE Events_Project SHALL contain an EventsServiceCollectionExtensions static class with a public static AddGroundUpEvents extension method on IServiceCollection.
2. WHEN AddGroundUpEvents is called, THE extension method SHALL register InProcessEventBus as the implementation for IEventBus with singleton lifetime.
3. THE AddGroundUpEvents method SHALL return the IServiceCollection instance to support method chaining.
4. THE EventsServiceCollectionExtensions SHALL have XML documentation comments on the class and method.
5. THE EventsServiceCollectionExtensions SHALL use a file-scoped namespace under GroundUp.Events.

### Requirement 8: Add NuGet Dependencies for InProcessEventBus

**User Story:** As a framework developer, I want the Events project to reference only the minimal NuGet abstractions needed for DI and logging, so that InProcessEventBus can resolve handlers and log failures without pulling in heavy runtime dependencies.

#### Acceptance Criteria

1. THE Events_Project SHALL include a NuGet package reference for Microsoft.Extensions.Logging.Abstractions.
2. THE Events_Project SHALL include a NuGet package reference for Microsoft.Extensions.DependencyInjection.Abstractions.
3. THE Events_Project SHALL reference Core_Project as its only project dependency.
4. THE Events_Project SHALL have no NuGet dependencies beyond Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions.

### Requirement 9: Build Non-Generic OperationResult

**User Story:** As a framework developer, I want a non-generic OperationResult class for void operations, so that methods like DeleteAsync can return success/failure status and error details without requiring a data payload.

#### Acceptance Criteria

1. THE Core_Project SHALL contain a sealed OperationResult class (non-generic) with properties: bool Success, string Message, List&lt;string&gt;? Errors, int StatusCode, string? ErrorCode.
2. THE OperationResult (non-generic) SHALL provide a static Ok factory method that creates a successful result with an optional message and status code.
3. THE OperationResult (non-generic) SHALL provide a static Fail factory method that creates a failure result with message, status code, optional error code, and optional error list.
4. THE OperationResult (non-generic) SHALL provide static NotFound, BadRequest, Unauthorized, and Forbidden factory methods matching the signatures of OperationResult&lt;T&gt; counterparts (without the Data parameter).
5. THE OperationResult (non-generic) SHALL have XML documentation comments on the class and all public members.
6. THE OperationResult (non-generic) SHALL reside in the same namespace (GroundUp.Core.Results) as OperationResult&lt;T&gt;.

### Requirement 10: Unit Tests for InProcessEventBus

**User Story:** As a framework developer, I want unit tests verifying event bus behavior, so that I have confidence that event publishing, handler invocation, and failure isolation work correctly.

#### Acceptance Criteria

1. THE Unit_Test_Project SHALL contain a test verifying that WHEN an EntityCreatedEvent is published via InProcessEventBus, a registered IEventHandler&lt;EntityCreatedEvent&gt; receives the event.
2. THE Unit_Test_Project SHALL contain a test verifying that WHEN a handler throws an exception, the PublishAsync call completes without throwing and the exception is logged.
3. THE Unit_Test_Project SHALL contain a test verifying that WHEN multiple handlers are registered for the same event type, all handlers receive the event.
4. THE Unit_Test_Project SHALL reference the Events_Project.
5. THE Unit_Test_Project SHALL use xUnit and NSubstitute for test infrastructure, consistent with the existing test conventions.

### Requirement 11: Solution Build Verification

**User Story:** As a framework developer, I want the entire solution to compile after all Phase 2 changes, so that I know the new types integrate correctly with the existing codebase.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed after all Phase 2 changes, THE Solution SHALL compile with zero errors.
2. WHEN `dotnet test` is executed after all Phase 2 changes, THE Unit_Test_Project SHALL pass all tests including the new event bus tests.

### Requirement 12: Enforce Coding Conventions for Event Types

**User Story:** As a framework developer, I want all Phase 2 types to follow established coding conventions, so that the event bus code is consistent with the rest of the framework.

#### Acceptance Criteria

1. THE Events_Project SHALL use file-scoped namespaces in all source files.
2. THE Events_Project SHALL use records for all event types (BaseEvent, EntityCreatedEvent, EntityUpdatedEvent, EntityDeletedEvent).
3. THE Events_Project SHALL use the sealed modifier on InProcessEventBus and EventsServiceCollectionExtensions.
4. THE Events_Project SHALL place each class, interface, and record in its own separate file.
5. THE Events_Project SHALL enable nullable reference types.
