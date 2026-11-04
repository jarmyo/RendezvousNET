#!/usr/bin/env python3
"""Makes original demo clips for the store screenshots, so no real video appears in them.

Draws layered landscapes (shifted between eyes for depth) and encodes 20 s clips with ffmpeg.
Usage: make_demo_videos.py OUTPUT_FOLDER. Needs Pillow and ffmpeg.
"""
import pathlib
import random
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFilter

CLIPS = {
    "Mountain_lake_180_LR.mp4": ((40, 92, 150), (248, 190, 130), (38, 66, 84), True, 1),
    "Desert_sunset_180_LR.mp4": ((70, 40, 96), (250, 140, 80), (120, 62, 44), True, 2),
    "Northern_forest_360.mp4": ((16, 40, 70), (120, 210, 170), (14, 48, 44), False, 3),
    "Ocean_morning_360.mp4": ((90, 160, 215), (250, 225, 190), (24, 84, 120), False, 4),
}
EYE_W, H = 1920, 1920


def scene(sky, glow, land, seed, shift, width):
    rng = random.Random(seed)
    img = Image.new("RGB", (width, H))
    d = ImageDraw.Draw(img)
    for y in range(H // 2 + 80):
        t = y / (H // 2 + 80)
        d.line([(0, y), (width, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(sky, glow)))
    d.ellipse([width * 0.5 - 90, H * 0.36, width * 0.5 + 90, H * 0.36 + 180], fill=(255, 240, 215))
    for layer in range(5):
        depth = (layer + 1) / 5
        base = H * (0.46 + 0.075 * layer)
        points, x = [(-200, H)], -200
        while x <= width + 200:
            y = base + rng.uniform(-70, 70) * (1.2 - depth)
            points.append((x + shift * depth, y))
            x += rng.randint(60, 180)
        points.append((width + 200, H))
        d.polygon(points, fill=tuple(int(c * (0.45 + 0.65 * (1 - depth)) + g * 0.25 * (1 - depth)) for c, g in zip(land, glow)))
    return img.filter(ImageFilter.GaussianBlur(1.2))


def main(out):
    out.mkdir(parents=True, exist_ok=True)
    for name, (sky, glow, land, stereo, seed) in CLIPS.items():
        if stereo:
            frame = Image.new("RGB", (EYE_W * 2, H))
            frame.paste(scene(sky, glow, land, seed, 0, EYE_W), (0, 0))
            frame.paste(scene(sky, glow, land, seed, -26, EYE_W), (EYE_W, 0))
        else:
            frame = scene(sky, glow, land, seed, 0, EYE_W * 2)
        still = out / (name + ".png")
        frame.save(still)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-loop", "1", "-i", str(still), "-t", "20", "-r", "30",
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "24", "-preset", "veryfast", str(out / name)], check=True)
        still.unlink()


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]))
