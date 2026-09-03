$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Verifier = Join-Path $Root "scripts/verify-repo.py"

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
    throw "Python 3 is required for the static repository verifier."
}

& $Python.Source @PythonArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host ".NET SDK detected; running compiler and runtime smoke checks."
    & (Join-Path $PSScriptRoot "test-backend.ps1")
    exit $LASTEXITCODE
}

Write-Warning "Static checks passed. The .NET SDK is not installed, so build and runtime checks were skipped."
Write-Warning "Run scripts/test-backend.ps1 after installing .NET 10."
