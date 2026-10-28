# macOS 应用图标

## 现状

`IGoLibrary-Ex` 的 macOS 包在 1.0.4 之前没有图标：仓库只有 Windows 用的 `.ico`，`.app` 包内既没有 `.icns`，`Info.plist` 也没有 `CFBundleIconFile`，因此 Finder 和 Dock 只能显示系统通用图标。

Windows 侧不受影响：`IGoLibrary.Ex.Desktop` / `Launcher` / `Updater` / 旧版 WinForms 都在 csproj 里声明了 `ApplicationIcon`，图标已经嵌进 exe 的 PE 资源。

## 产物

| 文件 | 用途 |
|---|---|
| `IGoLibrary-Ex/src/IGoLibrary.Ex.Desktop/Assets/AppIcon.icns` | macOS 图标，含 16–1024 全尺寸 |
| `docs/images/ex/软件图标-1024.png` | 1024×1024 源图 |
| `IGoLibrary-Ex/build/publish-macos.ps1` | 构建时把 icns 拷进 `Contents/Resources/`，并在 `Info.plist` 写入 `CFBundleIconFile` |

## 设计说明

图形沿用作者的原始设计：两根 `#1296DB` 竖条 + 一根倾斜 19°、30% 透明度的同色条。原始素材最大只有 64×64（`Assets/main.ico`）和 200×200（`docs/images/ex/软件图标-大.png`），直接放大到 1024 会模糊，因此按原图比例重绘：

- 画布 1024×1024，白色圆角底板 824×824、圆角半径 22.5%（macOS 图标网格）
- 内容区 618×618 居中，竖条宽 98.9、高 618、圆角为半宽
- 第三根条旋转 −19°，透明度 76/255

## 重新生成

源图改动后，重新生成 `.icns`：

```bash
# 1. 生成 1024 源图（依赖 Pillow，用 uv 隔离）
uv run --with pillow python generate.py   # 见下方脚本

# 2. 拆分尺寸
mkdir -p AppIcon.iconset
for s in 16 32 128 256 512; do
  sips -z $s $s AppIcon-1024.png --out AppIcon.iconset/icon_${s}x${s}.png
  sips -z $((s*2)) $((s*2)) AppIcon-1024.png --out AppIcon.iconset/icon_${s}x${s}@2x.png
done

# 3. 打包
iconutil -c icns AppIcon.iconset -o AppIcon.icns
```

生成源图的脚本：

```python
from PIL import Image, ImageDraw

SS = 4
S = 1024 * SS
BLUE = (18, 150, 219, 255)
TILE = (255, 255, 255, 255)

def sc(v): return v * SS

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
tile_x0, tile_y0, tile_w = 100, 100, 824
d.rounded_rectangle([sc(tile_x0), sc(tile_y0), sc(tile_x0 + tile_w), sc(tile_y0 + tile_w)],
                    radius=sc(tile_w * 0.225), fill=TILE)

content = 618.0
scale = content / 150.0
ox = 512 - content / 2.0
bar_w = 24.0 * scale
bar_h = 150.0 * scale

def capsule(cx, cy, w, h, angle=0.0, fill=BLUE):
    layer = Image.new("RGBA", (int(w) + 8 * SS, int(h) + 8 * SS), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.rounded_rectangle([4 * SS, 4 * SS, 4 * SS + w, 4 * SS + h], radius=w / 2.0, fill=fill)
    if angle:
        layer = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)))

for cx in (45.5, 87.0):
    capsule(sc(ox + (cx - 33) * scale), sc(512), sc(bar_w), sc(bar_h))
capsule(sc(ox + (148.04 - 33) * scale), sc(512), sc(bar_w), sc(bar_h), angle=-19.0,
        fill=(18, 150, 219, 76))

img.resize((1024, 1024), Image.LANCZOS).save("AppIcon-1024.png")
```

## 验证

```bash
plutil -extract CFBundleIconFile raw /Applications/IGoLibrary-Ex.app/Contents/Info.plist
ls -l /Applications/IGoLibrary-Ex.app/Contents/Resources/AppIcon.icns
sips -g pixelWidth -g pixelHeight /Applications/IGoLibrary-Ex.app/Contents/Resources/AppIcon.icns
```
