#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
RUNTIME="${2:-osx-arm64}"
APP_NAME="${APP_NAME:-IGoLibrary-Ex}"
BUNDLE_IDENTIFIER="${BUNDLE_IDENTIFIER:-com.igolibrary.ex}"
APP_VERSION="${APP_VERSION:-1.0.0}"
EXECUTABLE_NAME="IGoLibrary.Ex.Desktop"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/IGoLibrary.Ex.Desktop/IGoLibrary.Ex.Desktop.csproj"
PUBLISH_OUTPUT="${PUBLISH_OUTPUT:-$ROOT/artifacts/publish/$RUNTIME}"
APP_OUTPUT_ROOT="$ROOT/artifacts/macos/$RUNTIME"
APP_DIR="$APP_OUTPUT_ROOT/$APP_NAME.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RESOURCES_DIR="$APP_DIR/Contents/Resources"
ZIP_PATH="$APP_OUTPUT_ROOT/$APP_NAME-$RUNTIME.zip"

if [[ "${SKIP_PUBLISH:-0}" != "1" ]]; then
  dotnet publish "$PROJECT" \
    -c "$CONFIGURATION" \
    -r "$RUNTIME" \
    --self-contained true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:UsedAvaloniaProducts= \
    -p:UseSharedCompilation=false \
    -o "$PUBLISH_OUTPUT"
else
  echo "Skipping dotnet publish; packaging existing files from $PUBLISH_OUTPUT"
fi

if [[ ! -f "$PUBLISH_OUTPUT/$EXECUTABLE_NAME" ]]; then
  echo "Published executable was not found: $PUBLISH_OUTPUT/$EXECUTABLE_NAME" >&2
  exit 1
fi

rm -rf "$APP_DIR" "$ZIP_PATH"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp -R "$PUBLISH_OUTPUT/." "$MACOS_DIR/"
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"
if [[ -f "$MACOS_DIR/createdump" ]]; then
  chmod +x "$MACOS_DIR/createdump"
fi
find "$MACOS_DIR" -type f -name "*.dylib" -exec chmod 755 {} \;

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_IDENTIFIER</string>
    <key>CFBundleVersion</key>
    <string>$APP_VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$APP_VERSION</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
  </dict>
</plist>
PLIST

create_zip_with_python() {
  local app_dir="$1"
  local zip_path="$2"
  local app_name="$3"
  local executable_name="$4"

  python3 - "$app_dir" "$zip_path" "$app_name" "$executable_name" <<'PY'
import os
import stat
import sys
import zipfile

app_dir, zip_path, app_name, executable_name = sys.argv[1:5]

def unix_mode(path, arcname):
    if os.path.isdir(path) and not os.path.islink(path):
        return stat.S_IFDIR | 0o755
    leaf = os.path.basename(path)
    if arcname == f"{app_name}/Contents/MacOS/{executable_name}" or leaf == "createdump" or leaf.endswith(".dylib"):
        return stat.S_IFREG | 0o755
    return stat.S_IFREG | 0o644

def add_entry(archive, path, arcname):
    st = os.lstat(path)
    is_dir = os.path.isdir(path) and not os.path.islink(path)
    name = arcname + "/" if is_dir and not arcname.endswith("/") else arcname
    info = zipfile.ZipInfo(name, tuple(__import__("time").localtime(st.st_mtime)[:6]))
    info.create_system = 3
    info.external_attr = unix_mode(path, arcname) << 16
    if os.path.islink(path):
        info.external_attr = (stat.S_IFLNK | 0o777) << 16
        archive.writestr(info, os.readlink(path))
    elif is_dir:
        archive.writestr(info, b"")
    else:
        with open(path, "rb") as source:
            archive.writestr(info, source.read(), compress_type=zipfile.ZIP_DEFLATED)

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    add_entry(archive, app_dir, app_name)
    for root, dirs, files in os.walk(app_dir):
        dirs.sort()
        files.sort()
        for name in dirs:
            path = os.path.join(root, name)
            rel = os.path.relpath(path, os.path.dirname(app_dir)).replace(os.sep, "/")
            add_entry(archive, path, rel)
        for name in files:
            path = os.path.join(root, name)
            rel = os.path.relpath(path, os.path.dirname(app_dir)).replace(os.sep, "/")
            add_entry(archive, path, rel)
PY
}

if command -v ditto >/dev/null 2>&1; then
  (cd "$APP_OUTPUT_ROOT" && /usr/bin/ditto -c -k --keepParent "$APP_NAME.app" "$ZIP_PATH")
elif command -v python3 >/dev/null 2>&1; then
  create_zip_with_python "$APP_DIR" "$ZIP_PATH" "$APP_NAME.app" "$EXECUTABLE_NAME"
else
  echo "Neither ditto nor python3 was found; cannot create a permission-preserving macOS zip." >&2
  exit 1
fi

echo "Published files to $PUBLISH_OUTPUT"
echo "Created macOS app bundle at $APP_DIR"
echo "Created macOS zip at $ZIP_PATH"
echo "Unsigned builds still require users to right-click the app and choose Open the first time."
