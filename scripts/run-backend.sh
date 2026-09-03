#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Debug}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET 10 SDK is required but 'dotnet' was not found on PATH." >&2
  echo "Install the .NET 10 SDK, reopen the terminal, and run this script again." >&2
  exit 127
fi

export DEVSIGNAL_ROOT="$ROOT"

echo "Restoring DevSignal Studio..."
dotnet restore "$ROOT/DevSignalStudio.sln" --nologo

echo "Building DevSignal Studio ($CONFIGURATION)..."
dotnet build "$ROOT/DevSignalStudio.sln" \
  --configuration "$CONFIGURATION" \
  --no-restore \
  --nologo

echo "Starting API at http://localhost:5180"
dotnet run \
  --project "$ROOT/src/backend/DevSignalStudio.Api/DevSignalStudio.Api.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build
