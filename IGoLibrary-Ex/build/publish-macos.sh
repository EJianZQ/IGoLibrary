#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
RUNTIME="${2:-osx-arm64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/IGoLibrary.Ex.Desktop/IGoLibrary.Ex.Desktop.csproj"
OUTPUT="$ROOT/artifacts/publish/$RUNTIME"

dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$OUTPUT"

echo "Published desktop app to $OUTPUT"
echo "Run ./build/notarize-macos.sh after signing on a macOS release machine."
