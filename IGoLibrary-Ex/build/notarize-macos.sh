#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  echo "Usage: $0 <app-path> <signing-identity> <apple-id> <team-id> [keychain-profile]"
  exit 1
fi

APP_PATH="$1"
SIGNING_IDENTITY="$2"
APPLE_ID="$3"
TEAM_ID="$4"
KEYCHAIN_PROFILE="${5:-}"
ZIP_PATH="${APP_PATH%.*}.zip"

codesign --deep --force --options runtime --sign "$SIGNING_IDENTITY" "$APP_PATH"
/usr/bin/ditto -c -k --keepParent "$APP_PATH" "$ZIP_PATH"

if [[ -n "$KEYCHAIN_PROFILE" ]]; then
  xcrun notarytool submit "$ZIP_PATH" --keychain-profile "$KEYCHAIN_PROFILE" --wait
else
  xcrun notarytool submit "$ZIP_PATH" --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --wait
fi

xcrun stapler staple "$APP_PATH"
echo "Notarization completed for $APP_PATH"
