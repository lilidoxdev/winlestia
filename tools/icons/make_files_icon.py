"""Genere src/files/Assets/winlestia-files.ico + apercu build/icon-preview/files-icon-preview.png.
Dossier lavande Material 3, liseré indigo, etincelle (>=32 px). Chaque taille est dessinee nativement."""

import io, struct
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ICO = ROOT / "src" / "files" / "Assets" / "winlestia-files.ico"
PREVIEW = ROOT / "build" / "icon-preview" / "files-icon-preview.png"
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

OUTLINE = (52, 42, 112, 255)  # indigo fonce
BACK = (140, 124, 214, 255)  # violet moyen (panneau arriere)
FRONT_TOP = (238, 232, 255, 255)
FRONT_BOT = (203, 192, 250, 255)
STAR = (92, 70, 190, 255)  # accent unique

# x0, x1, tab_top, body_top, front_top, bottom, tab_w, radius, outline  (pixels, entiers)
SMALL = {
    16: (1, 15, 2, 4, 6, 14, 6, 1, 1),
    20: (1, 19, 3, 5, 7, 17, 8, 2, 1),
    24: (1, 23, 3, 6, 8, 20, 10, 2, 1),
}
DESIGN = (16, 240, 36, 62, 92, 218, 96, 24, 10)  # grille 256


def params(s):
    if s in SMALL:
        return [float(v) for v in SMALL[s]]
    k = s / 256
    p = [v * k for v in DESIGN]
    if s <= 48:
        p = [float(round(v)) for v in p]
        p[8] = max(1.0, p[8])
    return p


def star(d, cx, cy, r, col, pinch=0.22):
    pts = []
    import math

    for i in range(8):
        a = math.pi / 4 * i - math.pi / 2
        rr = r if i % 2 == 0 else r * pinch
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(pts, fill=col)


def draw_icon(s):
    S = 8 if s <= 48 else (4 if s <= 128 else 3)
    W = s * S
    x0, x1, ty, by, fy, y1, tw, r, w = [v * S for v in params(s)]
    big = s >= 32

    def shapes(d, inset, col_back, col_front, gradient=False):
        i = inset
        rr = max(0, r - i)
        # panneau arriere + onglet
        d.rounded_rectangle([x0 + i, ty + i, x0 + tw - i, by + r * 2], rr, fill=col_back)
        d.rounded_rectangle([x0 + i, by + i, x1 - i, y1 - i], rr, fill=col_back)
        if col_front:
            d.rounded_rectangle([x0 + i, fy + i, x1 - i, y1 - i], rr, fill=col_front)

    # ombre douce (>=32)
    base = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    if big:
        sh = Image.new("RGBA", (W, W), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sh)
        off = max(1, int(2 * S * s / 128))
        sd.rounded_rectangle([x0, by + off, x1, y1 + off], r, fill=(20, 10, 60, 90))
        base = Image.alpha_composite(base, sh.filter(ImageFilter.GaussianBlur(S * max(0.8, s / 64))))
    # contour
    o = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    od = ImageDraw.Draw(o)
    shapes(od, 0, OUTLINE, OUTLINE)
    base = Image.alpha_composite(base, o)
    # remplissage arriere
    b = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    bd = ImageDraw.Draw(b)
    wi = w
    rr = max(0, r - wi)
    bd.rounded_rectangle([x0 + wi, ty + wi, x0 + tw - wi, by + r * 2], rr, fill=BACK)
    bd.rounded_rectangle([x0 + wi, by + wi, x1 - wi, y1 - wi], rr, fill=BACK)
    base = Image.alpha_composite(base, b)
    # separation : liseré sous le bord superieur du panneau avant
    ol = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    ImageDraw.Draw(ol).rounded_rectangle([x0 + wi, fy, x1 - wi, y1 - wi], rr, fill=OUTLINE)
    base = Image.alpha_composite(base, ol)
    # panneau avant (degrade vertical si grand)
    fm = Image.new("L", (W, W), 0)
    ImageDraw.Draw(fm).rounded_rectangle([x0 + wi, fy + wi, x1 - wi, y1 - wi], max(0, rr - 0), fill=255)
    if big:
        grad = Image.new("RGBA", (W, W))
        gd = ImageDraw.Draw(grad)
        top, bot = int(fy), int(y1)
        for y in range(top, bot + 1):
            t = (y - top) / max(1, bot - top)
            gd.line([(0, y), (W, y)], fill=tuple(round(FRONT_TOP[c] * (1 - t) + FRONT_BOT[c] * t) for c in range(4)))
    else:
        grad = Image.new("RGBA", (W, W), FRONT_TOP if s < 24 else (222, 213, 255, 255))
    base.paste(grad, (0, 0), fm)
    # etincelle Winlestia
    if big:
        d = ImageDraw.Draw(base)
        fh = y1 - fy
        cx, cy = (x0 + x1) / 2, fy + fh * 0.52
        if s >= 64:
            star(d, cx - (x1 - x0) * 0.05, cy, fh * 0.34, STAR)
            star(d, cx + (x1 - x0) * 0.22, cy - fh * 0.2, fh * 0.14, STAR)
        else:
            star(d, cx, cy, fh * 0.32, STAR, 0.2)
    return base.resize((s, s), Image.LANCZOS)


def bmp_entry(im):
    s = im.width
    px = im.convert("RGBA").tobytes("raw", "BGRA", 0, -1)  # bas en haut
    hdr = struct.pack("<IiiHHIIiiII", 40, s, s * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    stride = ((s + 31) // 32) * 4
    return hdr + px + b"\0" * (stride * s)  # masque AND = 0 (alpha gere par 32 bits)


def write_ico(images, path):
    blobs = []
    for im in images:
        if im.width >= 128:
            buf = io.BytesIO()
            im.save(buf, "PNG", optimize=True)
            blobs.append(buf.getvalue())
        else:
            blobs.append(bmp_entry(im))
    off = 6 + 16 * len(images)
    out = struct.pack("<HHH", 0, 1, len(images))
    data = b""
    for im, bl in zip(images, blobs):
        n = im.width
        out += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(bl), off + len(data))
        data += bl
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(out + data)


def preview(images):
    pad = 16
    zoom = [i for i in images if i.width in (16, 24, 32)]
    row1 = sum(i.width + pad for i in images) + pad
    row2 = sum(i.width * 4 + pad for i in zoom) + pad
    W = max(row1, row2)
    H1 = 256 + 2 * pad
    H2 = 128 + 2 * pad
    out = Image.new("RGB", (W * 2 if False else W, 0 + (H1 + H2) * 2))
    y = 0
    for bg in ((243, 243, 243), (32, 32, 32)):
        d = ImageDraw.Draw(out)
        d.rectangle([0, y, W, y + H1 + H2], fill=bg)
        x = pad
        for im in images:
            out.paste(im, (x, y + pad + (256 - im.width)), im)
            x += im.width + pad
        x = pad
        for im in zoom:
            z = im.resize((im.width * 4, im.width * 4), Image.NEAREST)
            out.paste(z, (x, y + H1 + pad), z)
            x += z.width + pad
        y += H1 + H2
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    out.save(PREVIEW)


if __name__ == "__main__":
    imgs = [draw_icon(s) for s in SIZES]
    write_ico(imgs, ICO)
    preview(imgs)
    print("ok", ICO, PREVIEW)
