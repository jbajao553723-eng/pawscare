param(
    [string]$DirectoryServer = ".\SQLEXPRESS",
    [string]$ServerA = ".\SQLEXPRESS",
    [string]$ServerB = "(localdb)\PawCareServer2",
    [string]$ServerC = "(localdb)\PawCareServer3"
)

$ErrorActionPreference = "Stop"
$routingSetup = Join-Path $PSScriptRoot "configure-login-routing.ps1"
& $routingSetup -DirectoryServer $DirectoryServer -ServerA $ServerA -ServerB $ServerB -ServerC $ServerC
if ($LASTEXITCODE -ne 0) { throw "PawCare CRM API and login routing configuration failed." }
Write-Host "PawCare CRM API databases and central username routing are ready."
