param(
    [string]$Configuration = "Release",
    [string]$ServerHost = "127.0.0.1",
    [uint16]$TcpPort = 57207,
    [string]$Username = "",
    [string]$Password = "",
    [string]$Character = "",
    [switch]$AutoLogin,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ClientProject = Join-Path $RepoRoot "desktop\Carbon.Desktop\Carbon.Desktop.csproj"

$env:CARBON_SERVER_HOST = $ServerHost
$env:CARBON_TCP_PORT = "$TcpPort"

if (-not [string]::IsNullOrWhiteSpace($Username)) {
    $env:CARBON_USERNAME = $Username
}

if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $env:CARBON_PASSWORD = $Password
}

if (-not [string]::IsNullOrWhiteSpace($Character)) {
    $env:CARBON_CHARACTER = $Character
}

if ($AutoLogin) {
    $env:CARBON_AUTO_LOGIN = "1"
}
else {
    Remove-Item Env:CARBON_AUTO_LOGIN -ErrorAction SilentlyContinue
}

Write-Host "[Carbon Client] Server=$ServerHost`:$TcpPort (TCP + Authenticated UDP) | AutoLogin=$AutoLogin" -ForegroundColor Green

$dotnetArgs = @(
    "run",
    "--project", $ClientProject,
    "-c", $Configuration
)

if ($NoBuild) {
    $dotnetArgs += "--no-build"
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
