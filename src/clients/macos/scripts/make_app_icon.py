#!/usr/bin/env python3
"""Builds the macOS app icon from docs/branding/app-icon-source.png.

Makes the rounded-square shape Apple uses for Mac icons (824×824 body on a
1024×1024 canvas with a transparent margin) and writes every size Xcode needs
into HoloRepository2027/Assets.xcassets/AppIcon.appiconset.

Usage:  python3 scripts/make_app_icon.py     (needs Pillow: pip install pillow)
For the sharpest result, use a source image of at least 1024×1024.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs" / "branding" / "app-icon-source.png"
ICONSET = ROOT / "HoloRepository2027" / "Assets.xcassets" / "AppIcon.appiconset"

CANVAS, BODY, RADIUS = 1024, 824, 185  # Apple's macOS icon grid


def master() -> Image.Image:
    art = Image.open(SOURCE).convert("RGBA").resize((BODY, BODY), Image.LANCZOS)
    mask = Image.new("L", (BODY, BODY), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, BODY - 1, BODY - 1), radius=RADIUS, fill=255)
    art.putalpha(mask)

    icon = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    offset = (CANVAS - BODY) // 2
    # Soft drop shadow, like other Mac app icons.
    shadow = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 90), (offset, offset + 12), mask)
    icon = Image.alpha_composite(icon, shadow.filter(ImageFilter.GaussianBlur(14)))
    icon.alpha_composite(art, (offset, offset))
    return icon


def main() -> None:
    big = master()
    images = []
    for points in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            pixels = points * scale
            name = f"icon_{points}x{points}{'@2x' if scale == 2 else ''}.png"
            big.resize((pixels, pixels), Image.LANCZOS).save(ICONSET / name)
            images.append({"filename": name, "idiom": "mac", "scale": f"{scale}x", "size": f"{points}x{points}"})
    contents = {"images": images, "info": {"author": "xcode", "version": 1}}
    (ICONSET / "Contents.json").write_text(json.dumps(contents, indent=2) + "\n")
    print(f"Wrote {len(images)} icon sizes to {ICONSET.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
