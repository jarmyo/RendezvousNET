#!/usr/bin/env python3
"""Draws the Play icon (512x512) and the feature graphic (1024x500) of every store language.

The icon follows res/drawable/appicon_foreground.xml. Needs Pillow; run from anywhere.
"""
import math
import pathlib

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
METADATA = ROOT / "fastlane/metadata/android"
FONT = "/System/Library/Fonts/HelveticaNeue.ttc"
BACKGROUND, ACCENT = (27, 38, 49), (79, 195, 247)
TAGLINES = {
    "en-US": "Your VR videos, folder by folder",
    "es-419": "Tus videos VR, carpeta por carpeta",
    "es-ES": "Tus vídeos VR, carpeta por carpeta",
    "fr-FR": "Vos vidéos VR, dossier par dossier",
    "de-DE": "Deine VR-Videos, Ordner für Ordner",
}


def headset(size):
    """The app's headset on a transparent square, supersampled for smooth edges."""
    s = size * 4 / 108
    img = Image.new("RGBA", (size * 4, size * 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([22 * s, 38 * s, 86 * s, 72 * s], radius=8 * s, fill=ACCENT)
    clear = (0, 0, 0, 0)
    d.polygon([(44 * s, 72 * s), (50 * s, 64 * s), (58 * s, 64 * s), (64 * s, 72 * s)], fill=clear)
    d.ellipse([50 * s, 60 * s, 58 * s, 68 * s], fill=clear)
    for x in (42, 66):
        d.ellipse([(x - 8) * s, 47 * s, (x + 8) * s, 63 * s], fill=BACKGROUND + (255,))
    return img.resize((size, size), Image.LANCZOS)


def icon():
    img = Image.new("RGBA", (512, 512), BACKGROUND + (255,))
    # The adaptive icon's safe zone is the middle 66 of 108 units; Play masks the corners itself.
    mark = headset(620)
    img.alpha_composite(mark, ((512 - 620) // 2, (512 - 620) // 2))
    return img


def feature(tagline):
    w, h = 1024, 500
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        t = y / h
        for x in range(w):
            v = t * 0.8 + x / w * 0.2
            px[x, y] = tuple(int(a + (b - a) * v) for a, b in zip((16, 24, 38), (10, 58, 92)))
    d = ImageDraw.Draw(img, "RGBA")
    # A sphere's grid opening out of the corner: what a 360° video looks like from the inside.
    cx, cy = 790, 250
    for i in range(1, 7):
        rx = 62 * i
        d.ellipse([cx - rx, cy - 300, cx + rx, cy + 300], outline=ACCENT + (48,), width=2)
    for i in range(-3, 4):
        y = cy + i * 62
        half = math.sqrt(max(0, 1 - (i * 62 / 300) ** 2)) * 372
        d.line([cx - half, y, cx + half, y], fill=ACCENT + (48,), width=2)
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([cx - 170, cy - 170, cx + 170, cy + 170], fill=ACCENT + (70,))
    img.paste(glow.filter(ImageFilter.GaussianBlur(70)), (0, 0), glow.filter(ImageFilter.GaussianBlur(70)))
    mark = headset(430)
    img.paste(mark, (cx - 215, cy - 215), mark)
    d = ImageDraw.Draw(img, "RGBA")
    d.text((64, 142), "VrView", font=ImageFont.truetype(FONT, 92, index=1), fill="white")
    size = 38
    font = ImageFont.truetype(FONT, size)
    while d.textlength(tagline, font=font) > 560:
        size -= 1
        font = ImageFont.truetype(FONT, size)
    d.text((66, 256), tagline, font=font, fill=(214, 228, 240))
    d.text((66, 316), "180°  ·  360°  ·  Fisheye  ·  VR180", font=ImageFont.truetype(FONT, 25), fill=ACCENT)
    return img


if __name__ == "__main__":
    for locale, tagline in TAGLINES.items():
        images = METADATA / locale / "images"
        images.mkdir(parents=True, exist_ok=True)
        icon().save(images / "icon.png")
        feature(tagline).save(images / "featureGraphic.png")
