#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET 10 SDK is required but 'dotnet' was not found on PATH." >&2
  exit 127
fi

export DEVSIGNAL_ROOT="$ROOT"

python3 "$ROOT/scripts/verify-repo.py"
dotnet restore "$ROOT/DevSignalStudio.sln" --nologo
dotnet build "$ROOT/DevSignalStudio.sln" \
  --configuration "$CONFIGURATION" \
  --no-restore \
  --nologo
dotnet run \
  --project "$ROOT/tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build
