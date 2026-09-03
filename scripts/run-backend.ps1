param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $Root "DevSignalStudio.sln"
$ApiProject = Join-Path $Root "src/backend/DevSignalStudio.Api/DevSignalStudio.Api.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required, but 'dotnet' was not found on PATH. Install it, reopen PowerShell, and run this script again."
}

$env:DEVSIGNAL_ROOT = $Root

Write-Host "Restoring DevSignal Studio..."
dotnet restore $Solution --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building DevSignal Studio ($Configuration)..."
dotnet build $Solution --configuration $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Starting API at http://localhost:5180"
dotnet run --project $ApiProject --configuration $Configuration --no-build
exit $LASTEXITCODE
