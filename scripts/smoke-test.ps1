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

& dotnet run `
    --project "$root/tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj" `
    --configuration $Configuration
