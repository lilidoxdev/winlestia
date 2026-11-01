/* Deterministic timeline: window.render(t) positions everything for time t (seconds). No CSS animation, no timers. */
(function () {
  function bez(x1, y1, x2, y2) { // cubic-bezier easing
    return function (x) {
      if (x <= 0) return 0; if (x >= 1) return 1;
      var lo = 0, hi = 1, t = x;
      for (var i = 0; i < 30; i++) {
        t = (lo + hi) / 2;
        var cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t;
        if (cx < x) lo = t; else hi = t;
      }
      return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
    };
  }
  var DEC = bez(.05, .7, .1, 1), IO = bez(.2, 0, 0, 1);
  var cl = function (x) { return Math.max(0, Math.min(1, x)); };
  var seg = function (t, a, d) { return cl((t - a) / d); };
  var $ = function (id) { return document.getElementById(id); };

  var W = innerWidth, H = innerHeight, V = H > W;
  var S = V ? 0.82 : 1.15;                          // card scale
  var CW = 1280 * S, CH = 800 * S;
  var CX = (W - CW) / 2, CY = V ? 760 : 38;
  var cap = $("cap"), capk = $("capk"), caps = $("caps"), foot = $("foot");
  if (V) { cap.style.top = "250px"; capk.style.fontSize = "92px"; caps.style.fontSize = "40px"; cap.style.padding = "0 90px"; }
  else { cap.style.top = (CY + CH + 14) + "px"; capk.style.fontSize = "46px"; caps.style.fontSize = "26px"; }
  foot.style.left = V ? "90px" : "auto"; foot.style.right = V ? "auto" : "40px"; foot.style.bottom = V ? "120px" : "14px";
  var bg = $("bg"), card = $("card");
  card.style.left = CX + "px"; card.style.top = CY + "px"; card.style.transform = "scale(" + S + ")";
  var bs = Math.max(W / 1280, H / 800) * 1.15;
  bg.style.transform = "translate(" + (-1280 * bs / 2) + "px," + (-800 * bs / 2) + "px) scale(" + bs + ")";
  function word(el, txt, fs) { el.style.fontSize = fs + "px"; el.innerHTML = txt.split("").map(function (c) { return "<span>" + c + "</span>"; }).join(""); }
  var tfs = V ? 170 : 150;
  word($("tnm"), "Winlestia", tfs); word($("enm"), "Winlestia", tfs);
  ["tcen", "ecen"].forEach(function (id) { $(id).style.top = (V ? 620 : 250) + "px"; });
  $("tnm").style.marginTop = "26px"; $("enm").style.marginTop = "26px";
  $("ttg").style.cssText += ";margin-top:26px;font-size:" + (V ? 46 : 36) + "px;padding:0 80px";
  $("etg").style.cssText += ";margin-top:22px;margin-bottom:34px;font-size:" + (V ? 40 : 32) + "px";
  $("eurl").style.fontSize = (V ? 38 : 30) + "px";
  if (V) { $("tmark").style.transform = "scale(1.3)"; $("emark").style.transform = "scale(1.3)"; }

  // key, start, end, headline, sub
  var SH = [
    ["dash", 3.2, 6.2, "Dashboard", "Weather, calendar, resources and media"],
    ["perf", 6.2, 9.0, "Performance", "Temperature, memory and storage at a glance"],
    ["wsA", 9.0, 12.0, "Workspaces", "Applications, Games, Projects and Documents"],
    ["dock", 12.0, 15.0, "Dock and quick command", "Ctrl + F2 opens the palette"],
    ["tools", 15.0, 17.5, "Tools", "Capture, colour picker, pixel ruler and more"],
    ["sett", 17.5, 20.0, "Customize", "Theme, rendering and animations"],
    ["wall", 20.0, 22.2, "Wallpaper switcher", "Choose a wallpaper, press Enter to apply"]
  ];
  var TW = 22.2, TT = 24.0, END = 25.6, DUR = 28.8;
  var layersAll = {};
  ["b", "c"].forEach(function (p) {
    document.querySelectorAll('[id^="' + p + '_"]').forEach(function (e) { var k = e.dataset.s; (layersAll[k] = layersAll[k] || []).push(e); });
  });
  function put(k, op, ty, sc, clip) {
    layersAll[k].forEach(function (el) {
      el.style.opacity = op;
      el.style.transform = "translate(0px," + ty + "px) scale(" + sc + ")";
      el.style.clipPath = clip || "none";
    });
  }
  function hideAll() { for (var k in layersAll) put(k, 0, 0, 1); }
  function lay(k, t, a, b) {
    var i = DEC(seg(t, a, .65)), o = IO(seg(t, b - .45, .45));
    var drift = 1 + .035 * cl((t - a) / (b - a));
    put(k, i * (1 - o), (1 - i) * 46 - o * 10, (.955 + .045 * i) * drift * (1 + .02 * o));
  }
  var lastCap = "";
  function setCap(k, s, a, b, t) {
    var i = DEC(seg(t, a + .15, .55)), o = IO(seg(t, b - .35, .3));
    cap.style.opacity = i * (1 - o);
    cap.style.transform = "translateY(" + ((1 - i) * 24) + "px)";
    if (lastCap !== k) { capk.textContent = k; caps.textContent = s; lastCap = k; }
  }
  function titleAnim(el, mark, nm, tg, t, a, extra) {
    $(mark).style.opacity = DEC(seg(t, a + .1, .8));
    var sp = $(nm).children;
    for (var i = 0; i < sp.length; i++) {
      var q = DEC(seg(t, a + .35 + i * .06, .7));
      sp[i].style.opacity = q; sp[i].style.transform = "translateY(" + ((1 - q) * 60) + "px)";
    }
    $(tg).style.opacity = DEC(seg(t, a + 1.0, .7));
    if (extra) $(extra).style.opacity = DEC(seg(t, a + 1.3, .7));
  }

  window.render = function (t) {
    t = Math.min(t, DUR);
    hideAll();
    var bgd = IO(seg(t, 3.0, .6)) * (1 - IO(seg(t, END - .2, .5)));
    bg.style.opacity = bgd; card.style.opacity = bgd;
    var capOn = false;
    titleAnim($("title"), "tmark", "tnm", "ttg", t, 0.1);
    $("title").style.opacity = IO(seg(t, 0, .5)) * (1 - IO(seg(t, 3.0, .7)));
    for (var i = 0; i < SH.length; i++) {
      var s = SH[i];
      if (t >= s[1] - .1 && t <= s[2] + .1) { lay(s[0], t, s[1], s[2]); capOn = true; setCap(s[3], s[4], s[1], s[2], t); }
    }
    // recolour highlight: same panel, palette changes through a growing circle
    if (t >= TW - .1 && t <= END + .1) {
      var o = IO(seg(t, END - .45, .45)), base = DEC(seg(t, TW, .6));
      var r1 = IO(seg(t, TW + .4, 1.3)), r2 = IO(seg(t, TT, 1.3));
      var ty = (1 - base) * 40, sc = .955 + .045 * base;
      put("wsA", base * (1 - o), ty, sc);
      put("wsW", r1 > 0 ? base * (1 - o) : 0, ty, sc, "circle(" + (r1 * 150) + "% at 50% 100%)");
      put("wsT", r2 > 0 ? base * (1 - o) : 0, ty, sc, "circle(" + (r2 * 150) + "% at 50% 100%)");
      capOn = true;
      var k2 = t < TW + 1.2 ? "Change the wallpaper" : "The whole shell recolours";
      var oo = IO(seg(t, END - .35, .3)), ii = DEC(seg(t, TW + .15, .55));
      cap.style.opacity = ii * (1 - oo); cap.style.transform = "translateY(" + ((1 - ii) * 24) + "px)";
      if (lastCap !== k2) { capk.textContent = k2; caps.textContent = "Automatic theme follows the wallpaper"; lastCap = k2; }
    }
    if (!capOn) cap.style.opacity = 0;
    titleAnim($("end"), "emark", "enm", "etg", t, END + .1, "eurl");
    $("end").style.opacity = IO(seg(t, END - .1, .7));
    foot.style.zIndex = 10;
  };
  window.READY = false;
  var fonts = document.fonts ? document.fonts.ready : Promise.resolve();
  var imgs = Array.prototype.slice.call(document.images).map(function (im) { return im.decode ? im.decode().catch(function () {}) : Promise.resolve(); });
  Promise.all([fonts].concat(imgs)).then(function () { render(0); window.READY = true; });
})();
