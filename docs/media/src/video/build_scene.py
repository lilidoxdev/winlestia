"""Assemble video/scene.html from the phase-2 sources (tokens, components, dashboard, fragments).
Run:  python build_scene.py      (output: scene.html next to this file)
The palette variants (warm, teal) are scoped copies of tokens-warm.css / tokens-teal.css so that the
app can switch ambiance inside the same scene."""
import html as _html
import os
import re
from pathlib import Path

# Public repository URL shown in the closing pill (single source of truth).
REPO_URL = os.environ.get("WINLESTIA_REPO_URL", "github.com/<owner>/winlestia")

HERE = Path(__file__).resolve().parent
SRC = HERE.parent
rd = lambda p: (SRC / p).read_text(encoding="utf-8")


def style(html):
    return "\n".join(re.findall(r"<style>(.*?)</style>", html, re.S))


def body(html):
    html = re.sub(r"<style>.*?</style>", "", html, flags=re.S)
    return html.strip()


def amb(name, cls):
    css = re.sub(r"/\*.*?\*/", "", rd(name), flags=re.S).replace(":root", "." + cls)
    # derived roles are computed from --primary, so they must be redefined in the scope
    derived = ("--primary-container: color-mix(in srgb,var(--primary) 24%,var(--surface-container-high));"
               "--secondary-container: color-mix(in srgb,var(--primary) 22%,var(--surface-container-high));")
    return css.replace("{", "{" + derived, 1)


dash = rd("dashboard.html")
dash_css = re.search(r"<style>(.*?)</style>", dash, re.S).group(1)
dash_css = re.sub(r"html,body\{[^}]*\}\n", "", dash_css)
dash_css = re.sub(r"\.stage\{[^}]*\}\n", "", dash_css)
dash_main = re.search(r'<main class="panel">.*?</main>', dash, re.S).group(0)

comp = rd("components.css")
comp = re.sub(r"html,body\{[^}]*\}\n", "", comp)
comp = re.sub(r"\.stage\{[^}]*\}\n", "", comp)

icons = rd("fragments/_icons.html").strip()
dash_defs = re.search(r'<svg width="0".*?</defs></svg>', dash, re.S).group(0)

media = rd("fragments/media.html")
perf = rd("fragments/performance.html").replace('id="pw"', 'id="pfw"').replace("getElementById('pw')", "getElementById('pfw')")
esp = rd("fragments/espaces.html")
outils = rd("fragments/outils.html")
dock = rd("fragments/dock.html")
pal = re.search(r'<section class="panel pal".*?</section>', dock, re.S).group(0)
pal = pal.replace(' style="transform:translateX(-50%)"', "").replace("<span>24 × 7</span>", '<span id="qtxt"></span>')
dockcss = style(dock)
dock_nav = re.search(r'<nav class="dock">.*?</nav>', dock, re.S).group(0)

BANNER = r"""<b class="ac"> _       ___       __          __  _</b>
<b class="ac">| |     / (_)___  / /__  _____/ /_(_)___ _</b>
<b class="ac">| | /| / / / __ \/ / _ \/ ___/ __/ / __ `/</b>
<b class="ac">| |/ |/ / / / / / /  __(__  ) /_/ / /_/ /</b>
<b class="ac">|__/|__/_/_/ /_/_/\___/____/\__/_/\__,_/</b>"""
CHEV = ('<svg class="ch" viewBox="0 0 7 11"><path d="M1.2 1.2 5.6 5.5 1.2 9.8" fill="none" stroke="currentColor" '
        'stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg> ')
PATH = r"C:\Users\Alex\projects\demo"


def prompt(i, typed=True):
    return (f'<div class="pr" id="pr{i}"><div class="ln"><span class="c1"></span><span class="ac">{PATH}</span>'
            f'<span class="mu">  ·  CMD  ·  10:09</span></div><div class="ln"><span class="c2"></span>{CHEV}'
            f'<span class="fg" id="cmd{i}"></span><span class="cur" id="cur{i}"></span></div></div>')


def rows(pairs):
    return "\n".join(f'<span class="fg">{a.ljust(10)}</span>  <span class="cy">{b}</span>' for a, b in pairs)


TERM = f"""<section class="term" id="term">
  <div class="tb"><svg class="i ic"><use href="#i-terminal"/></svg><span>Winlestia - CMD</span><span class="g"></span>
    <span class="w"><svg viewBox="0 0 12 12"><path d="M1 6h10" stroke="currentColor" fill="none"/></svg></span>
    <span class="w"><svg viewBox="0 0 12 12"><rect x="1.5" y="1.5" width="9" height="9" stroke="currentColor" fill="none"/></svg></span>
    <span class="w"><svg viewBox="0 0 12 12"><path d="M1 1l10 10M11 1L1 11" stroke="currentColor" fill="none"/></svg></span></div>
<pre class="t"><div class="ban">{BANNER}</div><div class="ln">&nbsp;</div><div class="ln"><span class="mu">WINDOWS / WINLESTIA  </span><span class="cy">·  espace de commande</span></div><div class="bx">{rows([("système", "Windows 11 Home · 26200"), ("session", "Alex · Standard"), ("machine", "ALEX-PC"), ("shell", "CMD · Clink 1.9"), ("mémoire", "7,8 / 16 Go"), ("activité", "2 h 14 min")])}</div>{prompt(1)}<div class="out" id="out1">Sur la branche main
Votre branche est à jour avec 'origin/main'.

Modifications qui ne seront pas validées :
        <span class="rd">modifié :         src/app.js</span>
Fichiers non suivis :
        <span class="rd">notes.txt</span></div>{prompt(2)}<div class="out" id="out2">Clink v1.9.0</div>{prompt(3)}</pre>
</section>"""

TERM_CSS = """
.term{position:absolute;left:40px;top:20px;width:1200px;height:760px;border-radius:14px;overflow:hidden;--lh:21px;
  border:1px solid var(--outline-variant);background:color-mix(in srgb,var(--background) 68%,transparent);
  backdrop-filter:blur(var(--glass-panel-blur)) saturate(1.15)}
.tb{height:38px;display:flex;align-items:center;padding:0 0 0 16px;gap:10px;font-size:14px;color:var(--on-surface-variant)}
.tb .ic{width:18px;height:18px;color:var(--primary)}
.tb .g{flex:1}
.tb .w{width:50px;height:38px;display:flex;align-items:center;justify-content:center;color:var(--on-surface)}
.tb .w svg{width:13px;height:13px}
pre.t{font-family:Consolas,"Lucida Console",monospace;font-size:17px;line-height:var(--lh);padding:4px 26px 0;color:var(--on-surface);white-space:pre}
pre.t .ln{display:block;height:var(--lh)}
pre.t .ac,pre.t b.ac{color:var(--primary);font-weight:400}
pre.t .cy{color:color-mix(in srgb,var(--primary) 62%,#fff)}
pre.t .mu{color:var(--outline)}
pre.t .fg{color:var(--on-surface)}
pre.t .rd{color:#ff7b8a}
.c1,.c2{display:inline-block;position:relative;width:2ch;height:var(--lh);vertical-align:top}
.c1:before,.c2:before{content:"";position:absolute;left:4px;right:1px;border:1px solid var(--outline)}
.c1:before{top:8px;bottom:-1px;border-right:0;border-bottom:0;border-top-left-radius:7px}
.c2:before{top:-1px;bottom:9px;border-right:0;border-top:0;border-bottom-left-radius:7px}
svg.ch{display:inline-block;width:9px;height:14px;vertical-align:-2px;margin:0 .5ch 0 .4ch;color:var(--primary)}
.bx{margin:5px 0 7px 2ch;width:max-content;padding:7px 22px 7px 16px;border:1px solid var(--outline);border-radius:12px}
.cur{display:inline-block;width:10px;height:17px;background:var(--primary);vertical-align:-3px}
.pr{margin-top:5px}
"""

GROUPS = [  # id, words (max 4 per plan), css class
    ("t1", ["Winlestia"]), ("t1b", ["Windows,", "beautifully", "reimagined"]),
    ("t2", ["Quick", "command,", "instantly"]), ("t3", ["Live", "music,", "live", "sensors"]),
    ("t4", ["Dock,", "spaces,", "flow"]), ("t5", ["CMD,", "under", "Clink"]),
    ("fn", ["Winlestia"]), ("fp", ["Windows,", "beautifully", "reimagined"]),
]
txt = "\n".join(f'  <div class="txt" id="{i}">' + "".join(f'<span class="w">{w}</span>' for w in ws) + "</div>" for i, ws in GROUPS)
txt += ('\n  <div class="txt" id="fl"><span class="w"><svg width="120" height="120" viewBox="0 0 120 120">'
        '<defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#e9b6ff"/></linearGradient></defs>'
        '<g fill="url(#lg)"><rect x="8" y="8" width="49" height="49" rx="10"/><rect x="63" y="8" width="49" height="49" rx="10"/>'
        '<rect x="8" y="63" width="49" height="49" rx="10"/><rect x="63" y="63" width="49" height="49" rx="10"/></g></svg></span></div>')
txt += '\n  <div class="txt" id="fu"><span class="w pill">' + _html.escape(REPO_URL) + '</span></div>'

PANES = [("pw", dash_main), ("palw", pal), ("pm", body(media)), ("pp", body(perf)), ("ps", body(esp)),
         ("pt", TERM), ("po", body(outils))]
panes = "\n".join(f'    <div class="pn" id="{i}">{h}</div>' for i, h in PANES)

html = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>Winlestia - promo video scene</title>
<style>
@font-face{{font-family:"Rubik";src:url("../fonts/Rubik.ttf");font-weight:300 900}}
{rd("tokens.css")}
</style>
<style>
/* ---- shared Winlestia UI (components.css, dashboard.html, fragments; palette variants scoped below) */
*{{box-sizing:border-box;margin:0}}
{comp}
{dash_css}
{dockcss}
{style(media)}
{style(perf)}
{style(esp)}
{style(outils)}
{TERM_CSS}
{amb("tokens-warm.css", "amb-warm")}
{amb("tokens-teal.css", "amb-teal")}
.amb-warm,.amb-teal{{color:var(--on-surface)}}
/* ---- video scene */
html,body{{margin:0;width:100vw;height:100vh;overflow:hidden;background:#0a0613;font-family:var(--font-sans);color:var(--on-surface)}}
#cam{{position:absolute;left:0;top:0;width:100%;height:100%;transform-origin:50% 50%;will-change:transform}}
#bg{{position:absolute;inset:-12%}}
#bg i,#stg i.wp{{position:absolute;inset:0;background-size:cover;background-position:center}}
#bg i{{filter:saturate(1.15)}}
.wA{{background-image:url(../wallpaper-aurora.svg)}}.wW{{background-image:url(../wallpaper-warm.svg)}}.wT{{background-image:url(../wallpaper-teal.svg)}}
#scrim{{position:absolute;left:0;right:0;top:0;height:46%;background:linear-gradient(#0a061366,transparent)}}
#scr{{position:absolute;border-radius:26px;overflow:hidden;background:#0a0613;
  box-shadow:0 70px 150px rgba(8,3,20,.55),0 0 0 2px rgba(255,255,255,.10),0 0 0 11px #0c0816}}
#stg{{position:absolute;left:0;top:0;width:1280px;height:800px;transform-origin:0 0;overflow:hidden}}
.pn{{position:absolute;inset:0;transform-origin:50% 50%;visibility:hidden}}
/* nested backdrop-filters smear in the animated 3D scene: use denser flat glass instead */
.pn *{{backdrop-filter:none!important}}
.pn .panel,.pn .pal,.pn .term{{background:color-mix(in srgb,var(--background) 80%,transparent)!important}}
#dockw{{position:absolute;inset:0;transform-origin:50% 100%}}
#glare{{position:absolute;inset:0;background:linear-gradient(115deg,rgba(255,255,255,.10),transparent 38%);pointer-events:none}}
#cur0{{position:absolute;left:0;top:0;width:34px;height:34px;pointer-events:none;filter:drop-shadow(0 2px 3px rgba(0,0,0,.45));z-index:9}}
.rip{{position:absolute;left:0;top:0;width:60px;height:60px;margin:-30px 0 0 -30px;border-radius:50%;border:3px solid #fff;pointer-events:none;opacity:0;z-index:8}}
.txt{{position:absolute;left:0;right:0;text-align:center;font-weight:700;letter-spacing:-.01em;color:#fff;
  text-shadow:0 4px 30px rgba(20,4,40,.55),0 1px 2px rgba(20,4,40,.5);white-space:nowrap;pointer-events:none}}
.txt .w{{display:inline-block;margin:0 .14em;transform-origin:50% 60%;opacity:0;will-change:transform,filter}}
#t1 .w,#fn .w{{background:linear-gradient(180deg,#fff,#f0c8ff);-webkit-background-clip:text;background-clip:text;color:transparent;text-shadow:none;filter:drop-shadow(0 6px 24px rgba(30,5,60,.55))}}
#fu .pill{{font-weight:500;letter-spacing:0;padding:.28em .8em;border-radius:999px;background:rgba(14,6,30,.45);border:2px solid rgba(255,255,255,.35);margin:0}}
</style></head>
<body>
{dash_defs}
{icons}
<svg width="0" height="0" style="position:absolute"><defs>
 <filter id="mb" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur id="mbs" stdDeviation="0 0"/></filter>
</defs></svg>
<div id="cam">
  <div id="bg"><i class="wA"></i><i class="wW"></i><i class="wT"></i></div><div id="scrim"></div>
  <div id="mbw" style="position:absolute;inset:0">
  <div id="scr"><div id="stg"><i class="wp wA"></i><i class="wp wW"></i><i class="wp wT"></i><div class="frame"></div>
{panes}
    <div id="dockw">{dock_nav}</div>
    <div class="rip" id="rip"></div>
    <svg id="cur0" viewBox="0 0 24 24"><path d="M3.2 2.2 3.2 19.4 7.6 15.2 10.6 21.8 13.6 20.4 10.7 14 16.8 14Z" fill="#fff" stroke="#000" stroke-width="1.5" stroke-linejoin="round"/></svg>
    <div id="glare"></div>
  </div></div>
  </div>
{txt}
</div>
<script src="timeline.js"></script>
</body></html>
"""
(HERE / "scene.html").write_text(html, encoding="utf-8")
print("scene.html", len(html), "bytes")
