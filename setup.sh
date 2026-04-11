#!/usr/bin/env bash
# setup.sh — copies Lidarr reference DLLs to _lidarr_ref/ so the plugin can compile.
# Usage: ./setup.sh [/path/to/Lidarr/installation]
#   Default Lidarr path: /opt/Lidarr

set -euo pipefail

LIDARR_PATH="${1:-/opt/Lidarr}"
REF_DIR="$(dirname "$0")/_lidarr_ref"

if [[ ! -d "$LIDARR_PATH" ]]; then
  echo "ERROR: Lidarr not found at '$LIDARR_PATH'."
  echo "Install Lidarr or pass the correct path: ./setup.sh /path/to/Lidarr"
  exit 1
fi

mkdir -p "$REF_DIR"

ASSEMBLIES=(
  "NzbDrone.Core.dll"
  "NzbDrone.Common.dll"
  "NLog.dll"
  "FluentValidation.dll"
  "Newtonsoft.Json.dll"
  "AutoMapper.dll"
)

echo "Copying reference assemblies from '$LIDARR_PATH' → '$REF_DIR'"
for asm in "${ASSEMBLIES[@]}"; do
  src="$LIDARR_PATH/$asm"
  if [[ -f "$src" ]]; then
    cp "$src" "$REF_DIR/"
    echo "  ✓ $asm"
  else
    echo "  ✗ $asm (not found — build may fail)"
  fi
done

echo ""
echo "Done. Build with:"
echo "  dotnet build -c Debug  -p:LidarrPath=$LIDARR_PATH"
echo "  dotnet build -c Release -p:LidarrPath=$LIDARR_PATH"
