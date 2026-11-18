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


def title(d, text):
    d.text((18 * SCALE, 14 * SCALE), text, font=font(22), fill=DIM)


def bar(d, x, y, w, ratio):
    d.rounded_rectangle((x * SCALE, y * SCALE, (x + w) * SCALE, (y + 5) * SCALE), radius=5, fill=EDGE)
    d.rounded_rectangle((x * SCALE, y * SCALE, (x + w * ratio) * SCALE, (y + 5) * SCALE), radius=5, fill=ACCENT)


def lines(d, rows, y0=44, step=34, x=18):
    for i, (txt, col) in enumerate(rows):
        d.text((x * SCALE, (y0 + i * step) * SCALE), txt, font=font(28), fill=col)


def player():
    img, d = card(352, 168)
    d.rounded_rectangle((18 * SCALE, 18 * SCALE, 96 * SCALE, 96 * SCALE), radius=16, fill=EDGE)
    d.text((112 * SCALE, 26 * SCALE), 'Titre du morceau', font=font(34, True), fill=TEXT)
    d.text((112 * SCALE, 56 * SCALE), 'Artiste', font=font(26), fill=DIM)
    bar(d, 18, 118, 316, 0.4)
    d.ellipse((190 * SCALE, 134 * SCALE, 206 * SCALE, 150 * SCALE), fill=ACCENT)
    return img


def system():
    img, d = card(352, 352)
    title(d, 'Processeur')
    d.text((18 * SCALE, 40 * SCALE), '27 %', font=font(80, True), fill=TEXT)
    sparkline(d, (18 * SCALE, 170 * SCALE, 334 * SCALE, 260 * SCALE), [3, 5, 4, 7, 6, 9, 5, 6, 8, 4])
    d.text((18 * SCALE, 290 * SCALE), 'RAM', font=font(24), fill=DIM)
    bar(d, 18, 322, 316, 0.58)
    return img


def markets():
    img, d = card(352, 352)
    title(d, 'Marches')
    for i, (n, p, c) in enumerate((('AAPL', '231,40', '+1,2 %'), ('MSFT', '418,10', '-0,4 %'), ('GSPC', '5 820', '+0,7 %'))):
        y = (52 + i * 96) * SCALE
        d.text((18 * SCALE, y), n, font=font(32, True), fill=TEXT)
        d.text((230 * SCALE, y), p, font=font(32, True), fill=TEXT)
        d.rounded_rectangle((246 * SCALE, y + 44 * SCALE, 334 * SCALE, y + 70 * SCALE), radius=14, outline=ACCENT, width=2)
        d.text((258 * SCALE, y + 44 * SCALE), c, font=font(22), fill=ACCENT)
    return img


def storage():
    img, d = card(352, 168)
    title(d, 'Stockage')
    cx, cy, r = 88 * SCALE, 98 * SCALE, 48 * SCALE
    d.arc((cx - r, cy - r, cx + r, cy + r), 0, 360, fill=EDGE, width=10)
    d.arc((cx - r, cy - r, cx + r, cy + r), -90, 140, fill=ACCENT, width=10)
    d.text((cx - 28 * SCALE, cy - 16 * SCALE), '64 %', font=font(34, True), fill=TEXT)
    lines(d, [('C: Windows', TEXT), ('412 sur 953 Go', DIM), ('541 Go libres', ACCENT)], 50, 32, 170)
    return img


def files():
    img, d = card(352, 352)
    title(d, 'Dossiers')
    lines(d, [('Documents', TEXT), ('Telechargements', TEXT), ('Images', TEXT), ('Bureau', TEXT)], 48, 40)
    d.line((18 * SCALE, 218 * SCALE, 334 * SCALE, 218 * SCALE), fill=EDGE, width=2)
    d.text((18 * SCALE, 228 * SCALE), 'Recents', font=font(22), fill=DIM)
    lines(d, [('rapport.docx', TEXT), ('budget.xlsx', TEXT)], 262, 36)
    return img


def notes():
    img, d = card(352, 352)
    title(d, 'Notes')
    lines(d, [('Courses du week-end', TEXT), ('- pain, lait, cafe', DIM), ('- appeler Marie', DIM)], 52, 38)
    return img


def agenda():
    img, d = card(352, 352)
    d.text((18 * SCALE, 14 * SCALE), 'Lundi', font=font(24, True), fill=ACCENT)
    d.text((18 * SCALE, 40 * SCALE), '9 novembre 2026', font=font(34, True), fill=TEXT)
    for i, (a, b) in enumerate((('Reunion equipe', "Aujourd'hui, 09:30"), ('Dentiste', 'Mardi, 14:00'),
        ('Anniversaire', 'Jeudi, journee'))):
        y = (100 + i * 64) * SCALE
        d.text((18 * SCALE, y), a, font=font(28), fill=TEXT)
        d.text((18 * SCALE, y + 30 * SCALE), b, font=font(22), fill=DIM)
    return img


def weather():
    img, d = card(352, 168)
    title(d, 'Paris')
    d.ellipse((22 * SCALE, 56 * SCALE, 66 * SCALE, 100 * SCALE), outline=ACCENT, width=6)
    d.text((80 * SCALE, 44 * SCALE), '18°', font=font(70, True), fill=TEXT)
    d.text((18 * SCALE, 116 * SCALE), 'Ensoleille', font=font(28), fill=TEXT)
    d.text((190 * SCALE, 60 * SCALE), 'Ressenti 17°', font=font(24), fill=DIM)
    d.text((190 * SCALE, 92 * SCALE), 'Max 21°  Min 11°', font=font(24), fill=DIM)
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
        BUILTIN / 'winlestia.lecteur': player,
        BUILTIN / 'winlestia.systeme': system,
        BUILTIN / 'winlestia.marches': markets,
        BUILTIN / 'winlestia.stockage': storage,
        BUILTIN / 'winlestia.fichiers': files,
        BUILTIN / 'winlestia.notes': notes,
        BUILTIN / 'winlestia.agenda': agenda,
        BUILTIN / 'winlestia.meteo': weather,
        EXAMPLE: citation,
    }
    for folder, make in jobs.items():
        out = folder / 'preview.png'
        make().save(out, optimize=True)
        print(out.relative_to(ROOT), out.stat().st_size, 'octets')


if __name__ == '__main__':
    main()
