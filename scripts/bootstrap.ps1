[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 10 is required but the dotnet command was not found.'
}

$version = (& dotnet --version).Trim()
if (-not $version.StartsWith('10.')) {
    throw "DevSignal Studio requires .NET SDK 10.x. Detected: $version"
}

Write-Host "Using .NET SDK $version"
& dotnet restore "$root/DevSignalStudio.sln"
& dotnet build "$root/DevSignalStudio.sln" --configuration $Configuration --no-restore
& dotnet run --project "$root/tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj" --configuration $Configuration --no-build

Write-Host ''
Write-Host 'Backend bootstrap completed.'
Write-Host 'Start the API with: ./scripts/run-backend.ps1'
