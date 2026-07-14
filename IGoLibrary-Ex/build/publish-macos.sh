#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
RUNTIME="${2:-osx-arm64}"
APP_NAME="${APP_NAME:-IGoLibrary-Ex}"
BUNDLE_IDENTIFIER="${BUNDLE_IDENTIFIER:-com.igolibrary.ex}"
APP_VERSION="${APP_VERSION:-}"

if [[ -z "${APP_VERSION//[[:space:]]/}" ]]; then
  echo "APP_VERSION is required. Example: APP_VERSION=1.0.1 ./build/publish-macos.sh Release osx-arm64" >&2
  exit 1
fi
if [[ "$APP_VERSION" =~ ^[vV] ]]; then
  echo "APP_VERSION must not include the v prefix; use vN.N.N only for the Git tag / GitHub Release." >&2
  exit 1
fi
if [[ ! $APP_VERSION =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Invalid APP_VERSION: $APP_VERSION. Use a canonical N.N.N stable version without leading zeroes." >&2
  exit 1
fi
case "$RUNTIME" in
  osx-arm64|osx-x64) ;;
  *)
    echo "Unsupported macOS runtime: $RUNTIME" >&2
    exit 1
    ;;
esac

if ! command -v pwsh >/dev/null 2>&1; then
  echo "PowerShell 7 (pwsh) is required to publish both macOS package variants." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
PUBLISH_SCRIPT="$ROOT/build/publish-macos.ps1"
if [[ ! -f "$PUBLISH_SCRIPT" ]]; then
  echo "macOS PowerShell publish script was not found: $PUBLISH_SCRIPT" >&2
  exit 1
fi

PUBLISH_ARGS=(
  -NoLogo
  -NoProfile
  -NonInteractive
  -File "$PUBLISH_SCRIPT"
  -Configuration "$CONFIGURATION"
  -Runtime "$RUNTIME"
  -AppName "$APP_NAME"
  -BundleIdentifier "$BUNDLE_IDENTIFIER"
  -AppVersion "$APP_VERSION"
)

if [[ -n "${PUBLISH_OUTPUT:-}" ]]; then
  PUBLISH_ARGS+=( -PublishOutput "$PUBLISH_OUTPUT" )
fi
if [[ "${SKIP_PUBLISH:-0}" == "1" ]]; then
  PUBLISH_ARGS+=( -SkipPublish )
fi

exec pwsh "${PUBLISH_ARGS[@]}"
