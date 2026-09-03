#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Debug}"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK 10 is required but the dotnet command was not found." >&2
  exit 1
fi

VERSION="$(dotnet --version)"
case "$VERSION" in
  10.*) ;;
  *)
    echo "DevSignal Studio requires .NET SDK 10.x. Detected: $VERSION" >&2
    exit 1
    ;;
esac

echo "Using .NET SDK $VERSION"
dotnet restore "$ROOT/DevSignalStudio.sln"
dotnet build "$ROOT/DevSignalStudio.sln" --configuration "$CONFIGURATION" --no-restore
dotnet run --project "$ROOT/tests/backend/DevSignalStudio.Tests/DevSignalStudio.Tests.csproj" --configuration "$CONFIGURATION" --no-build

echo
echo "Backend bootstrap completed."
echo "Start the API with: ./scripts/run-backend.sh"
