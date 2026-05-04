# Settings Module — Test Coverage Review

> Generated from the current test suite across Phases 6A, 6B, and 6C.
> **Total: 100+ test methods across 22 test files (19 unit + 3 integration)**

---

## Summary

| Category | Files | Tests | What's Covered |
|----------|-------|-------|----------------|
| **Cascading Resolution** | 2 | 16 | Scope chain walking, fallback to defaults, inheritance tracking, disallowed levels |
| **Value Validation (SetAsync)** | 1 | 14 | Required, min/max value, min/max length, regex, read-only, encryption, upsert |
| **Type Conversion** | 1 | 15 | String, Int, Long, Decimal, Bool, DateTime, Date, JSON, AllowMultiple, null, invalid |
| **Admin CRUD — Levels** | 1 | 10 | Create, update, delete, child-level guard, value-reference guard, not found |
| **Admin CRUD — Groups** | 1 | 8 | Create, update, delete, orphan definitions on delete, not found |
| **Admin CRUD — Definitions** | 1 | 9 | Create with options/levels, update replaces collections, delete, get by ID, not found |
| **Caching** | 2 | 8 | Cache miss → DB, cache hit → cached, invalidation on change, bulk invalidation, exception swallowing |
| **Events** | 1 | 4 | SettingChangedEvent on create/update/delete, event failure doesn't break operation |
| **Scope Chain Provider** | 1 | 3 | Tenant ID set → chain, empty tenant → empty, missing level → empty |
| **DI Registration** | 1 | 2 | ISettingsService registered, works without encryption provider |
| **Structural (Entities)** | 1 | 21 | Sealed, BaseEntity, IAuditable, collection initialization for all 6 entities |
| **Structural (DTOs)** | 1 | 8+ | Record types, expected properties, no navigation/audit fields for all 5 DTOs |
| **Structural (Enums/Constants)** | 2 | 7 | SettingDataType values, SettingDependencyOperator constants |
| **Structural (Interfaces)** | 1 | 3 | ISettingEncryptionProvider method signatures |
| **Integration — Cascading** | 1 | 6 | End-to-end via HTTP: system value, tenant override, cross-tenant isolation, delete revert, default fallback, 404 |
| **Integration — Admin CRUD** | 1 | 10 | End-to-end via HTTP: level/group/definition CRUD, set+resolve, delete removes |
| **Integration — Cache** | 1 | 3 | End-to-end via HTTP: set→read→update→read, delete→read fallback, consistent reads |

---

## Detailed Test Inventory

### 1. SettingsService — GetAsync (Cascading Resolution)

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetAsyncTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAsync_KeyNotFound_ReturnsNotFound` | 404 when setting key doesn't exist |
| `GetAsync_EmptyScopeChain_ReturnsDefaultValue` | Falls back to definition default with empty scope chain |
| `GetAsync_FirstMatchInScopeChainWins` | Tenant value (25) wins over system value (100) when tenant is first in chain |
| `GetAsync_SkipsValuesAtDisallowedLevels` | Values at levels not in AllowedLevels are ignored |
| `GetAsync_FallsBackToDefaultWhenNoMatch` | Returns default (30) when no value exists for the scope |
| `GetAsync_EncryptedSetting_DecryptsOnRead` | Calls ISettingEncryptionProvider.Decrypt on read |
| `GetAsync_EncryptedSettingWithoutProvider_ReturnsFail` | 500 error when encrypted setting accessed without provider |
| `GetAsync_SecretSettingViaGetAsync_ReturnsRealValue` | GetAsync returns real value (not masked) — masking only in GetAllForScopeAsync |
| `GetAsync_NullValue_ReturnsDefault` | Null default value returns null for string type |

### 2. SettingsService — GetAllForScopeAsync (Bulk Resolution)

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetAllForScopeAsyncTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAllForScopeAsync_ResolvesAllDefinitions` | Returns all definitions with effective values |
| `GetAllForScopeAsync_IsInheritedFalse_WhenValueFromFirstScopeEntry` | IsInherited=false when value comes from the most specific scope |
| `GetAllForScopeAsync_IsInheritedTrue_WhenValueFromHigherLevel` | IsInherited=true when value comes from a less specific scope |
| `GetAllForScopeAsync_MasksSecretValues` | Secret values show "••••••••" instead of real value |

### 3. SettingsService — GetGroupAsync (Group Resolution)

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceGetGroupAsyncTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetGroupAsync_GroupNotFound_ReturnsNotFound` | 404 when group key doesn't exist |
| `GetGroupAsync_ResolvesSettingsInGroup` | Returns only settings in the group, not others |
| `GetGroupAsync_OrderedByDisplayOrder` | Results ordered by DisplayOrder |

### 4. SettingsService — SetAsync (Value Writing + Validation)

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceSetAsyncTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `SetAsync_KeyNotFound_ReturnsNotFound` | 404 when setting key doesn't exist |
| `SetAsync_DisallowedLevel_ReturnsBadRequest` | 400 when level not in AllowedLevels |
| `SetAsync_RequiredValueEmpty_ReturnsBadRequest` | 400 when IsRequired=true and value is empty |
| `SetAsync_BelowMinValue_ReturnsBadRequest` | 400 when numeric value below MinValue |
| `SetAsync_AboveMaxValue_ReturnsBadRequest` | 400 when numeric value above MaxValue |
| `SetAsync_BelowMinLength_ReturnsBadRequest` | 400 when string shorter than MinLength |
| `SetAsync_AboveMaxLength_ReturnsBadRequest` | 400 when string longer than MaxLength |
| `SetAsync_RegexMismatch_ReturnsBadRequestWithValidationMessage` | 400 with custom ValidationMessage on regex failure |
| `SetAsync_ReadOnly_ReturnsBadRequest` | 400 when IsReadOnly=true |
| `SetAsync_CreatesNewValue_WhenNoneExists` | Creates new SettingValue entity |
| `SetAsync_UpdatesExistingValue_WhenMatchExists` | Updates existing SettingValue (upsert) |
| `SetAsync_EncryptsValueOnWrite` | Calls ISettingEncryptionProvider.Encrypt before storing |
| `SetAsync_EncryptedWithoutProvider_ReturnsFail` | 500 when encrypted setting written without provider |
| `SetAsync_EmptyValueWithMinLength_WhenNotRequired_Succeeds` | Empty value bypasses MinLength when IsRequired=false |

### 5. SettingsService — DeleteValueAsync

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceDeleteValueAsyncTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `DeleteValueAsync_NotFound_ReturnsNotFound` | 404 when value ID doesn't exist |
| `DeleteValueAsync_Success_RemovesEntityAndReturnsOk` | Removes entity from DB, returns 200 |

### 6. SettingsService — Event Publishing

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceEventTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `SetAsync_PublishesSettingChangedEvent_OnCreate` | Event published with key, levelId, scopeId, null oldValue, newValue |
| `SetAsync_PublishesSettingChangedEvent_OnUpdate_WithOldValue` | Event includes old value on update |
| `DeleteValueAsync_PublishesSettingChangedEvent_WithNullNewValue` | Event has null newValue on delete |
| `SetAsync_EventPublishingFailure_DoesNotFailOperation` | Event bus exception doesn't break SetAsync |

### 7. SettingValueConverter — Type Conversion

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingValueConverterTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `Convert_StringType_ReturnsStringValue` | "hello" → "hello" |
| `Convert_IntType_ReturnsIntValue` | "42" → 42 |
| `Convert_LongType_ReturnsLongValue` | "9999999999" → 9999999999L |
| `Convert_DecimalType_ReturnsDecimalValue` | "3.14" → 3.14m |
| `Convert_BoolType_TrueValue_ReturnsBool` | "True" → true |
| `Convert_BoolType_CaseInsensitive_ReturnsBool` | "true" → true |
| `Convert_DateTimeType_ReturnsDateTime` | ISO string → DateTime |
| `Convert_DateType_ReturnsDateOnly` | "2024-01-15" → DateOnly |
| `Convert_JsonType_ReturnsDeserializedObject` | JSON → deserialized record |
| `Convert_NullValue_ReturnsDefault` | null → 0 (for int) |
| `Convert_NullStringValue_ReturnsDefault` | null → null (for string) |
| `Convert_InvalidInt_ReturnsFail` | "not-a-number" → failure with key in message |
| `Convert_InvalidBool_ReturnsFail` | "maybe" → failure with key in message |
| `Convert_AllowMultiple_ReturnsListOfInt` | "[1,2,3]" → List\<int\> {1,2,3} |
| `Convert_AllowMultiple_ReturnsListOfString` | '["a","b"]' → List\<string\> {"a","b"} |

### 8. DefaultScopeChainProvider

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/DefaultScopeChainProviderTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetScopeChainAsync_TenantIdSetAndTenantLevelExists_ReturnsSingleEntryScopeChain` | Returns [(tenantLevelId, tenantId)] |
| `GetScopeChainAsync_TenantIdIsEmpty_ReturnsEmptyList` | Guid.Empty → empty chain |
| `GetScopeChainAsync_TenantLevelNotFound_ReturnsEmptyList` | No "Tenant" level → empty chain |

### 9. SettingsCacheInvalidationHandler

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCacheInvalidationHandlerTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `HandleAsync_EventReceived_ClearsEntriesForChangedKey` | Removes `settings:get:MaxUploadSizeMB:*` entries, keeps other keys |
| `HandleAsync_EventReceived_ClearsBulkCacheEntries` | Removes `settings:all:*` and `settings:group:*` entries |
| `HandleAsync_ExceptionDuringRemoval_DoesNotPropagate` | Disposed cache doesn't throw |

### 10. SettingsService — Cache Behavior

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsCacheTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAsync_CacheMiss_ResolvesFromDbAndStoresInCache` | First call resolves from DB, cache key tracked |
| `GetAsync_CacheHit_ReturnsCachedValueWithoutDbQuery` | Second call returns cached value even after DB change |
| `SetAsync_DoesNotCache` | Write operations don't populate cache |
| `DeleteValueAsync_DoesNotCache` | Delete operations don't populate cache |
| `GetAsync_CacheExceptionOnRead_FallsThroughToDb` | Throwing cache → falls through to DB |

### 11. SettingsAdminService — Level CRUD

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceLevelTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAllLevelsAsync_ReturnsAllLevels` | Returns all levels ordered by DisplayOrder |
| `GetAllLevelsAsync_EmptyDatabase_ReturnsEmptyList` | Empty DB → empty list |
| `CreateLevelAsync_CreatesAndReturnsDto` | Creates level, returns DTO, persists to DB |
| `CreateLevelAsync_WithParent_SetsParentId` | Parent-child relationship set correctly |
| `UpdateLevelAsync_ExistingLevel_UpdatesAndReturnsDto` | Updates name, description, display order |
| `UpdateLevelAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |
| `DeleteLevelAsync_WithChildLevels_ReturnsBadRequest` | 400 "has child levels" |
| `DeleteLevelAsync_WithReferencingValues_ReturnsBadRequest` | 400 "referenced by setting values" |
| `DeleteLevelAsync_NoReferences_Succeeds` | Removes level from DB |
| `DeleteLevelAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |

### 12. SettingsAdminService — Group CRUD

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceGroupTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAllGroupsAsync_ReturnsAllGroups` | Returns all groups |
| `GetAllGroupsAsync_EmptyDatabase_ReturnsEmptyList` | Empty DB → empty list |
| `CreateGroupAsync_CreatesAndReturnsDto` | Creates group, returns DTO, persists to DB |
| `UpdateGroupAsync_ExistingGroup_UpdatesAndReturnsDto` | Updates key, display name, description, icon |
| `UpdateGroupAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |
| `DeleteGroupAsync_OrphansDefinitions` | Sets GroupId=null on definitions, removes group |
| `DeleteGroupAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |
| `DeleteGroupAsync_NoDefinitions_Succeeds` | Removes empty group |

### 13. SettingsAdminService — Definition CRUD

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsAdminServiceDefinitionTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `GetAllDefinitionsAsync_IncludesOptions` | Returns definitions with their options |
| `GetDefinitionByIdAsync_ExistingId_ReturnsDto` | Returns definition by ID |
| `GetDefinitionByIdAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |
| `CreateDefinitionAsync_PersistsOptionsAndAllowedLevels` | Creates definition + 3 options + 2 allowed levels in one save |
| `CreateDefinitionAsync_NoOptionsOrLevels_Succeeds` | Creates definition without options/levels |
| `UpdateDefinitionAsync_ReplacesOptionsAndAllowedLevels` | Full replace: old options removed, new ones added |
| `UpdateDefinitionAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |
| `DeleteDefinitionAsync_RemovesDefinition` | Removes definition from DB |
| `DeleteDefinitionAsync_InvalidId_ReturnsNotFound` | 404 for non-existent ID |

### 14. DI Registration

**File:** `tests/GroundUp.Tests.Unit/Services/Settings/SettingsServiceCollectionExtensionsTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `AddGroundUpSettings_RegistersISettingsServiceAsScoped` | ISettingsService → SettingsService, scoped |
| `AddGroundUpSettings_WorksWithoutEncryptionProvider` | No ISettingEncryptionProvider registered, no error |

### 15. Entity Structure (Reflection)

**File:** `tests/GroundUp.Tests.Unit/Core/Settings/SettingEntityStructureTests.cs`

| Test | What It Verifies |
|------|-----------------|
| 6 entities × (IsSealed + ExtendsBaseEntity + IAuditable check) | All 6 entities: sealed, extend BaseEntity, correct IAuditable |
| Collection initialization tests | Children, Settings, Options, Values, AllowedLevels initialized |

### 16. DTO Structure (Reflection)

**File:** `tests/GroundUp.Tests.Unit/Core/Settings/SettingDtoStructureTests.cs`

| Test | What It Verifies |
|------|-----------------|
| 5 DTOs × IsRecordType | All DTOs are C# records |
| Property checks per DTO | Correct property names and types |
| 5 DTOs × NoNavigationProperties | No ICollection\<T\> properties |
| 5 DTOs × NoAuditFields | No CreatedAt/CreatedBy/UpdatedAt/UpdatedBy |

### 17. Enum/Constant Structure

**Files:** `SettingDataTypeTests.cs`, `SettingDependencyOperatorTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `SettingDataType_HasExactly8Members` | 8 enum values |
| 8 × `SettingDataType_Member_HasCorrectIntegerValue` | String=0 through Json=7 |
| `SettingDependencyOperator_IsStaticClass` | Static sealed class |
| 4 × operator constant values | Equals, NotEquals, Contains, In |

### 18. ISettingEncryptionProvider Structure

**File:** `tests/GroundUp.Tests.Unit/Core/Settings/ISettingEncryptionProviderTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `ISettingEncryptionProvider_IsInterface` | Is an interface |
| `Encrypt_AcceptsStringParameter_ReturnsString` | Encrypt(string) → string |
| `Decrypt_AcceptsStringParameter_ReturnsString` | Decrypt(string) → string |

---

## Integration Tests (End-to-End via HTTP)

### 19. Cascading Resolution (via API)

**File:** `tests/GroundUp.Tests.Integration/Settings/SettingsCascadingResolutionTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `Resolve_SystemValueSet_ReturnsSystemValue` | GET /api/settings/{key} returns system-level value |
| `Resolve_TenantOverrideExists_ReturnsTenantValue` | Tenant override wins over system value |
| `Resolve_DifferentTenantWithoutOverride_ReturnsSystemValue` | Tenant B (no override) gets system value |
| `Resolve_DeleteTenantOverride_RevertsToSystemValue` | DELETE override → GET returns system value |
| `Resolve_NoValueSet_ReturnsDefaultValue` | No values set → returns definition default |
| `Resolve_NonExistentKey_ReturnsNotFound` | Non-existent key → 404 |

### 20. Admin CRUD (via API)

**File:** `tests/GroundUp.Tests.Integration/Settings/SettingsAdminCrudTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `CreateLevel_ReturnsCreatedLevel` | POST /api/settings/levels → 200 with level data |
| `UpdateLevel_PersistsChanges` | PUT /api/settings/levels/{id} → updated data |
| `DeleteLevel_WithNoReferences_Succeeds` | DELETE /api/settings/levels/{id} → 200 |
| `GetAllLevels_ReturnsLevels` | GET /api/settings/levels → non-empty list |
| `CreateGroup_ReturnsCreatedGroup` | POST /api/settings/groups → 200 with group data |
| `DeleteGroup_Succeeds` | DELETE /api/settings/groups/{id} → 200 |
| `CreateDefinition_WithOptionsAndAllowedLevels_ReturnsFullDefinition` | POST /api/settings/definitions with options → full response |
| `SetValue_ThenResolve_ReturnsSetValue` | PUT then GET → returns set value |
| `DeleteDefinition_RemovesDefinition` | DELETE definition → GET returns 404 |
| `GetAllDefinitions_ReturnsDefinitions` | GET /api/settings/definitions → contains created definition |

### 21. Cache Invalidation (via API)

**File:** `tests/GroundUp.Tests.Integration/Settings/SettingsCacheInvalidationTests.cs`

| Test | What It Verifies |
|------|-----------------|
| `SetReadUpdateRead_ReturnsUpdatedValue` | Set → Read → Update → Read returns updated value |
| `DeleteValue_ThenRead_FallsBackToDefault` | Set → Read → Delete → Read returns default |
| `MultipleReads_AfterSet_ReturnConsistentValue` | 3 consecutive reads return same value |
