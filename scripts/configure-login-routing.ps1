param(
    [string]$DirectoryServer = ".\SQLEXPRESS",
    [string]$ServerA = ".\SQLEXPRESS",
    [string]$ServerB = ".\SQLEXPRESS",
    [string]$ServerC = ".\SQLEXPRESS"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$setupProject = Join-Path $workspaceRoot "src\PawCare.DatabaseSetup\PawCare.DatabaseSetup.csproj"
$apiProject = Join-Path $workspaceRoot "src\PawCare.Api"
$infrastructureProject = Join-Path $workspaceRoot "src\PawCare.Infrastructure"
$routingSql = Join-Path $PSScriptRoot "configure-login-routing.sql"
$setupOutput = Join-Path $workspaceRoot "artifacts\routing-setup\"

& dotnet ef database update --context MasterErpDbContext --project $infrastructureProject --startup-project $apiProject
if ($LASTEXITCODE -ne 0) { throw "The central directory migration failed." }

foreach ($profile in @(
    @{ Instance=$ServerA; TenantId="tenant-a"; TenantDatabase="PawCareTenantA" },
    @{ Instance=$ServerB; TenantId="tenant-b"; TenantDatabase="PawCareTenantB" },
    @{ Instance=$ServerC; TenantId="tenant-c"; TenantDatabase="PawCareTenantC" }
)) {
    $env:PAWCARE_SQL_SERVER = $profile.Instance
    $env:PAWCARE_DATABASE_PREFIX = "PawCare"
    $env:PAWCARE_TENANT_ID = $profile.TenantId
    & dotnet run --project $setupProject -c Release --no-restore "-p:OutputPath=$setupOutput"
    if ($LASTEXITCODE -ne 0) { throw "Account initialization failed on $($profile.Instance)." }
    $connection = "Server=$($profile.Instance);Database=$($profile.TenantDatabase);Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;"
    & dotnet ef database update --context TenantErpDbContext --project $infrastructureProject --startup-project $apiProject --connection $connection
    if ($LASTEXITCODE -ne 0) { throw "Tenant CRM migration failed for $($profile.TenantDatabase)." }
}
Remove-Item Env:PAWCARE_SQL_SERVER -ErrorAction SilentlyContinue
Remove-Item Env:PAWCARE_DATABASE_PREFIX -ErrorAction SilentlyContinue
Remove-Item Env:PAWCARE_TENANT_ID -ErrorAction SilentlyContinue

& sqlcmd -S $DirectoryServer -E -C -b -i $routingSql -v "ServerA=$ServerA" "ServerB=$ServerB" "ServerC=$ServerC"
if ($LASTEXITCODE -ne 0) { throw "Central login route configuration failed." }
Write-Host "Central username routing is configured."
