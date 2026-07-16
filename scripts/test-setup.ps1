# Manual testing script for the setup wizard
# Run from: d:\_Rob\github\groundup
# Usage: .\scripts\test-setup.ps1

$baseUrl = "http://localhost:5000"
$token = "my-dev-bootstrap-token-at-least-32-chars-long!!"

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type"  = "application/json"
}

Write-Host "=== Step 1: Check setup status ===" -ForegroundColor Cyan
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/setup" -Method GET
    Write-Host "Setup status:" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Step 2: Set App Identity ===" -ForegroundColor Cyan
$body = @{
    applicationName = "GroundUp Dev"
    defaultDomain   = "localhost"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/setup/app-identity" -Method POST -Headers $headers -Body $body
    Write-Host "Success:" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd() -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Step 3: Set Identity Provider ===" -ForegroundColor Cyan
$body = @{
    publicBaseUrl   = "http://localhost:8080"
    internalBaseUrl = "http://localhost:8080"
    sharedRealmName = "groundup"
    appClientId     = "groundup-app"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/setup/identity-provider" -Method POST -Headers $headers -Body $body
    Write-Host "Success:" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd() -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Step 4: Bootstrap Keycloak Admin ===" -ForegroundColor Cyan

$body = @{
    masterAdminUsername = "admin"
    masterAdminPassword = "admin"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/setup/keycloak-bootstrap" -Method POST -Headers $headers -Body $body
    Write-Host "Success:" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd() -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Step 5: Complete Setup ===" -ForegroundColor Cyan
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/setup/complete" -Method POST -Headers $headers
    Write-Host "Success:" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd() -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Done! Restart the app to verify Keycloak validation passes. ===" -ForegroundColor Cyan
