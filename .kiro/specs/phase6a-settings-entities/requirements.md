# Requirements Document

## Introduction

Phase 6A establishes the core entities, enums, interfaces, and EF Core data layer for a data-driven cascading multi-tenant settings infrastructure within the GroundUp framework. Unlike a traditional hardcoded settings enum, this design treats cascade levels as first-class entities forming a self-referencing tree, allowing each consuming application to define its own hierarchy (e.g., User → Team → Department → Tenant → System, or simply Tenant → System).

Settings are foundational to every application, so they live within the existing GroundUp projects — entities and interfaces in `GroundUp.Core`, EF configurations in `GroundUp.Data.Postgres` — rather than in a separate module. Phase 6A focuses exclusively on the data model: entity definitions, the `SettingDataType` enum, the `ISettingEncryptionProvider` interface, DTOs for all setting types, and Fluent API EF Core configurations with proper indexes, foreign keys, and constraints.

Phase 6A does NOT include cascading resolution logic (Phase 6B), caching (Phase 6C), controllers or API endpoints (Phase 6C), integration tests for cascading behavior (Phase 6D), or data seeders (Phase 6C).

## Glossary

- **SettingLevel**: An entity representing a named tier in the cascade hierarchy (e.g., "System", "Tenant", "User"). Forms a self-referencing tree via `ParentId`. The root level has `ParentId` of null. Resolution walks UP the parent chain from the most specific level to the root.
- **SettingGroup**: An entity that logically groups related setting definitions into a composite object (e.g., a "DatabaseConnection" group containing Host, Port, Username, Password settings). Has its own metadata for UI rendering.
- **SettingDefinition**: An entity that declares a single setting's key, data type, default value, UI metadata, validation rules, conditional dependencies, and encryption flags. Each definition stores ALL metadata needed to render a settings UI.
- **SettingOption**: An entity representing a selectable option for a setting definition of select or multi-select type. Supports cascading options via `ParentOptionValue`.
- **SettingValue**: An entity storing the actual value of a setting at a specific level and scope. The `Value` column is always a string; the `DataType` on the definition tells the service how to deserialize it.
- **SettingDefinitionLevel**: A junction entity representing the many-to-many relationship between setting definitions and setting levels, declaring which levels a setting can be overridden at.
- **SettingDataType**: An enum defining the supported data types for setting values: String, Int, Long, Decimal, Bool, DateTime, Date, Json.
- **ISettingEncryptionProvider**: An interface that consuming applications implement to provide encryption and decryption of sensitive setting values at rest.
- **SettingDependencyOperator**: A static class defining string constants for conditional dependency operators: Equals, NotEquals, Contains, In.
- **BaseEntity**: The abstract base class in `GroundUp.Core` providing a UUID v7 `Id` property for all entities.
- **IAuditable**: The opt-in interface in `GroundUp.Core` for automatic audit field population (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy).
- **GroundUpDbContext**: The abstract base DbContext in `GroundUp.Data.Postgres` that consuming applications inherit from. Configures UUID v7 generation and soft delete filters.
- **Fluent API**: EF Core's code-based configuration approach using `IEntityTypeConfiguration<T>`. The GroundUp framework uses Fluent API exclusively — never data annotations for schema.

## Requirements

### Requirement 1: SettingLevel Entity

**User Story:** As a framework consumer, I want cascade levels defined as entities forming a self-referencing tree, so that each application can define its own settings hierarchy without being constrained by a hardcoded enum.

#### Acceptance Criteria

1. THE SettingLevel entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Core/Entities/Settings/SettingLevel.cs`.
2. THE SettingLevel entity SHALL have a `Name` property of type `string` (required, max length 100) representing the level name (e.g., "System", "Tenant", "User").
3. THE SettingLevel entity SHALL have a `Description` property of type `string?` (optional, max length 500).
4. THE SettingLevel entity SHALL have a `ParentId` property of type `Guid?` forming a self-referencing foreign key to another SettingLevel. A null `ParentId` indicates the root level.
5. THE SettingLevel entity SHALL have a `Parent` navigation property of type `SettingLevel?` and a `Children` navigation collection of type `ICollection<SettingLevel>` for traversing the hierarchy.
6. THE SettingLevel entity SHALL have a `DisplayOrder` property of type `int` for UI ordering of levels.
7. THE SettingLevel entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 2: SettingGroup Entity

**User Story:** As a framework consumer, I want to group related settings into logical composites with their own metadata, so that the UI can render settings in organized sections (e.g., a "Database Connection" group containing Host, Port, Username, Password).

#### Acceptance Criteria

1. THE SettingGroup entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Core/Entities/Settings/SettingGroup.cs`.
2. THE SettingGroup entity SHALL have a `Key` property of type `string` (required, max length 200, unique) serving as the programmatic identifier (e.g., "DatabaseConnection").
3. THE SettingGroup entity SHALL have a `DisplayName` property of type `string` (required, max length 200) for UI rendering.
4. THE SettingGroup entity SHALL have a `Description` property of type `string?` (optional, max length 1000).
5. THE SettingGroup entity SHALL have an `Icon` property of type `string?` (optional, max length 100) for storing a CSS class or icon name for UI rendering.
6. THE SettingGroup entity SHALL have a `DisplayOrder` property of type `int` for controlling the order groups appear in the UI.
7. THE SettingGroup entity SHALL have a `Settings` navigation collection of type `ICollection<SettingDefinition>` for accessing all definitions in the group.
8. THE SettingGroup entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 3: SettingDefinition Entity

**User Story:** As a framework consumer, I want setting definitions that store all metadata needed to render a UI — display info, validation rules, conditional dependencies, and encryption flags — so that settings can be fully data-driven without hardcoded UI logic.

#### Acceptance Criteria

1. THE SettingDefinition entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Core/Entities/Settings/SettingDefinition.cs`.
2. THE SettingDefinition entity SHALL have a `Key` property of type `string` (required, max length 200, unique) serving as the programmatic identifier (e.g., "MaxUploadSizeMB").
3. THE SettingDefinition entity SHALL have a `DataType` property of type `SettingDataType` enum specifying how the stored string value is deserialized.
4. THE SettingDefinition entity SHALL have a `DefaultValue` property of type `string?` (optional, max length 4000) storing the serialized default value.
5. THE SettingDefinition entity SHALL have a `GroupId` property of type `Guid?` as an optional foreign key to `SettingGroup`, and a `Group` navigation property of type `SettingGroup?`.
6. THE SettingDefinition entity SHALL have UI metadata properties: `DisplayName` (string, required, max length 200), `Description` (string?, max length 1000), `Category` (string?, max length 200), `DisplayOrder` (int), `IsVisible` (bool, default true), `IsReadOnly` (bool, default false).
7. THE SettingDefinition entity SHALL have a `AllowMultiple` property of type `bool` (default false) indicating whether the setting supports multiple values stored as a JSON array.
8. THE SettingDefinition entity SHALL have encryption properties: `IsEncrypted` (bool, default false) indicating the value is encrypted at rest, and `IsSecret` (bool, default false) indicating the value is masked in API responses.
9. THE SettingDefinition entity SHALL have validation properties: `IsRequired` (bool, default false), `MinValue` (string?, max length 100), `MaxValue` (string?, max length 100), `MinLength` (int?), `MaxLength` (int?), `RegexPattern` (string?, max length 500), `ValidationMessage` (string?, max length 500).
10. THE SettingDefinition entity SHALL have conditional dependency properties: `DependsOnKey` (string?, max length 200), `DependsOnOperator` (string?, max length 20), `DependsOnValue` (string?, max length 1000).
11. THE SettingDefinition entity SHALL have a `CustomValidatorType` property of type `string?` (max length 500) for specifying a fully qualified type name for custom validation logic.
12. THE SettingDefinition entity SHALL have navigation collections: `Options` of type `ICollection<SettingOption>`, `Values` of type `ICollection<SettingValue>`, and `AllowedLevels` of type `ICollection<SettingDefinitionLevel>`.
13. THE SettingDefinition entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 4: SettingOption Entity

**User Story:** As a framework consumer, I want setting definitions to support predefined selectable options with labels and ordering, so that select and multi-select settings can be rendered as dropdowns or checkbox lists in the UI.

#### Acceptance Criteria

1. THE SettingOption entity SHALL extend `BaseEntity` and reside in `src/GroundUp.Core/Entities/Settings/SettingOption.cs`.
2. THE SettingOption entity SHALL have a `SettingDefinitionId` property of type `Guid` as a required foreign key to `SettingDefinition`, and a `SettingDefinition` navigation property.
3. THE SettingOption entity SHALL have a `Value` property of type `string` (required, max length 1000) storing the option's programmatic value.
4. THE SettingOption entity SHALL have a `Label` property of type `string` (required, max length 200) storing the display text for the option.
5. THE SettingOption entity SHALL have a `DisplayOrder` property of type `int` for controlling the order options appear in the UI.
6. THE SettingOption entity SHALL have an `IsDefault` property of type `bool` (default false) indicating whether this option is pre-selected.
7. THE SettingOption entity SHALL have a `ParentOptionValue` property of type `string?` (optional, max length 1000) for cascading options that filter based on a parent setting's selected value.
8. THE SettingOption entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 5: SettingValue Entity

**User Story:** As a framework consumer, I want to store setting values at specific cascade levels and scopes, so that the resolution service can walk up the hierarchy to find the effective value for any given context.

#### Acceptance Criteria

1. THE SettingValue entity SHALL extend `BaseEntity` and implement `IAuditable`, and reside in `src/GroundUp.Core/Entities/Settings/SettingValue.cs`.
2. THE SettingValue entity SHALL have a `SettingDefinitionId` property of type `Guid` as a required foreign key to `SettingDefinition`, and a `SettingDefinition` navigation property.
3. THE SettingValue entity SHALL have a `LevelId` property of type `Guid` as a required foreign key to `SettingLevel`, and a `Level` navigation property.
4. THE SettingValue entity SHALL have a `ScopeId` property of type `Guid?` representing the specific entity at that level (e.g., a TenantId or UserId). A null `ScopeId` indicates the root/system level where no specific entity scope applies.
5. THE SettingValue entity SHALL have a `Value` property of type `string?` (max length 4000) storing the serialized value (encrypted if the definition's `IsEncrypted` flag is true).
6. THE SettingValue entity SHALL have a unique composite constraint on (`SettingDefinitionId`, `LevelId`, `ScopeId`) to prevent duplicate values for the same setting at the same level and scope.
7. THE SettingValue entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 6: SettingDefinitionLevel Junction Entity

**User Story:** As a framework consumer, I want to declare which cascade levels each setting definition can be overridden at, so that the system enforces that settings are only set at their allowed levels.

#### Acceptance Criteria

1. THE SettingDefinitionLevel entity SHALL extend `BaseEntity` and reside in `src/GroundUp.Core/Entities/Settings/SettingDefinitionLevel.cs`.
2. THE SettingDefinitionLevel entity SHALL have a `SettingDefinitionId` property of type `Guid` as a required foreign key to `SettingDefinition`, and a `SettingDefinition` navigation property.
3. THE SettingDefinitionLevel entity SHALL have a `SettingLevelId` property of type `Guid` as a required foreign key to `SettingLevel`, and a `SettingLevel` navigation property.
4. THE SettingDefinitionLevel entity SHALL have a unique composite constraint on (`SettingDefinitionId`, `SettingLevelId`) to prevent duplicate entries.
5. THE SettingDefinitionLevel entity SHALL be a sealed class with XML doc comments on all public members.

### Requirement 7: SettingDataType Enum

**User Story:** As a framework consumer, I want a well-defined enum of supported setting data types, so that the service layer knows how to deserialize the string value column into the correct CLR type.

#### Acceptance Criteria

1. THE SettingDataType enum SHALL reside in `src/GroundUp.Core/Enums/SettingDataType.cs`.
2. THE SettingDataType enum SHALL define the following members: `String = 0`, `Int = 1`, `Long = 2`, `Decimal = 3`, `Bool = 4`, `DateTime = 5`, `Date = 6`, `Json = 7`.
3. THE SettingDataType enum SHALL have XML doc comments on the enum type and each member describing its purpose and the CLR type it maps to.

### Requirement 8: ISettingEncryptionProvider Interface

**User Story:** As a framework consumer, I want an encryption provider interface that I can implement with my own encryption strategy, so that sensitive setting values are encrypted at rest without the framework dictating a specific encryption algorithm.

#### Acceptance Criteria

1. THE ISettingEncryptionProvider interface SHALL reside in `src/GroundUp.Core/Abstractions/ISettingEncryptionProvider.cs`.
2. THE ISettingEncryptionProvider interface SHALL declare an `Encrypt` method accepting a `string` plaintext parameter and returning a `string` ciphertext.
3. THE ISettingEncryptionProvider interface SHALL declare a `Decrypt` method accepting a `string` ciphertext parameter and returning a `string` plaintext.
4. THE ISettingEncryptionProvider interface SHALL have XML doc comments on the interface and each method describing the contract and the expectation that the consuming application provides the implementation.

### Requirement 9: SettingDependencyOperator Constants

**User Story:** As a framework consumer, I want well-defined string constants for dependency operators, so that conditional dependency comparisons use consistent values rather than magic strings.

#### Acceptance Criteria

1. THE SettingDependencyOperator class SHALL be a static class residing in `src/GroundUp.Core/Constants/SettingDependencyOperator.cs`.
2. THE SettingDependencyOperator class SHALL define the following `string` constants: `Equals` with value `"Equals"`, `NotEquals` with value `"NotEquals"`, `Contains` with value `"Contains"`, `In` with value `"In"`.
3. THE SettingDependencyOperator class SHALL have XML doc comments on the class and each constant describing its comparison semantics.

### Requirement 10: EF Core Configuration for SettingLevel

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingLevel entity, so that the database schema has proper constraints, indexes, and the self-referencing foreign key is correctly configured.

#### Acceptance Criteria

1. THE SettingLevelConfiguration SHALL implement `IEntityTypeConfiguration<SettingLevel>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingLevelConfiguration.cs`.
2. THE SettingLevelConfiguration SHALL configure `Name` as required with a max length of 100.
3. THE SettingLevelConfiguration SHALL configure `Description` with a max length of 500.
4. THE SettingLevelConfiguration SHALL configure the self-referencing relationship: `ParentId` as an optional foreign key to `SettingLevel.Id`, with `Parent` and `Children` navigation properties, using `DeleteBehavior.Restrict` to prevent cascading deletes of the hierarchy.
5. THE SettingLevelConfiguration SHALL configure `DisplayOrder` with a default value of 0.
6. THE SettingLevelConfiguration SHALL configure the table name as `"SettingLevels"`.

### Requirement 11: EF Core Configuration for SettingGroup

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingGroup entity, so that the database schema enforces the unique key constraint and proper column lengths.

#### Acceptance Criteria

1. THE SettingGroupConfiguration SHALL implement `IEntityTypeConfiguration<SettingGroup>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingGroupConfiguration.cs`.
2. THE SettingGroupConfiguration SHALL configure `Key` as required with a max length of 200 and a unique index.
3. THE SettingGroupConfiguration SHALL configure `DisplayName` as required with a max length of 200.
4. THE SettingGroupConfiguration SHALL configure `Description` with a max length of 1000.
5. THE SettingGroupConfiguration SHALL configure `Icon` with a max length of 100.
6. THE SettingGroupConfiguration SHALL configure `DisplayOrder` with a default value of 0.
7. THE SettingGroupConfiguration SHALL configure the table name as `"SettingGroups"`.

### Requirement 12: EF Core Configuration for SettingDefinition

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingDefinition entity, so that the database schema enforces the unique key constraint, proper column lengths, foreign keys, and default values for all boolean and integer properties.

#### Acceptance Criteria

1. THE SettingDefinitionConfiguration SHALL implement `IEntityTypeConfiguration<SettingDefinition>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingDefinitionConfiguration.cs`.
2. THE SettingDefinitionConfiguration SHALL configure `Key` as required with a max length of 200 and a unique index.
3. THE SettingDefinitionConfiguration SHALL configure `DataType` as a required column with integer conversion for the enum.
4. THE SettingDefinitionConfiguration SHALL configure `DefaultValue` with a max length of 4000.
5. THE SettingDefinitionConfiguration SHALL configure the `GroupId` foreign key relationship to `SettingGroup` with `DeleteBehavior.SetNull`, so that deleting a group does not delete its definitions.
6. THE SettingDefinitionConfiguration SHALL configure UI metadata columns: `DisplayName` (required, max length 200), `Description` (max length 1000), `Category` (max length 200), `DisplayOrder` (default 0), `IsVisible` (default true), `IsReadOnly` (default false).
7. THE SettingDefinitionConfiguration SHALL configure `AllowMultiple` with a default value of false.
8. THE SettingDefinitionConfiguration SHALL configure `IsEncrypted` with a default value of false and `IsSecret` with a default value of false.
9. THE SettingDefinitionConfiguration SHALL configure validation columns: `IsRequired` (default false), `MinValue` (max length 100), `MaxValue` (max length 100), `RegexPattern` (max length 500), `ValidationMessage` (max length 500).
10. THE SettingDefinitionConfiguration SHALL configure dependency columns: `DependsOnKey` (max length 200), `DependsOnOperator` (max length 20), `DependsOnValue` (max length 1000).
11. THE SettingDefinitionConfiguration SHALL configure `CustomValidatorType` with a max length of 500.
12. THE SettingDefinitionConfiguration SHALL configure the table name as `"SettingDefinitions"`.

### Requirement 13: EF Core Configuration for SettingOption

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingOption entity, so that the database schema enforces the foreign key to SettingDefinition and proper column constraints.

#### Acceptance Criteria

1. THE SettingOptionConfiguration SHALL implement `IEntityTypeConfiguration<SettingOption>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingOptionConfiguration.cs`.
2. THE SettingOptionConfiguration SHALL configure the `SettingDefinitionId` foreign key relationship to `SettingDefinition` with `DeleteBehavior.Cascade`, so that deleting a definition removes its options.
3. THE SettingOptionConfiguration SHALL configure `Value` as required with a max length of 1000.
4. THE SettingOptionConfiguration SHALL configure `Label` as required with a max length of 200.
5. THE SettingOptionConfiguration SHALL configure `DisplayOrder` with a default value of 0.
6. THE SettingOptionConfiguration SHALL configure `IsDefault` with a default value of false.
7. THE SettingOptionConfiguration SHALL configure `ParentOptionValue` with a max length of 1000.
8. THE SettingOptionConfiguration SHALL configure the table name as `"SettingOptions"`.

### Requirement 14: EF Core Configuration for SettingValue

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingValue entity, so that the database schema enforces foreign keys, the unique composite constraint, and proper column lengths.

#### Acceptance Criteria

1. THE SettingValueConfiguration SHALL implement `IEntityTypeConfiguration<SettingValue>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingValueConfiguration.cs`.
2. THE SettingValueConfiguration SHALL configure the `SettingDefinitionId` foreign key relationship to `SettingDefinition` with `DeleteBehavior.Cascade`, so that deleting a definition removes its values.
3. THE SettingValueConfiguration SHALL configure the `LevelId` foreign key relationship to `SettingLevel` with `DeleteBehavior.Restrict`, so that a level cannot be deleted while values reference it.
4. THE SettingValueConfiguration SHALL configure `Value` with a max length of 4000.
5. THE SettingValueConfiguration SHALL configure a unique composite index on (`SettingDefinitionId`, `LevelId`, `ScopeId`) to prevent duplicate values for the same setting at the same level and scope.
6. THE SettingValueConfiguration SHALL configure the table name as `"SettingValues"`.

### Requirement 15: EF Core Configuration for SettingDefinitionLevel

**User Story:** As a framework developer, I want a Fluent API configuration for the SettingDefinitionLevel junction entity, so that the database schema enforces the many-to-many relationship with a unique composite constraint.

#### Acceptance Criteria

1. THE SettingDefinitionLevelConfiguration SHALL implement `IEntityTypeConfiguration<SettingDefinitionLevel>` and reside in `src/GroundUp.Data.Postgres/Configurations/Settings/SettingDefinitionLevelConfiguration.cs`.
2. THE SettingDefinitionLevelConfiguration SHALL configure the `SettingDefinitionId` foreign key relationship to `SettingDefinition` with `DeleteBehavior.Cascade`, so that deleting a definition removes its level associations.
3. THE SettingDefinitionLevelConfiguration SHALL configure the `SettingLevelId` foreign key relationship to `SettingLevel` with `DeleteBehavior.Cascade`, so that deleting a level removes its definition associations.
4. THE SettingDefinitionLevelConfiguration SHALL configure a unique composite index on (`SettingDefinitionId`, `SettingLevelId`) to prevent duplicate entries.
5. THE SettingDefinitionLevelConfiguration SHALL configure the table name as `"SettingDefinitionLevels"`.

### Requirement 16: Settings DTOs

**User Story:** As a framework consumer, I want DTO records for all settings entities, so that the service and API layers can transfer settings data without exposing EF Core entity internals.

#### Acceptance Criteria

1. THE SettingLevelDto SHALL be a record class in `src/GroundUp.Core/Dtos/Settings/SettingLevelDto.cs` with properties: `Id` (Guid), `Name` (string), `Description` (string?), `ParentId` (Guid?), `DisplayOrder` (int).
2. THE SettingGroupDto SHALL be a record class in `src/GroundUp.Core/Dtos/Settings/SettingGroupDto.cs` with properties: `Id` (Guid), `Key` (string), `DisplayName` (string), `Description` (string?), `Icon` (string?), `DisplayOrder` (int).
3. THE SettingDefinitionDto SHALL be a record class in `src/GroundUp.Core/Dtos/Settings/SettingDefinitionDto.cs` with properties matching all SettingDefinition entity properties except navigation collections: `Id`, `Key`, `DataType`, `DefaultValue`, `GroupId`, `DisplayName`, `Description`, `Category`, `DisplayOrder`, `IsVisible`, `IsReadOnly`, `AllowMultiple`, `IsEncrypted`, `IsSecret`, `IsRequired`, `MinValue`, `MaxValue`, `MinLength`, `MaxLength`, `RegexPattern`, `ValidationMessage`, `DependsOnKey`, `DependsOnOperator`, `DependsOnValue`, `CustomValidatorType`.
4. THE SettingOptionDto SHALL be a record class in `src/GroundUp.Core/Dtos/Settings/SettingOptionDto.cs` with properties: `Id` (Guid), `SettingDefinitionId` (Guid), `Value` (string), `Label` (string), `DisplayOrder` (int), `IsDefault` (bool), `ParentOptionValue` (string?).
5. THE SettingValueDto SHALL be a record class in `src/GroundUp.Core/Dtos/Settings/SettingValueDto.cs` with properties: `Id` (Guid), `SettingDefinitionId` (Guid), `LevelId` (Guid), `ScopeId` (Guid?), `Value` (string?).
6. ALL setting DTO records SHALL have XML doc comments on the record type and each property.

### Requirement 17: DbContext Registration of Settings Entities

**User Story:** As a framework developer, I want the GroundUpDbContext to automatically discover and apply settings entity configurations, so that consuming applications inherit the settings schema without manual registration.

#### Acceptance Criteria

1. THE GroundUpDbContext SHALL apply all settings entity configurations from the `GroundUp.Data.Postgres` assembly when `OnModelCreating` is called, using `modelBuilder.ApplyConfigurationsFromAssembly` or explicit `ApplyConfiguration` calls for each settings configuration class.
2. WHEN a consuming application inherits from GroundUpDbContext and calls `base.OnModelCreating`, THE settings entity tables (SettingLevels, SettingGroups, SettingDefinitions, SettingOptions, SettingValues, SettingDefinitionLevels) SHALL be included in the EF Core model.
3. THE GroundUpDbContext SHALL NOT require consuming applications to declare `DbSet<T>` properties for settings entities — the configurations alone are sufficient for EF Core to track the entities.

### Requirement 18: Solution Compilation

**User Story:** As a framework developer, I want the entire solution to compile with zero errors after adding all settings entities, enums, interfaces, DTOs, and EF configurations, so that the data layer is ready for the service and resolution logic in Phase 6B.

#### Acceptance Criteria

1. WHEN `dotnet build groundup.sln` is executed, THE build SHALL complete with zero errors.
2. THE settings entities SHALL follow the existing project conventions: file-scoped namespaces, sealed classes, XML doc comments on all public types and members, one class per file.
3. THE settings EF configurations SHALL follow the existing project conventions: Fluent API only (no data annotations), separate configuration class per entity, configurations in a `Settings` subfolder under `Configurations`.
