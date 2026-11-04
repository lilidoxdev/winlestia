# Python3.12 / custom_palette.py / Five user colours mapped to shared Winlestia material roles.
from functools import lru_cache
import re

DEFAULT = {
    "background": "#fff8f6",
    "text": "#261916",
    "primary": "#895143",
    "secondary": "#775b52",
    "tertiary": "#68613c",
}


def valid_color(value):
    return isinstance(value, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", value) is not None


def rgb(value):
    return tuple(int(value[i : i + 2], 16) / 255 for i in (1, 3, 5))


def blend(a, b, t):
    return "#" + "".join(f"{round((x * (1 - t) + y * t) * 255):02x}" for x, y in zip(rgb(a), rgb(b)))


def luminance(c):
    values = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in rgb(c)]
    return sum(a * b for a, b in zip(values, (0.2126, 0.7152, 0.0722)))


def on_color(c):
    return "#ffffff" if luminance(c) < 0.179 else "#101518"


def make_palette(colors=None):
    colors = colors or {}
    c = {k: colors.get(k, v).lower() if valid_color(colors.get(k, v)) else v for k, v in DEFAULT.items()}
    return _build(tuple(c[k] for k in DEFAULT))


@lru_cache(maxsize=32)
def _build(values):
    bg, text, primary, secondary, tertiary = values
    dark = luminance(bg) < 0.35
    p = {
        "m3background": bg,
        "m3surface": bg,
        "m3onSurface": text,
        "m3primary": primary,
        "m3secondary": secondary,
        "m3tertiary": tertiary,
        "m3onPrimary": on_color(primary),
        "m3onSecondary": on_color(secondary),
        "m3onTertiary": on_color(tertiary),
        "m3onSurfaceVariant": blend(bg, text, 0.76),
        "m3outline": blend(bg, text, 0.54),
        "m3outlineVariant": blend(bg, text, 0.2),
        "m3error": "#ffb4ab" if dark else "#b3261e",
        "m3onError": "#690005" if dark else "#ffffff",
        "m3shadow": "#000000",
        "m3scrim": "#000000",
        "m3inverseSurface": text,
        "m3inverseOnSurface": bg,
        "m3inversePrimary": blend(primary, text, 0.4),
    }
    for name, t in [("Low", 0.03), ("", 0.06), ("High", 0.10), ("Highest", 0.14)]:
        p["m3surfaceContainer" + name] = blend(bg, text, t)
    for name, accent in [("Primary", primary), ("Secondary", secondary), ("Tertiary", tertiary)]:
        p["m3" + name.lower() + "Container"] = blend(bg, accent, 0.2)
        p["m3on" + name + "Container"] = text
    return {"palette": p, "dark": dark, "sourceColor": primary, "colors": list(values), "source": "Personnalisé"}
