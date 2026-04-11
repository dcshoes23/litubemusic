#!/usr/bin/env bash
# setup.sh — initialises the Lidarr git submodule so the plugin can compile.
# Usage: ./setup.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "Initialising Lidarr submodule..."
git -C "$SCRIPT_DIR" submodule update --init --recursive --depth 1 Submodules/Lidarr

COMMON_CSPROJ="$SCRIPT_DIR/Submodules/Lidarr/src/NzbDrone.Common/Lidarr.Common.csproj"
CORE_CSPROJ="$SCRIPT_DIR/Submodules/Lidarr/src/NzbDrone.Core/Lidarr.Core.csproj"

for f in "$COMMON_CSPROJ" "$CORE_CSPROJ"; do
  if [[ -f "$f" ]]; then
    echo "  ✓ $(basename "$f")"
  else
    echo "  ✗ $(basename "$f") not found — something went wrong"
    exit 1
  fi
done

echo ""
echo "Done. Build with:"
echo "  dotnet build -c Debug"
echo "  dotnet build -c Release"
