#!/usr/bin/env bash
# Builds the self-contained linux-x64 release tarball for the Linux GUI shell.
#
# Environment overrides:
#   CONFIGURATION  build configuration (default: Release)
#   RID            runtime identifier (default: linux-x64)
#   OUTPUT_DIR     artifact directory (default: <repo>/artifacts)
#   VERSION        version string used in the archive name (default: project version)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-linux-x64}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT/artifacts}"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet was not found on PATH" >&2; exit 1; }

VERSION="${VERSION:-$(dotnet msbuild "$ROOT/GUI.Linux/GUI.Linux.csproj" -getProperty:ProjectVersion -nologo -v:q)}"
VERSION="${VERSION:-0.0.0}"

STAGE="$OUTPUT_DIR/Source2Viewer"
TARBALL="$OUTPUT_DIR/source2viewer-linux-x64-$VERSION.tar.gz"

rm -r "$STAGE" 2>/dev/null || true
mkdir -p "$STAGE"

dotnet publish "$ROOT/GUI.Linux/GUI.Linux.csproj" \
    --configuration "$CONFIGURATION" \
    --runtime "$RID" \
    --self-contained true \
    --output "$STAGE" \
    -nologo

cp "$ROOT/Misc/Linux/source2viewer.desktop" "$STAGE/source2viewer.desktop"
cp "$ROOT/Misc/Icons/source2viewer.png" "$STAGE/source2viewer.png"
cp "$ROOT/README.md" "$STAGE/README.md"
cp "$ROOT/LICENSE" "$STAGE/LICENSE"

tar -C "$OUTPUT_DIR" -czf "$TARBALL" "Source2Viewer"

echo "Created $TARBALL"
