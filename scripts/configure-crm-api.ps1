param(
    [string]$DirectoryServer = ".\SQLEXPRESS",
    [string]$ServerA = ".\SQLEXPRESS",
    [string]$ServerB = ".\SQLEXPRESS",
    [string]$ServerC = ".\SQLEXPRESS"
)

$ErrorActionPreference = "Stop"
$routingSetup = Join-Path $PSScriptRoot "configure-login-routing.ps1"
& $routingSetup -DirectoryServer $DirectoryServer -ServerA $ServerA -ServerB $ServerB -ServerC $ServerC
if ($LASTEXITCODE -ne 0) { throw "PawCare CRM API and login routing configuration failed." }
Write-Host "PawCare CRM API databases and central username routing are ready."
