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
ICON_SOURCE="${ICON_SOURCE:-$ROOT/../docs/images/ex/软件图标-大.png}"
ICON_FILE_NAME="AppIcon.icns"
PUBLISH_OUTPUT="${PUBLISH_OUTPUT:-$ROOT/artifacts/publish/$RUNTIME}"
APP_OUTPUT_ROOT="$ROOT/artifacts/macos/$RUNTIME"
APP_DIR="$APP_OUTPUT_ROOT/$APP_NAME.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RESOURCES_DIR="$APP_DIR/Contents/Resources"
case "$RUNTIME" in
  osx-arm64)
    PACKAGE_NAME="${PACKAGE_NAME:-$APP_NAME-macOS-Apple-Silicon-arm64.zip}"
    ;;
  osx-x64)
    PACKAGE_NAME="${PACKAGE_NAME:-$APP_NAME-macOS-Intel-x64.zip}"
    ;;
  *)
    PACKAGE_NAME="${PACKAGE_NAME:-$APP_NAME-$RUNTIME.zip}"
    ;;
esac
ZIP_PATH="$APP_OUTPUT_ROOT/$PACKAGE_NAME"
FIRST_RUN_GUIDE_PATH="$APP_OUTPUT_ROOT/macOS首次运行说明.txt"
FIRST_RUN_COMMAND_PATH="$APP_OUTPUT_ROOT/首次运行.command"

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

rm -rf "$APP_DIR" "$ZIP_PATH" "$FIRST_RUN_GUIDE_PATH" "$FIRST_RUN_COMMAND_PATH"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp -R "$PUBLISH_OUTPUT/." "$MACOS_DIR/"
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"
if [[ -f "$MACOS_DIR/createdump" ]]; then
  chmod +x "$MACOS_DIR/createdump"
fi
find "$MACOS_DIR" -type f -name "*.dylib" -exec chmod 755 {} \;

copy_app_icon() {
  local icon_source="$1"
  local resources_dir="$2"
  local icon_output="$resources_dir/$ICON_FILE_NAME"

  if [[ ! -f "$icon_source" ]]; then
    echo "App icon source was not found: $icon_source" >&2
    return
  fi

  if ! command -v sips >/dev/null 2>&1 || ! command -v iconutil >/dev/null 2>&1; then
    echo "sips/iconutil was not found; skipping macOS app icon generation." >&2
    return
  fi

  local iconset="$APP_OUTPUT_ROOT/AppIcon.iconset"
  rm -rf "$iconset"
  mkdir -p "$iconset"

  local sizes=(16 32 128 256 512)
  local size
  for size in "${sizes[@]}"; do
    /usr/bin/sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    /usr/bin/sips -z "$((size * 2))" "$((size * 2))" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done

  if ! /usr/bin/iconutil -c icns "$iconset" -o "$icon_output" 2>/dev/null; then
    echo "iconutil could not build the app icon; using Python fallback." >&2
    create_icns_with_python "$iconset" "$icon_output"
  fi
  rm -rf "$iconset"
}

create_icns_with_python() {
  local iconset="$1"
  local icon_output="$2"

  python3 - "$iconset" "$icon_output" <<'PY'
import os
import struct
import sys

iconset, icon_output = sys.argv[1:3]
entries = [
    ("icp4", "icon_16x16.png"),
    ("icp5", "icon_32x32.png"),
    ("icp6", "icon_32x32@2x.png"),
    ("ic07", "icon_128x128.png"),
    ("ic08", "icon_256x256.png"),
    ("ic09", "icon_512x512.png"),
    ("ic10", "icon_512x512@2x.png"),
]

chunks = []
for icon_type, filename in entries:
    path = os.path.join(iconset, filename)
    with open(path, "rb") as source:
        data = source.read()
    chunks.append(icon_type.encode("ascii") + struct.pack(">I", len(data) + 8) + data)

payload = b"".join(chunks)
with open(icon_output, "wb") as target:
    target.write(b"icns" + struct.pack(">I", len(payload) + 8) + payload)
PY
}

copy_app_icon "$ICON_SOURCE" "$RESOURCES_DIR"

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
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
  </dict>
</plist>
PLIST

cat > "$FIRST_RUN_GUIDE_PATH" <<GUIDE
IGoLibrary-Ex macOS 首次运行说明

此版本未签名、未公证。macOS 首次运行时可能提示“已损坏，无法打开”。
这通常是 Gatekeeper 对下载文件添加了隔离标记，不代表压缩包真的损坏。

推荐操作：
1. 解压 zip，确认本文件和 $APP_NAME.app 在同一个目录。
2. 打开“终端”。
3. 输入下面这行命令，注意末尾保留一个空格：

   xattr -dr com.apple.quarantine

   然后按一次空格

4. 把 $APP_NAME.app 从 Finder 拖到终端窗口里，终端会自动补全路径。
5. 按回车执行。
6. 再双击或右键打开 $APP_NAME.app。

如果你愿意，也可以尝试双击同目录下的“首次运行.command”，它会自动执行解除隔离并打开应用。
GUIDE

cat > "$FIRST_RUN_COMMAND_PATH" <<'COMMAND'
#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_PATH="$SCRIPT_DIR/IGoLibrary-Ex.app"

if [[ ! -d "$APP_PATH" ]]; then
  echo "未找到 IGoLibrary-Ex.app，请确认首次运行.command 与 App 在同一目录。"
  read -r -p "按回车退出..."
  exit 1
fi

xattr -dr com.apple.quarantine "$APP_PATH" || true
open "$APP_PATH"
COMMAND
chmod +x "$FIRST_RUN_COMMAND_PATH"

create_zip_with_python() {
  local app_dir="$1"
  local zip_path="$2"
  local app_name="$3"
  local executable_name="$4"
  local first_run_guide="$5"
  local first_run_command="$6"

  python3 - "$app_dir" "$zip_path" "$app_name" "$executable_name" "$first_run_guide" "$first_run_command" <<'PY'
import os
import stat
import sys
import zipfile

app_dir, zip_path, app_name, executable_name, first_run_guide, first_run_command = sys.argv[1:7]

def unix_mode(path, arcname):
    if os.path.isdir(path) and not os.path.islink(path):
        return stat.S_IFDIR | 0o755
    leaf = os.path.basename(path)
    if arcname == f"{app_name}/Contents/MacOS/{executable_name}" or leaf == "createdump" or leaf.endswith(".dylib") or leaf.endswith(".command"):
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
    for path in (first_run_guide, first_run_command):
        add_entry(archive, path, os.path.basename(path))
PY
}

if command -v python3 >/dev/null 2>&1; then
  create_zip_with_python "$APP_DIR" "$ZIP_PATH" "$APP_NAME.app" "$EXECUTABLE_NAME" "$FIRST_RUN_GUIDE_PATH" "$FIRST_RUN_COMMAND_PATH"
elif command -v ditto >/dev/null 2>&1; then
  (cd "$APP_OUTPUT_ROOT" && /usr/bin/ditto -c -k --norsrc "$APP_NAME.app" "macOS首次运行说明.txt" "首次运行.command" "$ZIP_PATH")
else
  echo "Neither ditto nor python3 was found; cannot create a permission-preserving macOS zip." >&2
  exit 1
fi

echo "Published files to $PUBLISH_OUTPUT"
echo "Created macOS app bundle at $APP_DIR"
echo "Created macOS zip at $ZIP_PATH"
echo "Unsigned builds may require users to remove quarantine on first run. See macOS first-run instructions inside the zip."
