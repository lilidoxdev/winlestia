"""Assemble video/scene.html (motion-design illustration, HTML/CSS compositing of the rendered stills).
Run:  python build_scene.py     (needs video/shots/*.png from  render.ps1 -Shots)
The repository URL shown on the closing card can be overridden with WINLESTIA_REPO_URL."""
import os
from pathlib import Path

REPO_URL = os.environ.get("WINLESTIA_REPO_URL", "github.com/lilidoxdev/winlestia")
HERE = Path(__file__).resolve().parent

# (key, image) -- every layer exists twice: in the card and, blurred, in the backdrop
SHOTS = [("dash", "dashboard"), ("perf", "performance"), ("wsA", "v-workspaces"), ("dock", "v-dock-palette"),
         ("tools", "v-tools"), ("sett", "v-settings"), ("wall", "wallpaper"), ("wsW", "v-workspaces-warm"),
         ("wsT", "v-workspaces-teal")]


def layers(tag):
    return "\n".join(f'<img class="L" data-s="{k}" id="{tag}_{k}" src="shots/{img}.png" alt="">' for k, img in SHOTS)


html = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Winlestia - motion design illustration</title>
<style>
@font-face {{ font-family:"Rubik"; src:url("../fonts/Rubik.ttf"); font-weight:300 900; }}
*{{margin:0;box-sizing:border-box}}
html,body{{width:100vw;height:100vh;overflow:hidden;background:#0a0613;font-family:"Rubik",system-ui,sans-serif;color:#fff}}
#root{{position:absolute;inset:0;overflow:hidden}}
#bgw{{position:absolute;inset:-60px;background:url("../wallpaper-aurora.svg") center/cover;filter:saturate(1.1)}}
#bg{{position:absolute;left:50%;top:50%;width:1280px;height:800px;transform-origin:0 0;filter:blur(38px) brightness(.55) saturate(1.2)}}
.L{{position:absolute;left:0;top:0;width:1280px;height:800px;opacity:0;will-change:opacity,transform,clip-path}}
#card{{position:absolute;left:0;top:0;width:1280px;height:800px;transform-origin:0 0;border-radius:22px;overflow:hidden;
  box-shadow:0 30px 90px rgba(5,2,14,.65),0 0 0 1px rgba(255,255,255,.10)}}
#cardin{{position:absolute;inset:0;transform-origin:50% 50%}}
#cap{{position:absolute;left:0;right:0;text-align:center}}
#cap .k{{font-weight:700;letter-spacing:-.5px;text-shadow:0 4px 30px rgba(60,20,120,.6)}}
#cap .s{{font-weight:400;color:rgba(255,255,255,.82);margin-top:8px}}
#foot{{position:absolute;font-size:20px;font-weight:400;letter-spacing:.3px;color:rgba(255,255,255,.78);padding:6px 14px;border-radius:99px;
  background:rgba(10,6,19,.45);border:1px solid rgba(255,255,255,.16)}}
#title,#end{{position:absolute;inset:0;opacity:0}}
#title .bgp,#end .bgp{{position:absolute;inset:-40px;background:url("../wallpaper-aurora.svg") center/cover;filter:saturate(1.15)}}
#title .shade,#end .shade{{position:absolute;inset:0;background:radial-gradient(ellipse 60% 50% at 50% 50%,rgba(10,6,19,.62),rgba(10,6,19,.18) 70%,rgba(10,6,19,0))}}
.cen{{position:absolute;left:0;right:0;display:flex;flex-direction:column;align-items:center;text-align:center}}
.mark{{width:250px;height:178px;background:url("../../../../src/frontend/assets/winlestia-mark.png") no-repeat;background-size:350px 350px;background-position:-53px -76px;
  filter:drop-shadow(0 0 22px rgba(214,150,255,.75)) drop-shadow(0 6px 22px rgba(60,20,120,.6))}}
.nm{{font-weight:700;letter-spacing:-2px;line-height:1;display:flex;filter:drop-shadow(0 4px 24px rgba(120,40,200,.55))}}
.nm span{{display:inline-block;background:linear-gradient(180deg,#fff 30%,#e6c9ff 100%);-webkit-background-clip:text;color:transparent}}
.tg{{font-weight:500;color:rgba(255,255,255,.92);text-shadow:0 2px 14px rgba(20,8,40,.6)}}
.url{{padding:12px 28px;border-radius:99px;font-weight:500;color:#f4e9ff;background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.26);
  box-shadow:inset 0 1px 0 rgba(255,255,255,.18),0 6px 20px rgba(10,6,19,.35)}}
</style></head><body>
<div id="root">
  <div id="bgw"></div>
  <div id="bg">{layers("b")}</div>
  <div id="card"><div id="cardin">{layers("c")}</div></div>
  <div id="cap"><div class="k" id="capk"></div><div class="s" id="caps"></div></div>
  <div id="title"><div class="bgp"></div><div class="shade"></div>
    <div class="cen" id="tcen"><div class="mark" id="tmark"></div><div class="nm" id="tnm"></div><div class="tg" id="ttg">A port of Caelestia for Windows 11</div></div></div>
  <div id="end"><div class="bgp"></div><div class="shade"></div>
    <div class="cen" id="ecen"><div class="mark" id="emark"></div><div class="nm" id="enm"></div><div class="tg" id="etg">Desktop shell for Windows 11</div><div class="url" id="eurl">{REPO_URL}</div></div></div>
  <div id="foot">Motion design illustration</div>
</div>
<script src="timeline.js"></script>
</body></html>
"""
(HERE / "scene.html").write_text(html, encoding="utf-8")
print("wrote scene.html")
