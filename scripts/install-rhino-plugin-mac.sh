#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Debug}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/csharp/RhinoRendererPlugin/RhinoRendererPlugin.csproj"
OUTPUT_DIR="$REPO_ROOT/csharp/RhinoRendererPlugin/bin/$CONFIGURATION/net8.0"
DEST_DIR="$HOME/Library/Application Support/McNeel/Rhinoceros/MacPlugIns/RhinoRendererPlugin.rhp"

dotnet build "$PROJECT" -c "$CONFIGURATION"
rm -rf "$DEST_DIR"
mkdir -p "$DEST_DIR"
cp -R "$OUTPUT_DIR"/* "$DEST_DIR"/

echo "Installed to: $DEST_DIR"
echo "Restart Rhino or load: $DEST_DIR/RhinoRendererPlugin.rhp"
