"""Genere les preview.png des paquets Winjets fournis et de l'exemple communautaire (Pillow).

Les apercus sont des images statiques en gris neutres : le vrai rendu suit la palette de l'utilisateur.
Usage : python tools/make_winjet_previews.py
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
BUILTIN = ROOT / 'src' / 'winjets' / 'builtin'
EXAMPLE = ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation'

BG = (38, 40, 44, 255)
EDGE = (92, 96, 104, 255)
TEXT = (236, 238, 241, 255)
DIM = (150, 154, 162, 255)
ACCENT = (190, 194, 202, 255)
SCALE = 2  # 168 px logiques -> 336 px


def font(size, bold=False):
    for name in ('segoeuib.ttf' if bold else 'segoeui.ttf', 'arialbd.ttf' if bold else 'arial.ttf', 'DejaVuSans.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def card(w, h):
    img = Image.new('RGBA', (w * SCALE, h * SCALE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, w * SCALE - 1, h * SCALE - 1), radius=20 * SCALE, fill=BG, outline=EDGE, width=2)
    return img, d


def sparkline(d, box, values, width=4):
    x0, y0, x1, y1 = box
    lo, hi = min(values), max(values)
    pts = [(x0 + (x1 - x0) * i / (len(values) - 1), y1 - (y1 - y0) * (v - lo) / (hi - lo)) for i, v in enumerate(values)]
    d.line(pts, fill=ACCENT, width=width, joint='curve')


def clock():
    img, d = card(168, 168)
    cx = cy = 84 * SCALE
    r = 62 * SCALE
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=EDGE, width=3)
    for k in range(12):
        a = k * math.pi / 6
        d.line((cx + math.sin(a) * (r - 10), cy - math.cos(a) * (r - 10), cx + math.sin(a) * (r - 2), cy - math.cos(a) * (r - 2)),
               fill=DIM, width=3)
    for angle, length, width, color in ((math.radians(305), 0.5, 8, TEXT), (math.radians(60), 0.75, 5, TEXT)):
        d.line((cx, cy, cx + math.sin(angle) * r * length, cy - math.cos(angle) * r * length), fill=color, width=width)
    d.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), fill=ACCENT)
    return img


def calendar():
    img, d = card(168, 168)
    d.text((24 * SCALE, 20 * SCALE), 'LUNDI', font=font(26), fill=DIM)
    d.text((24 * SCALE, 40 * SCALE), '9', font=font(130, True), fill=TEXT)
    d.text((24 * SCALE, 130 * SCALE), 'novembre', font=font(28), fill=ACCENT)
    return img


def btc():
    img, d = card(352, 168)
    d.text((24 * SCALE, 20 * SCALE), 'Bitcoin', font=font(26), fill=DIM)
    d.text((24 * SCALE, 44 * SCALE), '71 380 €', font=font(54, True), fill=TEXT)
    d.text((24 * SCALE, 112 * SCALE), '+0,34 %', font=font(30), fill=ACCENT)
    values = [3, 4, 3.6, 5, 6.2, 5.8, 7, 8.4, 8, 9.5]
    sparkline(d, (190 * SCALE, 36 * SCALE, 330 * SCALE, 140 * SCALE), values)
    return img


def citation():
    img, d = card(352, 168)
    d.text((24 * SCALE, 18 * SCALE), 'CITATION DU JOUR', font=font(22), fill=DIM)
    for i in range(4):
        for j in range(4):
            x, y = (290 + i * 11) * SCALE, (18 + j * 11) * SCALE
            d.ellipse((x, y, x + 6 * SCALE, y + 6 * SCALE), fill=ACCENT if (i + j) % 2 == 0 else EDGE)
    d.text((24 * SCALE, 56 * SCALE), 'Je pense, donc je suis.', font=font(40, True), fill=TEXT)
    d.text((24 * SCALE, 100 * SCALE), 'René Descartes', font=font(26), fill=ACCENT)
    d.rounded_rectangle((24 * SCALE, 130 * SCALE, 100 * SCALE, 154 * SCALE), radius=24, fill=ACCENT)
    d.rounded_rectangle((108 * SCALE, 130 * SCALE, 176 * SCALE, 154 * SCALE), radius=24, outline=EDGE, width=2)
    d.text((40 * SCALE, 133 * SCALE), 'Citation', font=font(22), fill=BG)
    d.text((127 * SCALE, 133 * SCALE), 'Auteur', font=font(22), fill=TEXT)
    return img


def main():
    jobs = {
        BUILTIN / 'winlestia.horloge': clock,
        BUILTIN / 'winlestia.calendrier': calendar,
        BUILTIN / 'winlestia.graphique-btc': btc,
        EXAMPLE: citation,
    }
    for folder, make in jobs.items():
        out = folder / 'preview.png'
        make().save(out, optimize=True)
        print(out.relative_to(ROOT), out.stat().st_size, 'octets')


if __name__ == '__main__':
    main()
