param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$DatabasePrefix = "PawCare",
    [string]$ProfileName = "PawCare Server 1",
    [string]$TenantId = "",
    [string]$MirrorDirectory = "",
    [ValidateRange(0, 2147483647)]
    [int]$MaxServerMemoryMb = 0
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "src\PawCare.DatabaseSetup\PawCare.DatabaseSetup.csproj"
$appSettingsPath = Join-Path $workspaceRoot "src\PawCare.Desktop\appsettings.json"
$dataRoot = Join-Path $env:LOCALAPPDATA "PawCare\data"
$cloudRoot = Join-Path $dataRoot "cloud-sync-simulator"
if (-not $MirrorDirectory -and ($ServerInstance -ne ".\SQLEXPRESS" -or $DatabasePrefix -ne "PawCare")) {
    $profileSlug = ($ProfileName.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    $MirrorDirectory = "%LOCALAPPDATA%\PawCare\data\server-mirrors\$profileSlug"
}
if ($MirrorDirectory) { $cloudRoot = [Environment]::ExpandEnvironmentVariables($MirrorDirectory) }

if ($DatabasePrefix -notmatch '^[A-Za-z0-9_]+$') {
    throw "DatabasePrefix may contain only letters, numbers, and underscores."
}
if ([string]::IsNullOrWhiteSpace($ProfileName)) { throw "ProfileName is required." }
if ($TenantId -and $TenantId -notin @('tenant-a','tenant-b','tenant-c')) {
    throw "TenantId must be tenant-a, tenant-b, or tenant-c."
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed. Install SQL Server command-line utilities, then rerun this script."
}

& sqlcmd -S $ServerInstance -E -C -b -l 10 -t 30 -Q "SET NOCOUNT ON; SELECT @@SERVERNAME AS ServerName;"
if ($LASTEXITCODE -ne 0) { throw "Could not connect to SQL Server instance $ServerInstance." }
if ($MaxServerMemoryMb -gt 0) {
    $memoryQuery = "EXEC sp_configure 'show advanced options',1; RECONFIGURE; EXEC sp_configure 'max server memory (MB)',$MaxServerMemoryMb; RECONFIGURE;"
    & sqlcmd -S $ServerInstance -d master -E -C -b -l 10 -t 30 -Q $memoryQuery
    if ($LASTEXITCODE -ne 0) { throw "Could not set the SQL Server memory limit on $ServerInstance." }
}

$profiles = @()
$profileReplaced = $false
if (Test-Path -LiteralPath $appSettingsPath) {
    $existingSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
    if ($existingSettings.Database.Servers) {
        foreach ($existingProfile in $existingSettings.Database.Servers) {
            if ($existingProfile.Name -eq $ProfileName) {
                $resolvedTenantId = if ($TenantId) { $TenantId } else { $existingProfile.TenantId }
                $profiles += @{ Name = $ProfileName; Instance = $ServerInstance; DatabasePrefix = $DatabasePrefix; TenantId = $resolvedTenantId; MirrorDirectory = $MirrorDirectory }
                $profileReplaced = $true
            } else {
                $profiles += @{ Name = $existingProfile.Name; Instance = $existingProfile.Instance; DatabasePrefix = $existingProfile.DatabasePrefix; TenantId = $existingProfile.TenantId; MirrorDirectory = $existingProfile.MirrorDirectory }
            }
        }
    }
}
if (-not $profileReplaced) {
    $newProfile = @{ Name = $ProfileName; Instance = $ServerInstance; DatabasePrefix = $DatabasePrefix; TenantId = $TenantId }
    if ($MirrorDirectory) { $newProfile.MirrorDirectory = $MirrorDirectory }
    $profiles += $newProfile
}
if (@($profiles | Group-Object { $_.Name } | Where-Object Count -gt 1).Count -gt 0) {
    throw "Server profile names must be unique."
}
$databaseLocations = @($profiles | ForEach-Object { "$($_.Instance.ToUpperInvariant())|$($_.DatabasePrefix.ToUpperInvariant())" })
if (@($databaseLocations | Select-Object -Unique).Count -ne $databaseLocations.Count) {
    throw "Each server profile needs a distinct SQL instance and database prefix."
}

New-Item -ItemType Directory -Path $cloudRoot -Force | Out-Null
if ($ServerInstance -eq ".\SQLEXPRESS") {
    $serviceAccount = "NT SERVICE\MSSQL`$SQLEXPRESS"
    & icacls $cloudRoot /grant "${serviceAccount}:(OI)(CI)M" /T /C | Out-Host
}

$env:PAWCARE_SQL_SERVER = $ServerInstance
$env:PAWCARE_DATABASE_PREFIX = $DatabasePrefix
$configuredProfile = $profiles | Where-Object Name -eq $ProfileName | Select-Object -First 1
$env:PAWCARE_TENANT_ID = $configuredProfile.TenantId
if ($MirrorDirectory) { $env:PAWCARE_MIRROR_DIRECTORY = $MirrorDirectory }
& dotnet run --project $projectPath
if ($LASTEXITCODE -ne 0) { throw "PawCare database initialization failed." }

$tenantSuffixes = if ($configuredProfile.TenantId) {
    @(@{ 'tenant-a'='TenantA'; 'tenant-b'='TenantB'; 'tenant-c'='TenantC' }[$configuredProfile.TenantId])
} else {
    @('TenantA','TenantB','TenantC')
}
$expectedNames = @("N'${DatabasePrefix}Master'") + @($tenantSuffixes | ForEach-Object { "N'${DatabasePrefix}$_'" })
$databaseQuery = "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name IN ($($expectedNames -join ',')) ORDER BY name;"
& sqlcmd -S $ServerInstance -E -C -b -l 10 -t 30 -Q $databaseQuery
if ($LASTEXITCODE -ne 0) { throw "Could not verify the PawCare databases on $ServerInstance." }
$defaultProfile = $profiles[0]
$settings = @{
    Database = @{
        Server = $defaultProfile.Instance
        DatabasePrefix = $defaultProfile.DatabasePrefix
        DirectoryDatabase = "MasterCRMJ"
        TrustServerCertificate = $true
        Servers = $profiles
    }
}
$settings | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $appSettingsPath -Encoding utf8
Remove-Item Env:PAWCARE_SQL_SERVER,Env:PAWCARE_DATABASE_PREFIX,Env:PAWCARE_TENANT_ID,Env:PAWCARE_MIRROR_DIRECTORY -ErrorAction SilentlyContinue
Write-Host "PawCare SQL Server configuration completed. Refresh Databases in SSMS."
