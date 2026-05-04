# Implementation Plan: Base Class Refactor

## Overview

Refactor `BaseController<TDto>` and `BaseService<TDto>` from generic CRUD base classes into non-generic infrastructure-only base classes. The repository layer remains unchanged. Each task group is designed to be small enough to commit independently (~10-15 files max) while keeping the solution compilable after each step.

## Tasks

- [x] 1. Refactor BaseController — remove generics, keep helpers
  - [x] 1.1 Rewrite `src/GroundUp.Api/Controllers/BaseController.cs` to remove the `<TDto>` generic parameter, remove all 5 virtual CRUD methods (GetAll, GetById, Create, Update, Delete), remove the `Service` property, and extract `AddPaginationHeaders<T>(PaginatedData<T>)` as a new protected helper. Keep `ToActionResult<T>` and `ToActionResult` helpers, `[ApiController]`, and `[Route("api/[controller]")]` attributes.
    - The class signature changes from `BaseController<TDto> : ControllerBase where TDto : class` to `BaseController : ControllerBase`
    - Remove the `using GroundUp.Services;` import (no longer depends on BaseService)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 8.3_

  - [x] 1.2 Refactor `BaseService` in `src/GroundUp.Services/BaseService.cs` to remove the `<TDto>` generic parameter, remove all 5 virtual CRUD methods (GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync), remove the `Repository` property, change constructor to accept `IEventBus` + `IServiceProvider`, make `ValidateAsync` a generic method that resolves `IValidator<TDto>` from the service provider at runtime, and keep `PublishEventSafelyAsync` as-is.
    - The class signature changes from `BaseService<TDto> where TDto : class` to `BaseService`
    - Remove `IBaseRepository<TDto>` dependency — derived services inject their own repository
    - Add `using Microsoft.Extensions.DependencyInjection;` for `GetService<T>()`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 11.1, 11.4_

- [x] 2. Checkpoint — Verify framework projects compile
  - Run `dotnet build src/GroundUp.Api/GroundUp.Api.csproj` and `dotnet build src/GroundUp.Services/GroundUp.Services.csproj` to confirm the framework layer compiles. The sample app and tests will NOT compile yet — that's expected.
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Migrate simple entity services (TodoItem, Customer)
  - [x] 3.1 Rewrite `samples/GroundUp.Sample/Services/TodoItemService.cs` to extend non-generic `BaseService`, inject `IBaseRepository<TodoItemDto>` + `IEventBus` + `IServiceProvider`, and define 5 explicit CRUD methods (GetAllAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync) that delegate to the repository with validation and event publishing via base helpers.
    - _Requirements: 2.6, 4.1, 4.3, 10.1, 10.2, 10.3, 10.4, 11.2_

  - [x] 3.2 Rewrite `samples/GroundUp.Sample/Services/CustomerService.cs` to extend non-generic `BaseService`, inject `IBaseRepository<CustomerDto>` + `IEventBus` + `IServiceProvider`, and define 5 explicit CRUD methods following the same pattern as TodoItemService.
    - _Requirements: 2.6, 4.1, 4.3, 10.1, 10.2, 10.3, 10.4_

- [x] 4. Migrate complex entity service (Order)
  - [x] 4.1 Rewrite `samples/GroundUp.Sample/Services/OrderService.cs` to extend non-generic `BaseService`, inject `OrderRepository` + `IEventBus` + `IServiceProvider`, and define methods with correct per-operation DTO types: `GetAllAsync` returning `PaginatedData<OrderListDto>`, `GetByIdAsync` returning `OrderDetailDto`, `CreateAsync` accepting `CreateOrderDto` returning `OrderDetailDto`, `UpdateAsync` accepting `UpdateOrderDto` returning `OrderDetailDto`, `DeleteAsync` returning `OperationResult`.
    - Remove the `BaseService<OrderListDto>` inheritance and `_orderRepository` field naming — use `_repository` directly
    - Use `ValidateAsync<CreateOrderDto>` and `ValidateAsync<UpdateOrderDto>` for per-DTO validation
    - _Requirements: 5.2, 5.7, 10.1, 10.2, 10.3, 10.4, 11.2, 11.3_

- [x] 5. Migrate tenant-scoped entity service (Project)
  - [x] 5.1 Rewrite `samples/GroundUp.Sample/Services/ProjectService.cs` to extend non-generic `BaseService`, inject `IBaseRepository<ProjectDto>` + `IEventBus` + `IServiceProvider`, and define 5 explicit CRUD methods following the same pattern as TodoItemService.
    - _Requirements: 2.6, 6.1, 6.2, 6.4_

- [ ] 6. Migrate simple entity controllers (TodoItems, Customers)
  - [ ] 6.1 Rewrite `samples/GroundUp.Sample/Controllers/TodoItemsController.cs` to extend non-generic `BaseController`, inject `TodoItemService` directly (not `BaseService<TodoItemDto>`), and define 5 endpoint methods with explicit HTTP attributes, calling the service and using `ToActionResult` + `AddPaginationHeaders` from the base class.
    - _Requirements: 1.7, 4.2, 4.4, 4.5, 8.1, 8.2_

  - [ ] 6.2 Rewrite `samples/GroundUp.Sample/Controllers/CustomersController.cs` to extend non-generic `BaseController`, inject `CustomerService` directly, and define 5 endpoint methods following the same pattern as TodoItemsController.
    - _Requirements: 1.7, 4.2, 4.4, 4.5, 8.1, 8.2_

- [ ] 7. Migrate complex entity controller (Orders)
  - [ ] 7.1 Rewrite `samples/GroundUp.Sample/Controllers/OrdersController.cs` to extend non-generic `BaseController`, inject `OrderService` directly, and define 5 endpoint methods with correct per-operation DTO types: `GetAll` returning `PaginatedData<OrderListDto>`, `GetById` returning `OrderDetailDto`, `Create` accepting `CreateOrderDto` returning `OrderDetailDto`, `Update` accepting `UpdateOrderDto` returning `OrderDetailDto`, `Delete` returning `OperationResult`.
    - Remove the `BaseController<OrderListDto>` inheritance and the `_orderService` field — use `_service` directly
    - Use standard method names (Create, Update, GetById) — no more `CreateOrder`/`UpdateOrder` disambiguation
    - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 5.7, 8.1, 8.2, 8.4_

- [ ] 8. Migrate tenant-scoped entity controller (Projects)
  - [ ] 8.1 Rewrite `samples/GroundUp.Sample/Controllers/ProjectsController.cs` to extend non-generic `BaseController`, inject `ProjectService` directly, and define 5 endpoint methods following the same pattern as TodoItemsController.
    - _Requirements: 1.7, 6.3, 6.4_

- [ ] 9. Update DI registration in Program.cs
  - [ ] 9.1 Update `samples/GroundUp.Sample/Program.cs` to remove all `BaseService<TDto>` registrations and register services by their concrete type only. Specifically:
    - Change `builder.Services.AddScoped<BaseService<TodoItemDto>, TodoItemService>()` → `builder.Services.AddScoped<TodoItemService>()`
    - Change `builder.Services.AddScoped<BaseService<CustomerDto>, CustomerService>()` → `builder.Services.AddScoped<CustomerService>()`
    - Remove `builder.Services.AddScoped<BaseService<OrderListDto>, OrderService>()` (keep `builder.Services.AddScoped<OrderService>()`)
    - Change `builder.Services.AddScoped<BaseService<ProjectDto>, ProjectService>()` → `builder.Services.AddScoped<ProjectService>()`
    - Keep all `IBaseRepository<TDto>` and concrete repository registrations unchanged
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 12.6_

- [ ] 10. Checkpoint — Verify full solution compiles and Swagger loads
  - Run `dotnet build groundup.sln` to confirm the entire solution compiles.
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Fix/update unit tests for BaseController and BaseService
  - [ ] 11.1 Update `tests/GroundUp.Tests.Unit/Api/BaseControllerTests.cs` to test the non-generic `BaseController`: verify `ToActionResult<T>` and `ToActionResult` map all status codes correctly, verify `AddPaginationHeaders` sets the four expected headers, verify the class has `[ApiController]` and `[Route("api/[controller]")]` attributes, verify no CRUD methods exist, verify the class is not generic.
    - Create a concrete test subclass of `BaseController` to test the protected helpers
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 8.3_

  - [ ] 11.2 Update `tests/GroundUp.Tests.Unit/Api/BaseControllerPropertyTests.cs` to test properties against the non-generic `BaseController`: Property 1 (ToActionResult status code mapping) and Property 2 (AddPaginationHeaders round-trip).
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 11.3 Update `tests/GroundUp.Tests.Unit/Services/BaseServiceTests.cs` to test the non-generic `BaseService`: verify `ValidateAsync<TDto>` resolves validators from IServiceProvider, verify it returns null when no validator registered, verify it returns BadRequest with errors when validation fails, verify `PublishEventSafelyAsync` publishes events and swallows exceptions, verify no CRUD methods exist, verify the class is not generic.
    - Create a concrete test subclass of `BaseService` to test the protected helpers
    - Use NSubstitute to mock IServiceProvider, IValidator<T>, and IEventBus
    - _Requirements: 2.1, 2.2, 2.4, 2.5, 2.7, 10.5, 11.1, 11.4, 11.5_

  - [ ] 11.4 Update `tests/GroundUp.Tests.Unit/Services/BaseServicePropertyTests.cs` to test properties against the non-generic `BaseService`: Property 3 (ValidateAsync correctness), Property 4 (ValidateAsync resolves correct validator per type), Property 5 (PublishEventSafelyAsync swallows exceptions).
    - _Requirements: 2.1, 2.2, 2.7, 10.5, 11.1, 11.4, 11.5_

- [ ] 12. Fix/update integration tests
  - [ ] 12.1 Update `tests/GroundUp.Tests.Integration/Filtering/OrderFilteringTests.cs` to work with the refactored controllers and services. Verify that existing HTTP routes still work (GET /api/orders, GET /api/orders/{id}, POST /api/orders, PUT /api/orders/{id}, DELETE /api/orders/{id}) and that the correct DTO types are returned.
    - _Requirements: 9.4, 9.5, 12.5_

  - [ ]* 12.2 Add integration test verifying Swagger endpoint loads without ambiguous HTTP method errors
    - GET /swagger/v1/swagger.json should return 200 with valid OpenAPI JSON
    - Verify OrdersController endpoints show correct DTO types in the schema
    - _Requirements: 8.1, 8.2, 8.4, 12.5_

- [ ] 13. Final checkpoint — Full build and test verification
  - Run `dotnet build groundup.sln` and `dotnet test groundup.sln` to confirm everything compiles and all tests pass.
  - Verify no Swagger ambiguity errors by checking the OpenAPI spec generation.
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The Settings controllers (SettingGroupsController, SettingDefinitionsController, SettingLevelsController) already extend `ControllerBase` directly with their own `ToActionResult` helpers — they are NOT affected by this refactor
- BaseRepository and BaseTenantRepository are completely unchanged (Requirement 3)
- All sample repositories (TodoItemRepository, CustomerRepository, OrderRepository, ProjectRepository) are unchanged
- All Mapperly mappers are unchanged
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- The solution may not compile between tasks 1 and 9 (sample app references old generic types), but framework projects (tasks 1-2) and the full solution (task 10+) have explicit compile checkpoints
