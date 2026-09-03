param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $Root "DevSignalStudio.sln"
$TestProject = Join-Path $Root "tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj"
$Verifier = Join-Path $Root "scripts/verify-repo.py"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required, but 'dotnet' was not found on PATH."
}

$Python = Get-Command python -ErrorAction SilentlyContinue
$PythonArguments = @($Verifier)
if (-not $Python) {
    $Python = Get-Command python3 -ErrorAction SilentlyContinue
}
if (-not $Python) {
    $Python = Get-Command py -ErrorAction SilentlyContinue
    $PythonArguments = @('-3', $Verifier)
}
if (-not $Python) {
    throw "Python 3 is required for the dependency-free static repository verifier."
}

$env:DEVSIGNAL_ROOT = $Root

& $Python.Source @PythonArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet restore $Solution --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet build $Solution --configuration $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet run --project $TestProject --configuration $Configuration --no-build
exit $LASTEXITCODE
