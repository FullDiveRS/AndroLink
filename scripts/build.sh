#!/usr/bin/env bash
# AndroLink dev build + test (Linux/macOS). Compile-only for Windows targets;
# the GUI runs on Windows. For the full product build use scripts/build.ps1.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== AndroLink build (dev) =="
dotnet restore AndroLink.sln
dotnet build AndroLink.sln -c Release
dotnet test src/AndroLink.Tests/AndroLink.Tests.csproj -c Release --no-build --nologo
echo "Done."
