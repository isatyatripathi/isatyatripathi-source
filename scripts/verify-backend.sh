#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$ROOT/scripts/verify-repo.py"

if command -v dotnet >/dev/null 2>&1; then
  echo
  echo ".NET SDK detected; running compiler and runtime smoke checks."
  "$ROOT/scripts/test-backend.sh"
else
  echo
  echo "Static checks passed. The .NET SDK is not installed, so build and runtime checks were skipped." >&2
  echo "Run scripts/test-backend.sh after installing .NET 10." >&2
fi
