# Seeds Keycloak admin settings directly into the database, bypassing the setup wizard's bootstrap step.
# Run this after steps 2 and 3 of the setup wizard have completed.
# Usage: .\scripts\seed-keycloak-settings.ps1

param(
    [string]$AdminClientId = "groundup-admin",
    [string]$AdminClientSecret = "",
    [string]$AppClientId = "groundup-app"
)

if ([string]::IsNullOrEmpty($AdminClientSecret)) {
    $AdminClientSecret = Read-Host "Enter the groundup-admin client secret from Keycloak"
}

Write-Host "Seeding Keycloak settings into database..." -ForegroundColor Cyan

# Get the System level ID and setup group ID
$systemLevelQuery = "SELECT \""Id\"" FROM \""SettingLevels\"" WHERE \""Name\"" = 'System' LIMIT 1;"
$systemLevelId = (docker exec groundup-postgres-1 psql -U groundup -d groundup -t -c $systemLevelQuery).Trim()

$groupQuery = "SELECT \""Id\"" FROM \""SettingGroups\"" WHERE \""Key\"" = 'groundup.setup' LIMIT 1;"
$groupId = (docker exec groundup-postgres-1 psql -U groundup -d groundup -t -c $groupQuery).Trim()

Write-Host "System Level ID: $systemLevelId" -ForegroundColor Gray
Write-Host "Setup Group ID: $groupId" -ForegroundColor Gray

if ([string]::IsNullOrEmpty($systemLevelId) -or [string]::IsNullOrEmpty($groupId)) {
    Write-Host "ERROR: Could not find System level or setup group. Run the setup wizard steps 2-3 first." -ForegroundColor Red
    exit 1
}

# Create definitions and values for the three missing settings
$settings = @(
    @{ Key = "auth.keycloak.admin-client-id"; Value = $AdminClientId; Display = "Admin Client ID" },
    @{ Key = "auth.keycloak.admin-client-secret"; Value = $AdminClientSecret; Display = "Admin Client Secret" },
    @{ Key = "auth.keycloak.app-client-id"; Value = $AppClientId; Display = "App Client ID" }
)

foreach ($setting in $settings) {
    $key = $setting.Key
    $value = $setting.Value
    $display = $setting.Display

    # Insert definition (idempotent)
    $defSql = "INSERT INTO \""SettingDefinitions\"" (\""Id\"", \""Key\"", \""DataType\"", \""DisplayName\"", \""Description\"", \""GroupId\"", \""CreatedAt\"", \""CreatedBy\"", \""DisplayOrder\"") VALUES (gen_random_uuid(), '$key', 0, '$display', '$display', '$groupId', NOW(), '00000000-0000-0000-0000-000000000000', 0) ON CONFLICT (\""Key\"") DO NOTHING;"
    docker exec groundup-postgres-1 psql -U groundup -d groundup -c $defSql 2>&1 | Out-Null

    # Get definition ID
    $defIdQuery = "SELECT \""Id\"" FROM \""SettingDefinitions\"" WHERE \""Key\"" = '$key' LIMIT 1;"
    $defId = (docker exec groundup-postgres-1 psql -U groundup -d groundup -t -c $defIdQuery).Trim()

    # Insert allowed level (idempotent)
    $levelSql = "INSERT INTO \""SettingDefinitionLevels\"" (\""Id\"", \""SettingDefinitionId\"", \""SettingLevelId\"") VALUES (gen_random_uuid(), '$defId', '$systemLevelId') ON CONFLICT DO NOTHING;"
    docker exec groundup-postgres-1 psql -U groundup -d groundup -c $levelSql 2>&1 | Out-Null

    # Insert or update value
    $valueSql = "INSERT INTO \""SettingValues\"" (\""Id\"", \""SettingDefinitionId\"", \""SettingLevelId\"", \""ScopeId\"", \""Value\"", \""CreatedAt\"", \""CreatedBy\"") VALUES (gen_random_uuid(), '$defId', '$systemLevelId', NULL, '$value', NOW(), '00000000-0000-0000-0000-000000000000') ON CONFLICT (\""SettingDefinitionId\"", \""SettingLevelId\"", \""ScopeId\"") DO UPDATE SET \""Value\"" = '$value';"
    docker exec groundup-postgres-1 psql -U groundup -d groundup -c $valueSql 2>&1 | Out-Null

    Write-Host "  $key = $value" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done! Restart the app to pick up the new settings." -ForegroundColor Cyan
Write-Host "After restart, run: .\scripts\test-setup.ps1 (Step 5: Complete Setup should work)" -ForegroundColor Yellow
