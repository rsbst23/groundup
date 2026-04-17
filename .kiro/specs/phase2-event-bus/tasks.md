# Implementation Plan: Phase 2 — Event Bus & Non-Generic OperationResult

## Overview

Build the event bus abstraction and in-process implementation in GroundUp.Events, plus a non-generic OperationResult in GroundUp.Core for void operations. The event bus provides a side-channel for loose coupling — services publish domain events after completing their work, and other modules subscribe without the publisher needing to know about them. InProcessEventBus resolves handlers from DI, calls them sequentially, catches and logs failures.

Git workflow: work on branch `phase-2/event-bus`, commit frequently after each sub-step that compiles.

## Tasks

- [-] 1. Create feature branch and update project files
  - [x] 1.1 Create and checkout the feature branch
    - Create and checkout branch `phase-2/event-bus` from `main`
    - _Requirements: 11.1_

  - [x] 1.2 Add NuGet dependencies to GroundUp.Events csproj
    - Add `Microsoft.Extensions.DependencyInjection.Abstractions` (8.*) package reference
    - Add `Microsoft.Extensions.Logging.Abstractions` (8.*) package reference
    - Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to PropertyGroup
    - Verify GroundUp.Core project reference is already present
    - Verify no other NuGet or project dependencies exist
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 1.3 Add GroundUp.Events project reference to test project
    - Add `<ProjectReference>` for GroundUp.Events to `tests/GroundUp.Tests.Unit/GroundUp.Tests.Unit.csproj`
    - Add `<PackageReference>` for `Microsoft.Extensions.DependencyInjection` (8.*) to the test project (needed to build a real ServiceProvider in DI registration tests)
    - _Requirements: 10.4_

  - [-] 1.4 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "chore: add NuGet deps to Events, add Events ref to test project"
    - _Requirements: 11.1_

- [ ] 2. Define event interfaces and base types
  - [ ] 2.1 Implement IEvent interface
    - Create `src/GroundUp.Events/IEvent.cs`
    - Interface with properties: `Guid EventId`, `DateTime OccurredAt`, `Guid? TenantId`, `Guid? UserId`
    - File-scoped namespace `GroundUp.Events`, XML documentation on interface and all properties
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 2.2 Implement IEventBus interface
    - Create `src/GroundUp.Events/IEventBus.cs`
    - Interface with method: `Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent`
    - File-scoped namespace `GroundUp.Events`, XML documentation on interface and method
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 2.3 Implement IEventHandler&lt;T&gt; interface
    - Create `src/GroundUp.Events/IEventHandler.cs`
    - Generic interface with constraint `where T : IEvent` and method: `Task HandleAsync(T @event, CancellationToken cancellationToken = default)`
    - File-scoped namespace `GroundUp.Events`, XML documentation on interface and method
    - _Requirements: 3.1, 3.2, 3.3_

  - [ ] 2.4 Implement BaseEvent abstract record
    - Create `src/GroundUp.Events/BaseEvent.cs`
    - Abstract record implementing `IEvent` with `init` properties
    - `EventId` defaults to `Guid.NewGuid()`, `OccurredAt` defaults to `DateTime.UtcNow`, `TenantId` and `UserId` default to `null`
    - File-scoped namespace `GroundUp.Events`, XML documentation on record and all properties
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 12.1, 12.2_

  - [ ] 2.5 Implement entity lifecycle events
    - Create `src/GroundUp.Events/EntityCreatedEvent.cs` — record extending BaseEvent with `required T Entity` property
    - Create `src/GroundUp.Events/EntityUpdatedEvent.cs` — record extending BaseEvent with `required T Entity` property
    - Create `src/GroundUp.Events/EntityDeletedEvent.cs` — record extending BaseEvent with `required Guid EntityId` property
    - Each in its own file, file-scoped namespace `GroundUp.Events`, XML documentation
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 12.2, 12.4_

  - [ ] 2.6 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(events): add IEvent, IEventBus, IEventHandler, BaseEvent, entity lifecycle events"
    - _Requirements: 11.1, 12.1, 12.3, 12.5_

- [ ] 3. Implement InProcessEventBus and DI registration
  - [ ] 3.1 Implement InProcessEventBus
    - Create `src/GroundUp.Events/InProcessEventBus.cs`
    - Sealed class implementing `IEventBus`, constructor-injected `IServiceProvider` and `ILogger<InProcessEventBus>`
    - `PublishAsync` resolves all `IEventHandler<T>` from DI via `GetServices<IEventHandler<T>>()`
    - Invokes each handler's `HandleAsync` sequentially in a foreach loop
    - Catches exceptions per handler, logs via `ILogger.LogError` with structured properties (HandlerType, EventType, EventId), continues to next handler
    - Completes without error when no handlers are registered
    - File-scoped namespace `GroundUp.Events`, XML documentation on class and all public members
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9, 12.3, 12.4_

  - [ ] 3.2 Implement EventsServiceCollectionExtensions
    - Create `src/GroundUp.Events/EventsServiceCollectionExtensions.cs`
    - Static class with `AddGroundUpEvents` extension method on `IServiceCollection`
    - Registers `InProcessEventBus` as singleton for `IEventBus`
    - Returns `IServiceCollection` for method chaining
    - File-scoped namespace `GroundUp.Events`, XML documentation on class and method
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 12.3_

  - [ ] 3.3 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(events): add InProcessEventBus and AddGroundUpEvents DI registration"
    - _Requirements: 11.1_

- [ ] 4. Checkpoint — Verify Events project compiles and structure is correct
  - Run `dotnet build groundup.sln` — zero errors
  - Verify all 9 files exist in `src/GroundUp.Events/` (IEvent, IEventBus, IEventHandler, BaseEvent, EntityCreatedEvent, EntityUpdatedEvent, EntityDeletedEvent, InProcessEventBus, EventsServiceCollectionExtensions)
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Add non-generic OperationResult to Core
  - [ ] 5.1 Implement non-generic OperationResult
    - Create `src/GroundUp.Core/Results/OperationResult.NonGeneric.cs`
    - Sealed class `OperationResult` (non-generic) in namespace `GroundUp.Core.Results`
    - Properties: `bool Success`, `string Message`, `List<string>? Errors`, `int StatusCode`, `string? ErrorCode` — all with `init` setters
    - Static factory methods: `Ok(message, statusCode)`, `Fail(message, statusCode, errorCode, errors)`, `NotFound(message)`, `BadRequest(message, errors)`, `Unauthorized(message)`, `Forbidden(message)`
    - Shorthand factories delegate to `Fail` with appropriate status codes and `ErrorCodes` constants
    - XML documentation on class and all public members
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

  - [ ] 5.2 Build and commit
    - Run `dotnet build groundup.sln` to verify compilation
    - Commit: "feat(core): add non-generic OperationResult for void operations"
    - _Requirements: 11.1_

  - [ ]* 5.3 Write property test — Ok factory preserves inputs (Property 4)
    - **Property 4: Non-generic OperationResult Ok factory preserves inputs**
    - For any message string and status code integer, `OperationResult.Ok(message, statusCode)` produces `Success == true`, `Message` equals input message, `StatusCode` equals input status code
    - Test class: `tests/GroundUp.Tests.Unit/Core/OperationResultNonGenericPropertyTests.cs`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 9.2**

  - [ ]* 5.4 Write property test — Fail factory preserves inputs (Property 5)
    - **Property 5: Non-generic OperationResult Fail factory preserves inputs**
    - For any message, status code, optional error code, and optional error list, `OperationResult.Fail(...)` produces `Success == false` with all inputs preserved
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 9.3**

  - [ ]* 5.5 Write property test — Shorthand factories produce correct status codes (Property 6)
    - **Property 6: Non-generic OperationResult shorthand factories produce correct status codes**
    - `NotFound(message)` → 404, `BadRequest(message)` → 400, `Unauthorized(message)` → 401, `Forbidden(message)` → 403; all have `Success == false`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 9.4**

- [ ] 6. Implement BaseEvent and InProcessEventBus tests
  - [ ] 6.1 Write BaseEvent unit tests
    - Create `tests/GroundUp.Tests.Unit/Events/BaseEventTests.cs`
    - Test: `OccurredAt_DefaultsToUtcNow` — verify OccurredAt is within a small window of `DateTime.UtcNow`
    - Test: `TenantIdAndUserId_DefaultToNull` — verify both are null on a freshly created event
    - Use a concrete test record extending BaseEvent for testing (e.g., `private record TestEvent : BaseEvent;`)
    - _Requirements: 4.3, 4.4_

  - [ ]* 6.2 Write property test — BaseEvent EventId uniqueness (Property 1)
    - **Property 1: BaseEvent EventId uniqueness**
    - For any two independently constructed BaseEvent-derived instances, each has a non-empty EventId (not `Guid.Empty`), and the two EventIds are distinct
    - Test class: `tests/GroundUp.Tests.Unit/Events/BaseEventTests.cs` (add to same file)
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 4.2**

  - [ ] 6.3 Write InProcessEventBus unit tests
    - Create `tests/GroundUp.Tests.Unit/Events/InProcessEventBusTests.cs`
    - Test: `PublishAsync_WithRegisteredHandler_HandlerReceivesEvent` — publish EntityCreatedEvent, verify NSubstitute mock handler receives the exact event
    - Test: `PublishAsync_HandlerThrows_CompletesWithoutThrowingAndLogsError` — throwing handler, verify PublishAsync completes and ILogger.LogError is called
    - Test: `PublishAsync_MultipleHandlers_AllReceiveEvent` — register 3 handlers, verify all 3 invoked
    - Test: `PublishAsync_NoHandlers_CompletesWithoutError` — publish with no handlers registered, no exception
    - Use xUnit and NSubstitute, set up IServiceProvider to return mock handlers
    - _Requirements: 10.1, 10.2, 10.3, 10.5_

  - [ ]* 6.4 Write property test — All registered handlers are invoked (Property 2)
    - **Property 2: All registered handlers are invoked**
    - For any event and any set of N registered handlers (N >= 0), `PublishAsync` invokes `HandleAsync` on every handler exactly once
    - Test class: `tests/GroundUp.Tests.Unit/Events/InProcessEventBusPropertyTests.cs`
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 6.2, 6.3**

  - [ ]* 6.5 Write property test — Handler fault isolation (Property 3)
    - **Property 3: Handler fault isolation**
    - For any event and any set of handlers where one or more throw exceptions, `PublishAsync` invokes every handler regardless of prior failures, logs each exception, and completes without propagating any exception
    - Test class: `tests/GroundUp.Tests.Unit/Events/InProcessEventBusPropertyTests.cs` (add to same file)
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 6.4, 6.5**

  - [ ] 6.6 Build and run tests
    - Run `dotnet build groundup.sln` to verify compilation
    - Run `dotnet test` to verify all tests pass
    - Commit: "test(events): add BaseEvent and InProcessEventBus unit and property tests"
    - _Requirements: 11.2_

- [ ] 7. Implement DI registration tests
  - [ ] 7.1 Write AddGroundUpEvents unit tests
    - Create `tests/GroundUp.Tests.Unit/Events/EventsServiceCollectionExtensionsTests.cs`
    - Test: `AddGroundUpEvents_RegistersSingletonIEventBus` — build a real ServiceProvider, resolve IEventBus, verify it is an InProcessEventBus instance
    - Test: `AddGroundUpEvents_ReturnsServiceCollection` — verify return value is the same IServiceCollection instance
    - _Requirements: 7.2, 7.3_

  - [ ] 7.2 Build and run tests
    - Run `dotnet build groundup.sln` to verify compilation
    - Run `dotnet test` to verify all tests pass
    - Commit: "test(events): add DI registration tests for AddGroundUpEvents"
    - _Requirements: 11.2_

- [ ] 8. Final checkpoint — Full build and test verification
  - Run `dotnet build groundup.sln` — zero errors
  - Run `dotnet test` — all tests pass including new event bus and OperationResult tests
  - Verify all 9 source files in `src/GroundUp.Events/`
  - Verify `src/GroundUp.Core/Results/OperationResult.NonGeneric.cs` exists
  - Verify test files in `tests/GroundUp.Tests.Unit/Events/` and `tests/GroundUp.Tests.Unit/Core/`
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck.Xunit with `[Property]` attribute and minimum 100 iterations
- Git commits happen after each sub-step that compiles — small, frequent commits
- All code uses C# with file-scoped namespaces, nullable reference types, and `sealed` where appropriate
- The design uses specific C# code (not pseudocode), so no language selection was needed
- 6 correctness properties map to FsCheck property-based tests (Properties 1–6)
- 8 additional unit tests verify specific behavior (DI registration, sequential ordering, zero-handler edge case, BaseEvent defaults)
