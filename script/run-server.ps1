param(
    [string]$Configuration = "Release",
    [string]$DbProvider = "sqlite",
    [uint16]$TcpPort = 57207,
    [uint16]$UdpPort = 57208,
    [uint16]$WsPort = 57206,
    [uint64]$Seed = 1337,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ServerProject = Join-Path $RepoRoot "server\Carbon.Host\Carbon.Host.csproj"

$env:CARBON_DB_PROVIDER = $DbProvider
$env:CARBON_TCP_PORT = "$TcpPort"
$env:CARBON_UDP_PORT = "$UdpPort"
$env:CARBON_WS_PORT = "$WsPort"
$env:CARBON_WS_HOST = "localhost"
$env:CARBON_WORLD_SEED = "$Seed"

Write-Host "[Carbon Server] DB=$DbProvider | Seed=$Seed | TCP=$TcpPort | UDP=$UdpPort | WS=http://localhost:$WsPort/ws/" -ForegroundColor Cyan

$dotnetArgs = @(
    "run",
    "--project", $ServerProject,
    "-c", $Configuration
)

if ($NoBuild) {
    $dotnetArgs += "--no-build"
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
