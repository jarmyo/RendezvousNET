#!/usr/bin/env python3
"""Takes the store screenshots from a connected phone, once per language.

Before running: install the build to publish, copy the demo clips to the phone (see store/README.md),
open VrView and choose that folder. It drives the app with adb and its keyboard shortcuts, then
frames each shot with a caption, because Play rejects images more than twice as long as they are
wide and modern phones are wider than that. Needs Pillow and adb.
Usage: capture_screenshots.py [en-US es-419 es-ES fr-FR de-DE]
"""
import pathlib
import re
import subprocess
import sys
import time

from PIL import Image, ImageDraw, ImageFont

PACKAGE = "net.rendezvous.vrview"
ROOT = pathlib.Path(__file__).resolve().parents[2]
FONT = "/System/Library/Fonts/HelveticaNeue.ttc"
APP_LOCALE = {"en-US": "en", "es-419": "es-419", "es-ES": "es-ES", "fr-FR": "fr", "de-DE": "de"}

# (file, key pressed before the shot, caption per language: en, es, fr, de)
SHOTS = [
    ("1", None, ("Any folder becomes a playlist", "Cualquier carpeta es una lista de reproducción",
                 "Chaque dossier devient une playlist", "Jeder Ordner wird zur Playlist")),
    ("2", "KEYCODE_L", ("Your library, inside the headset", "Tu biblioteca, dentro del visor",
                        "Votre bibliothèque, dans le casque", "Deine Mediathek, direkt in der Brille")),
    ("3", "KEYCODE_F", ("Every format, even from unusual cameras", "Todos los formatos, hasta de cámaras raras",
                        "Tous les formats, même des caméras atypiques", "Alle Formate, auch von ungewöhnlichen Kameras")),
    ("4", "KEYCODE_H", ("Level any video with your head", "Nivela cualquier video con la cabeza",
                        "Redressez une vidéo avec la tête", "Richte jedes Video mit dem Kopf aus")),
    ("5", "KEYCODE_I", ("See exactly what is playing", "Mira exactamente qué se reproduce",
                        "Voyez exactement ce qui est lu", "Sieh genau, was gerade läuft")),
]


def adb(*args, binary=False):
    result = subprocess.run(["adb", *args], check=True, capture_output=True)
    return result.stdout if binary else result.stdout.decode("utf8", "replace")


def key(code):
    adb("shell", "input", "keyevent", code)
    time.sleep(2.5)


def tap_first_video():
    adb("shell", "uiautomator", "dump", "/sdcard/vrview_ui.xml")
    xml = adb("shell", "cat", "/sdcard/vrview_ui.xml")
    adb("shell", "rm", "/sdcard/vrview_ui.xml")
    match = re.search(r'class="android.widget.ListView".*?bounds="\[(\d+),(\d+)\]\[(\d+),\d+\]"', xml)
    if match is None:
        sys.exit("No video list on screen: choose the demo folder in VrView first.")
    x0, y0, x1 = map(int, match.groups())
    adb("shell", "input", "tap", str((x0 + x1) // 2), str(y0 + 80))


def framed(shot, caption):
    """Fits a screenshot under a caption on a 16:9 canvas."""
    w, h = (1920, 1080) if shot.width >= shot.height else (1080, 1920)
    band = 150
    scale = min((w - 80) / shot.width, (h - band - 40) / shot.height)
    small = shot.resize((round(shot.width * scale), round(shot.height * scale)), Image.LANCZOS)
    canvas = Image.new("RGB", (w, h), (16, 24, 38))
    canvas.paste(small, ((w - small.width) // 2, band))
    draw = ImageDraw.Draw(canvas)
    size = 58
    font = ImageFont.truetype(FONT, size, index=1)
    while draw.textlength(caption, font=font) > w - 100:
        size -= 2
        font = ImageFont.truetype(FONT, size, index=1)
    draw.text((w / 2, band / 2), caption, font=font, fill="white", anchor="mm")
    return canvas


def capture(locale):
    out = ROOT / "fastlane/metadata/android" / locale / "images/phoneScreenshots"
    out.mkdir(parents=True, exist_ok=True)
    language = ("en", "es", "fr", "de").index(locale[:2])
    adb("shell", "cmd", "locale", "set-app-locales", PACKAGE, "--locales", APP_LOCALE[locale])
    adb("shell", "am", "force-stop", PACKAGE)
    adb("shell", "am", "start", "-n", f"{PACKAGE}/{PACKAGE}.MainActivity")
    time.sleep(4)
    for index, (name, code, captions) in enumerate(SHOTS):
        if index == 1:
            tap_first_video()
            time.sleep(5)
            key("KEYCODE_V")  # The windows are clearest in the single "screen" view.
        if code:
            key(code)
        raw = out / f"{name}.raw.png"
        raw.write_bytes(adb("exec-out", "screencap", "-p", binary=True))
        framed(Image.open(raw).convert("RGB"), captions[language]).save(out / f"{name}.png")
        raw.unlink()
        if code:
            key("KEYCODE_ESCAPE")
    key("KEYCODE_V")
    adb("shell", "am", "force-stop", PACKAGE)
    adb("shell", "cmd", "locale", "set-app-locales", PACKAGE, "--locales", "")


if __name__ == "__main__":
    for locale in sys.argv[1:] or list(APP_LOCALE):
        capture(locale)
