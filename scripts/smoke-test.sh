#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Debug}"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK 10 is required but the dotnet command was not found." >&2
  exit 1
fi

dotnet run \
  --project "$ROOT/tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj" \
  --configuration "$CONFIGURATION"
