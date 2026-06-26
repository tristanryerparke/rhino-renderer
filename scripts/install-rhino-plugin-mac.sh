#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="Debug"
BUILD=1

usage() {
  cat <<'EOF'
Usage: scripts/install-rhino-plugin-mac.sh [options]

Builds GeometryRendererPlugin and installs the full plugin output folder to Rhino's
macOS MacPlugIns directory as GeometryRendererPlugin.rhp.

Options:
  -c, --configuration <Debug|Release>  Build configuration. Default: Debug
      --no-build                       Copy existing build output without building
  -h, --help                           Show this help

Environment:
  RHINO_MAC_PLUGINS_DIR  Override install directory.
                         Default: ~/Library/Application Support/McNeel/Rhinoceros/MacPlugIns
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration)
      CONFIGURATION="${2:-}"
      if [[ -z "$CONFIGURATION" ]]; then
        echo "Missing value for $1" >&2
        exit 2
      fi
      shift 2
      ;;
    --no-build)
      BUILD=0
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/csharp/GeometryRendererPlugin/GeometryRendererPlugin.csproj"
TARGET_FRAMEWORK="net8.0"
OUTPUT_DIR="$REPO_ROOT/csharp/GeometryRendererPlugin/bin/$CONFIGURATION/$TARGET_FRAMEWORK"
PLUGIN_FILE="$OUTPUT_DIR/GeometryRendererPlugin.rhp"
MAC_PLUGINS_DIR="${RHINO_MAC_PLUGINS_DIR:-$HOME/Library/Application Support/McNeel/Rhinoceros/MacPlugIns}"
INSTALL_DIR="$MAC_PLUGINS_DIR/GeometryRendererPlugin.rhp"

if [[ "$OSTYPE" != darwin* ]]; then
  echo "Warning: this installer targets Rhino for macOS, but OSTYPE=$OSTYPE" >&2
fi

if [[ "$BUILD" -eq 1 ]]; then
  echo "Building $PROJECT_PATH ($CONFIGURATION)..."
  dotnet build "$PROJECT_PATH" -c "$CONFIGURATION"
fi

if [[ ! -f "$PLUGIN_FILE" ]]; then
  echo "Built plugin was not found: $PLUGIN_FILE" >&2
  echo "Try running: dotnet build '$PROJECT_PATH' -c '$CONFIGURATION'" >&2
  exit 1
fi

mkdir -p "$MAC_PLUGINS_DIR"
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

if command -v rsync >/dev/null 2>&1; then
  rsync -a --delete --exclude '.DS_Store' "$OUTPUT_DIR/" "$INSTALL_DIR/"
else
  cp -R "$OUTPUT_DIR/." "$INSTALL_DIR/"
  find "$INSTALL_DIR" -name '.DS_Store' -delete
fi

cat <<EOF
Installed Geometry Renderer plugin:
  $INSTALL_DIR

Plugin file:
  $INSTALL_DIR/GeometryRendererPlugin.rhp

Next:
  1. Restart Rhino if it was already open.
  2. In Rhino, run: GeometryRenderer
  3. Check the web API: http://127.0.0.1:17891/health
EOF
