/* Winlestia promo video - deterministic timeline (22 s, 6 plans).
   Everything is a pure function of t (seconds): window.render(t). No CSS animation, no timers.
   Layout adapts to the viewport: 1080x1920 (9:16) or 1920x1080 (16:9). Debug: scene.html?t=12.4 */
(function () {
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var clamp = function (x, a, b) { return Math.max(a, Math.min(b, x)); };
  var seg = function (t, a, b) { return clamp((t - a) / (b - a), 0, 1); };
  function bez(x1, y1, x2, y2) {   // CSS cubic-bezier solver
    return function (x) {
      if (x <= 0) return 0; if (x >= 1) return 1;
      var lo = 0, hi = 1, t = x, i, cx;
      for (i = 0; i < 32; i++) {
        cx = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t;
        if (cx < x) lo = t; else hi = t; t = (lo + hi) / 2;
      }
      return 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
    };
  }
  var SPR = bez(.38, 1.21, .22, 1), DEC = bez(.05, .7, .1, 1), IO = bez(.4, 0, .2, 1);

  var W = window.innerWidth, H = window.innerHeight, PORTRAIT = H > W;
  var ZK = PORTRAIT ? 1 : .38, TK = PORTRAIT ? 1 : .6;
  var L, sc, pts = {}, ready = false, P = {}, startBtn, apps = [], appPts = [], hov = [];

  // ---------------------------------------------------------------- layout
  function fit(el, size, maxW) {
    el.style.fontSize = size + 'px';
    var tot = $$('.w', el).reduce(function (a, w) { return a + w.getBoundingClientRect().width + size * .28; }, 0);
    if (tot > maxW) el.style.fontSize = (size * maxW / tot) + 'px';
    return parseFloat(el.style.fontSize);
  }
  function layout() {
    L = PORTRAIT ? { sw: 1010, cy: H * .545, fs1: 190, fs2: 64, top: 245, t1: 205 }
                 : { sw: H * 1.05, cy: H * .668, fs1: H * .125, fs2: H * .052, top: H * .06, t1: H * .045 };
    L.sh = L.sw / 1.6; sc = L.sw / 1280;
    var s = $('#scr'); s.style.width = L.sw + 'px'; s.style.height = L.sh + 'px';
    s.style.marginLeft = (-L.sw / 2) + 'px'; s.style.marginTop = (-L.sh / 2) + 'px';
    s.style.left = (W / 2) + 'px'; s.style.top = L.cy + 'px';
    $('#stg').style.transform = 'scale(' + sc + ')';
    var f1 = fit($('#t1'), L.fs1, W * .86);
    $('#t1').style.top = L.t1 + 'px';
    var fb = fit($('#t1b'), L.fs2, W * .92);
    $('#t1b').style.top = (L.t1 + f1 * 1.12 + 6) + 'px';
    ['t2', 't3', 't4', 't5'].forEach(function (id) { fit($('#' + id), L.fs2 * 1.12, W * .92); $('#' + id).style.top = L.top + 'px'; });
    // final card (centred block)
    var lt = PORTRAIT ? H * .20 : H * .13;
    var fl = $('#fl'); fl.style.top = lt + 'px';
    var fnS = fit($('#fn'), PORTRAIT ? 190 : H * .15, W * .78), fn = $('#fn'); fn.style.top = (lt + 150) + 'px';
    var fpS = fit($('#fp'), PORTRAIT ? 64 : H * .052, W * .76), fp = $('#fp'); fp.style.top = (lt + 150 + fnS * 1.15 + 8) + 'px';
    var fu = $('#fu'); fu.style.fontSize = (PORTRAIT ? 44 : H * .037) + 'px'; fu.style.top = (lt + 150 + fnS * 1.15 + 8 + fpS * 1.3 + 36) + 'px';
  }

  // ---------------------------------------------------------------- measuring (stage units, identity transforms)
  function stageXY(el) {
    var r = el.getBoundingClientRect(), g = $('#stg').getBoundingClientRect();
    return [(r.left + r.width / 2 - g.left) / sc, (r.top + r.height / 2 - g.top) / sc];
  }
  function stageRect(el) {
    var r = el.getBoundingClientRect(), g = $('#stg').getBoundingClientRect();
    return [(r.left - g.left) / sc, (r.top - g.top) / sc, (r.right - g.left) / sc, (r.bottom - g.top) / sc];
  }
  function measure() {
    ['pw', 'palw', 'pm', 'pp', 'ps', 'pt', 'po'].forEach(function (id) { P[id] = $('#' + id); });
    startBtn = $('.d-start');
    var ib = $$('.dock > .ibtn');
    apps = $$('.dock .d-app'); appPts = apps.map(stageXY);
    pts.start = stageXY(startBtn); pts.search = stageXY(ib[0]); pts.gear = stageXY(ib[4]);
    apps.forEach(function (a, i) { pts['app' + (i + 1)] = appPts[i]; });
    pts.play = stageXY($('#pw .media .ctl svg:nth-child(2)')); pts.cal = stageXY($('#pw .cal .today'));
    pts.palCalc = stageXY($('#palw .pal-it:nth-child(2)'));
    pts.tabMedia = stageXY($('#pw .tab:nth-child(2)')); pts.tabPerf = stageXY($('#pm .tab:nth-child(3)'));
    pts.mSlider = stageXY($('#pm .m-prog .slider')); pts.ring1 = stageXY($('#pp .p-res:nth-child(1)')); pts.ring2 = stageXY($('#pp .p-res:nth-child(2)'));
    pts.spProj = stageXY($('#ps .w-space:nth-child(3)')); pts.tile3 = stageXY($('#ps .w-tile:nth-child(3)'));
    pts.toolC1 = stageXY($('#po .t-card:nth-child(1)')); pts.toolC4 = stageXY($('#po .t-card:nth-child(4)'));
    pts.p0 = [1040, 150]; pts.out = [1235, 640];
    // elements that react to hover
    ['#po .t-card', '#ps .w-space', '#ps .w-tile', '#palw .pal-it', '#pw .tab', '#pm .tab', '#pp .tab', '#pm .m-split'].forEach(function (sel) {
      $$(sel).forEach(function (el) { hov.push({ el: el, r: stageRect(el), pn: el.closest('.pn').id }); });
    });
    // panel items that spring in one after the other
    P.items = {
      pw: $$('#pw .grid > *'), palw: $$('#palw .pal-it'), pm: $$('#pm .m-cw,#pm .m-det,#pm .m-bg'),
      pp: $$('#pp .p-res'), ps: $$('#ps .w-space'), po: $$('#po .t-card')
    };
    P.tiles = $$('#ps .w-tile');
    P.bars = $$('#pw .res .fill'); P.tgt = P.bars.map(function (b) { return parseFloat(b.style.height); });
    P.vis = $$('#vis line'); P.cover1 = $('#pw .media .cover'); P.cover2 = $('#pm .cover'); P.ctl = $('#pw .media .ctl svg:nth-child(2)');
    P.mf = $('#pm .m-prog .f'); P.mh = $('#pm .m-prog .h');
    // performance rings: reveal the value arcs with a dash, count the numbers up
    P.rings = $$('#pp .p-res').map(function (r) {
      var ps = $$('path', r).filter(function (p, i) { return i === 1 || i === 3; });
      ps.forEach(function (p) { p.setAttribute('pathLength', '1'); });
      return { ps: ps, labs: [$('.l1', r), $('.l2', r)].map(function (e) { return { e: e, o: e.textContent }; }) };
    });
    // terminal
    var T = P.term = {};
    ['pr1', 'pr2', 'pr3', 'out1', 'out2', 'cmd1', 'cmd2', 'cur1', 'cur2', 'cur3'].forEach(function (k) { T[k] = $('#' + k); });
    T.s1 = 'git status'; T.s2 = 'clink --version';
    T.c1 = cum(T.s1, 16.20); T.c2 = cum(T.s2, 17.45); T.e1 = 16.95; T.e2 = 18.50;
    P.q = { s: '24 × 7', el: $('#qtxt') };
    P.q.c = cum(P.q.s, 5.55, .11);
    P.rip = $('#rip'); P.cur = $('#cur0');
  }
  function cum(s, t0, fixed) {   // deterministic, slightly irregular typing cadence
    var a = [], t = t0;
    for (var i = 0; i < s.length; i++) {
      var h = Math.abs(Math.sin((i + 1) * 12.9898) * 43758.5453) % 1;
      a.push(t); t += fixed || (.038 + .045 * h);
    }
    return a;
  }
  function typed(s, c, t) { var n = 0; while (n < c.length && t >= c[n]) n++; return s.slice(0, n); }

  // ---------------------------------------------------------------- cursor
  function cubic(p0, p3, k, u) {
    var dx = p3[0] - p0[0], dy = p3[1] - p0[1], d = Math.hypot(dx, dy) || 1, nx = -dy / d, ny = dx / d;
    var c1 = [p0[0] + dx * .25 + nx * d * k, p0[1] + dy * .25 + ny * d * k];
    var c2 = [p0[0] + dx * .78 + nx * d * k * .35, p0[1] + dy * .78 + ny * d * k * .35];
    var v = 1 - u, a = v * v * v, b = 3 * v * v * u, c = 3 * v * u * u, e = u * u * u;
    return [a * p0[0] + b * c1[0] + c * c2[0] + e * p3[0], a * p0[1] + b * c1[1] + c * c2[1] + e * p3[1]];
  }
  var PATH = [   // [t0, t1, from, to, bend]
    [0.70, 1.72, 'p0', 'start', .20], [2.05, 2.95, 'start', 'play', -.16], [3.12, 3.82, 'play', 'cal', .18],
    [4.05, 5.08, 'cal', 'search', -.22], [6.35, 7.12, 'search', 'palCalc', .15], [7.55, 8.26, 'palCalc', 'tabMedia', -.2],
    [8.55, 9.22, 'tabMedia', 'mSlider', .18], [9.38, 10.03, 'mSlider', 'tabPerf', -.2],
    [10.25, 10.95, 'tabPerf', 'ring1', .2], [11.1, 11.75, 'ring1', 'ring2', -.15],
    [12.05, 12.35, 'ring2', 'app1', .1], [12.38, 12.62, 'app1', 'app3', .05], [12.64, 12.86, 'app3', 'app6', .05], [12.9, 13.07, 'app6', 'start', .05],
    [13.4, 14.12, 'start', 'spProj', .16], [14.3, 14.85, 'spProj', 'tile3', -.15],
    [15.0, 15.42, 'tile3', 'app3', .12], [15.9, 16.15, 'app3', 'out', .1],
    [19.0, 19.38, 'out', 'gear', -.14], [19.75, 20.28, 'gear', 'toolC4', .16]
  ];
  var CLICKS = [[1.72, 'start'], [2.98, 'play'], [5.10, 'search'], [7.15, 'palCalc'], [8.28, 'tabMedia'], [9.25, 'mSlider'], [10.05, 'tabPerf'],
    [13.10, 'start'], [14.15, 'spProj'], [15.45, 'app3'], [19.40, 'gear'], [20.30, 'toolC4']];
  function cursorAt(t) {
    var p = pts.p0, i, s;
    for (i = 0; i < PATH.length; i++) {
      s = PATH[i];
      if (t >= s[0] && t <= s[1]) return cubic(pts[s[2]], pts[s[3]], s[4], IO(seg(t, s[0], s[1])));
      if (t > s[1]) p = pts[s[3]];
    }
    return p;
  }
  function curOpacity(t) {
    return seg(t, .70, .85) * (1 - seg(t, 15.95, 16.2)) + seg(t, 19.0, 19.12) * (1 - seg(t, 20.3, 20.45)) * (t > 16 ? 1 : 0);
  }

  // ---------------------------------------------------------------- camera / screen
  function punch(t, t0, amp) {
    var u = t - t0; if (u < 0) return 0;
    return u < .07 ? amp * u / .07 : amp * (1 - DEC(seg(u, .07, .6)));
  }
  var PUNCH = [[.45, .06], [1.72, .08], [2.98, .06], [4.35, .06], [5.1, .07], [8.28, .06], [10.05, .06], [10.7, .10], [13.1, .07], [14.15, .06],
    [15.45, .07], [16.95, .06], [18.5, .07], [19.4, .06], [20.3, .06], [20.45, .08]];
  var CUTS = [8.0, 12.0, 15.0, 19.0];
  var KEYS = [[0, 1, 0, -8], [3.85, 1.30, -38, -3], [8.0, 1.22, -20, -4], [10.0, 1.35, -30, -2], [12.0, 1.05, 60, -7],
    [13.1, 1.28, -30, -3], [15.0, 1.0, 10, 5], [15.6, 1.28, -10, -2], [19.0, 1.15, 0, -5], [20.35, .9, 40, -6]];
  function keyState(t) {
    var i = 0; while (i + 1 < KEYS.length && KEYS[i + 1][0] <= t) i++;
    var k = KEYS[i], p = KEYS[Math.max(0, i - 1)];
    if (i === 0) return [k[1], k[2], k[3]];
    var d = seg(t, k[0], k[0] + .65), z = p[1] + (k[1] - p[1]) * SPR(d), e = DEC(d);
    return [z, p[2] + (k[2] - p[2]) * e, p[3] + (k[3] - p[3]) * e];
  }
  function scrState(t) {   // [scale, rotateY, ty]
    var e = SPR(seg(t, 0, .8)), k = keyState(t);
    return [(.80 + .20 * e) * (1 + (k[0] - 1) * ZK), k[2] + (1 - e) * 34 + .6 * Math.sin(t * 1.1), (1 - e) * 170 + k[1] * TK];
  }
  function whip(t) { var w = 0; CUTS.forEach(function (c) { w = Math.max(w, 1 - Math.abs(t - c) / .14); }); return Math.max(0, w); }

  // ---------------------------------------------------------------- kinetic type
  function word(el, t, t0, te) {
    var u = t - t0, k = seg(u, 0, .30), op, sc2, bl, y;
    if (t < t0) { el.style.opacity = 0; return; }
    op = Math.min(1, DEC(k) * 2.4); sc2 = 1.15 - .15 * SPR(k); bl = 16 * (1 - DEC(k)); y = 16 * (1 - DEC(k));
    if (te !== undefined) { var e = seg(t, te, te + .28); op *= 1 - e; y -= 70 * IO(e); bl += 14 * e; sc2 *= 1 + .06 * e; }
    el.style.opacity = op;
    el.style.transform = 'translateY(' + y.toFixed(2) + 'px) scale(' + sc2.toFixed(4) + ')';
    el.style.filter = bl > .3 ? 'blur(' + bl.toFixed(2) + 'px)' : 'none';
  }
  var TXT = [
    ['t1', [.45], [3.55]], ['t1b', [.85, 1.10, 1.35], [3.62, 3.67, 3.72]],
    ['t2', [4.15, 4.33, 4.50], [7.5, 7.55, 7.6]], ['t3', [8.3, 8.46, 8.62, 8.78], [11.55, 11.6, 11.65, 11.7]],
    ['t4', [12.2, 12.38, 12.56], [14.6, 14.65, 14.7]], ['t5', [15.2, 15.36, 15.52], [18.6, 18.65, 18.7]],
    ['fl', [20.35]], ['fn', [20.5]], ['fp', [20.85, 21.05, 21.25]], ['fu', [21.4]]
  ];

  // ---------------------------------------------------------------- panels
  var PANELS = [['pw', 1.75, 8.28, 1.95], ['palw', 5.15, 7.6, 5.9], ['pm', 8.3, 10.08, 8.45], ['pp', 10.1, 12.0, 10.25],
    ['ps', 13.1, 15.0, 13.3], ['pt', 15.5, 19.0, 0], ['po', 19.42, 20.35, 19.65]];
  function panel(id, o, c, ib, t) {
    var el = P[id], vis = t >= o - .01 && t < c + .3;
    el.style.visibility = vis ? 'visible' : 'hidden';
    if (!vis) return;
    var k = SPR(seg(t, o, o + .65)), e = seg(t, c, c + .28), s = (.78 + .22 * k) * (1 - .07 * IO(e)), op = DEC(seg(t, o, o + .25)) * (1 - e);
    if (id === 'palw') { s = (.9 + .1 * k) * (1 - .06 * IO(e)); }
    el.style.opacity = op.toFixed(3);
    el.style.transform = 'scale(' + s.toFixed(4) + ')';
    el.style.filter = e > .02 ? 'blur(' + (e * 9).toFixed(1) + 'px)' : 'none';
    var it = P.items[id]; if (!it) return;
    it.forEach(function (c2, i) {
      var kk = seg(t, ib + i * .06, ib + i * .06 + .6), ee = SPR(kk);
      c2.style.opacity = t < ib + i * .06 ? 0 : DEC(seg(kk, 0, .5));
      c2.style.transform = 'translateY(' + ((1 - ee) * 26).toFixed(1) + 'px) scale(' + (.92 + .08 * ee).toFixed(4) + ')';
    });
  }

  function lab(l, k) {   // count a label up: "7,8 Go" -> animates the number, keeps unit and decimal comma
    var m = /^(\d+(?:[.,]\d+)?)(.*)$/.exec(l.o); if (!m) return;
    var dec = (m[1].split(/[.,]/)[1] || '').length, v = parseFloat(m[1].replace(',', '.')) * k, s = v.toFixed(dec);
    l.e.textContent = (m[1].indexOf(',') >= 0 ? s.replace('.', ',') : s) + m[2];
  }

  // ---------------------------------------------------------------- render
  function render(t) {
    if (!ready) return;
    // camera
    var pz = 1; PUNCH.forEach(function (p) { pz += punch(t, p[0], p[1]); });
    var wp = whip(t), z = 1.025 * (1 + .10 * (1 - DEC(seg(t, 0, 1.6)))) * pz * (1 + .04 * seg(t, 16, 18.6)) * (1 + .03 * wp);
    var dx = 2.6 * t, dy = -1.6 * t;
    $('#cam').style.transform = 'translate(' + dx.toFixed(2) + 'px,' + dy.toFixed(2) + 'px) scale(' + z.toFixed(4) + ')';
    // backgrounds: aurora -> warm (plan 4) -> teal (plans 5-6) -> aurora (end card)
    var wW = seg(t, 12.0, 12.4) * (1 - seg(t, 15.0, 15.4)), wT = seg(t, 15.0, 15.4) * (1 - seg(t, 20.4, 20.9)), wA = 1 - wW - wT;
    [['.wA', wA], ['.wW', wW], ['.wT', wT]].forEach(function (b) {
      $$(b[0]).forEach(function (el) { el.style.opacity = clamp(b[1], 0, 1).toFixed(3); });
    });
    $('#bg').style.transform = 'translate(' + (-dx * 3).toFixed(1) + 'px,' + (3 * -dy).toFixed(1) + 'px) scale(' + (1.02 + .012 * t).toFixed(4) + ')';
    $('#stg').className = t >= 15.0 ? 'amb-teal' : t >= 12.0 ? 'amb-warm' : '';

    // screen
    var s = scrState(t), s2 = scrState(t + .02), s1 = scrState(t - .02), scr = $('#scr');
    var fin = seg(t, 20.35, 20.75);
    scr.style.opacity = (seg(t, 0, .15) * (1 - IO(fin))).toFixed(3);
    var rx = 3 + .5 * Math.sin(t * .8);
    scr.style.transform = 'translateY(' + s[2].toFixed(2) + 'px) perspective(2300px) rotateY(' + s[1].toFixed(3) + 'deg) rotateX(' + rx.toFixed(3) + 'deg) scale(' + s[0].toFixed(4) + ')';
    var bx = clamp(Math.abs((s2[1] - s1[1]) * 1.3) + 16 * wp, 0, 18), by = clamp(Math.abs(s2[2] - s1[2]) * .12 + Math.abs(s2[0] - s1[0]) * 70, 0, 10), mbw = $('#mbw');
    if (bx + by > .5) { $('#mbs').setAttribute('stdDeviation', bx.toFixed(2) + ' ' + by.toFixed(2)); mbw.style.filter = 'url(#mb)'; } else mbw.style.filter = 'none';

    // dock: rises, then magnifies icons under the cursor
    var dw = $('#dockw'); dw.style.opacity = seg(t, .35, .55);
    dw.style.transform = 'translateY(' + ((1 - SPR(seg(t, .35, .95))) * 130).toFixed(1) + 'px)';
    var c = cursorAt(t), jx = Math.sin(t * 7) * .8, jy = Math.cos(t * 6) * .8, near = clamp(1 - Math.abs(c[1] - appPts[0][1]) / 70, 0, 1);
    apps.forEach(function (a, i) { var g = Math.exp(-Math.pow((c[0] - appPts[i][0]) / 46, 2)) * near; a.style.transform = 'translateY(' + (-7 * g).toFixed(2) + 'px) scale(' + (1 + .42 * g).toFixed(3) + ')'; });
    var sp = 0; CLICKS.forEach(function (k) { if ((k[1] === 'start') && t > k[0] && t < k[0] + .13) sp = 1; });
    startBtn.style.transform = 'scale(' + (1 - .14 * sp).toFixed(2) + ')';

    // panels
    PANELS.forEach(function (p) { panel(p[0], p[1], p[2], p[3], t); });
    var dim = DEC(seg(t, 5.15, 5.5)) * (1 - DEC(seg(t, 7.5, 7.9)));   // dashboard dims under the quick command
    if (t >= 5.15 && t < 8.3) { P.pw.style.opacity = (parseFloat(P.pw.style.opacity) * (1 - .8 * dim)).toFixed(3); }

    // dashboard
    P.bars.forEach(function (b, i) { b.style.height = (P.tgt[i] * DEC(seg(t, 2.35 + i * .12, 3.2))).toFixed(2) + '%'; });
    var playing = t > 2.98, spin = playing ? 140 * (t - 2.98) : 0;
    P.ctl.firstElementChild.setAttribute('href', playing ? '#i-pause' : '#i-play');
    P.cover1.style.transform = 'rotate(' + spin.toFixed(2) + 'deg)';
    P.ctl.style.transform = 'scale(' + (t > 2.98 && t < 3.1 ? .82 : 1) + ')';
    // quick command: typing, then result list
    P.q.el.textContent = typed(P.q.s, P.q.c, t);
    // media panel
    if (t >= 8.2 && t < 10.5) {
      P.vis.forEach(function (l, i) {
        var h = 6 + 30 * Math.abs(Math.sin(i * .55 + t * 4.1) * Math.cos(i * .21 + 1 + t * 2.3)) + 8 * Math.abs(Math.sin(i * 1.9 + t * 7)), a = i / 64 * Math.PI * 2 - Math.PI / 2;
        l.setAttribute('x2', (155 + Math.cos(a) * (82 + h)).toFixed(1)); l.setAttribute('y2', (155 + Math.sin(a) * (82 + h)).toFixed(1));
      });
      P.cover2.style.transform = 'rotate(' + (140 * (t - 2.98)).toFixed(1) + 'deg)';
      var pr = 43 + (t - 8.3) * 1.0 + 28 * SPR(seg(t, 9.25, 9.7));
      P.mf.style.width = pr.toFixed(2) + '%'; P.mh.style.left = pr.toFixed(2) + '%';
    }
    // performance panel
    P.rings.forEach(function (r, i) {
      var k = DEC(seg(t, 10.35 + i * .12, 11.3 + i * .12));
      r.ps.forEach(function (p) { p.setAttribute('stroke-dasharray', (k * 1.001).toFixed(4) + ' 1'); p.style.opacity = k > 0.001 ? 1 : 0; });
      r.labs.forEach(function (l) { lab(l, k); });
    });
    // spaces
    P.tiles.forEach(function (el, i) {
      var k1 = SPR(seg(t, 13.45 + i * .05, 14.0 + i * .05)), k2 = t > 14.2 ? SPR(seg(t, 14.2 + i * .04, 14.7 + i * .04)) : 1;
      el.style.opacity = t < 13.45 + i * .05 ? 0 : (t > 14.2 ? .45 + .55 * DEC(seg(t, 14.2 + i * .04, 14.5 + i * .04)) : DEC(seg(k1, 0, .5)));
      el.style.transform = 'scale(' + ((.85 + .15 * k1) * (.9 + .1 * k2)).toFixed(4) + ')';
    });
    var proj = $('#ps .w-space:nth-child(3)'), pk = t > 14.15 ? DEC(seg(t, 14.15, 14.5)) : 0;
    proj.style.background = pk > 0 ? 'color-mix(in srgb,var(--primary) ' + (34 * pk).toFixed(0) + '%,var(--surface-container))' : '';
    // terminal: typing, output drops at once, blinking cursor
    var T = P.term, blink = Math.floor(t * 2.2) % 2 === 0;
    T.cmd1.textContent = typed(T.s1, T.c1, t); T.cmd2.textContent = typed(T.s2, T.c2, t);
    var t1 = t >= T.c1[0] && t < T.e1, t2 = t >= T.c2[0] && t < T.e2;
    T.cur1.style.visibility = t < T.e1 && (t1 || blink) ? 'visible' : 'hidden';
    T.cur2.style.visibility = t >= T.e1 && t < T.e2 && (t2 || blink) ? 'visible' : 'hidden';
    T.cur3.style.visibility = t >= T.e2 && blink ? 'visible' : 'hidden';
    T.pr2.style.visibility = T.out1.style.visibility = t >= T.e1 ? 'visible' : 'hidden';
    T.pr3.style.visibility = T.out2.style.visibility = t >= T.e2 ? 'visible' : 'hidden';
    [[T.out1, T.e1], [T.out2, T.e2]].forEach(function (o) {
      var u = seg(t, o[1], o[1] + .2);
      o[0].style.opacity = Math.min(1, u * 5).toFixed(2); o[0].style.transform = 'translateY(' + ((1 - DEC(u)) * 12).toFixed(1) + 'px)';
    });
    // tools: card flash on click
    var tc = $('#po .t-card:nth-child(4)'); tc.style.background = t > 20.3 ? 'color-mix(in srgb,var(--primary) ' + (30 * (1 - seg(t, 20.3, 20.45))).toFixed(0) + '%,var(--surface-container))' : '';

    // hover highlights
    hov.forEach(function (h) {
      var on = P[h.pn].style.visibility === 'visible' && c[0] > h.r[0] && c[0] < h.r[2] && c[1] > h.r[1] && c[1] < h.r[3] && t < 20.45;
      h.el.style.boxShadow = on ? 'inset 0 0 0 200px rgba(255,255,255,.08)' : 'none';
    });

    // cursor + ripples
    var press = 0; CLICKS.forEach(function (k) { if (t >= k[0] && t < k[0] + .13) press = 1; });
    var cur = P.cur; cur.style.opacity = clamp(curOpacity(t), 0, 1).toFixed(2);
    cur.style.transform = 'translate(' + (c[0] + jx - 3).toFixed(2) + 'px,' + (c[1] + jy - 2).toFixed(2) + 'px) scale(' + (press ? .84 : 1) + ')';
    cur.style.transformOrigin = '3px 2px';
    var ro = 0, rs = 0, rp = pts.p0;
    CLICKS.forEach(function (k) { var kk = seg(t, k[0], k[0] + .55); if (t >= k[0] && kk < 1) { ro = .85 * (1 - DEC(kk)); rs = .25 + 1.6 * DEC(kk); rp = pts[k[1]]; } });
    P.rip.style.opacity = ro; P.rip.style.transform = 'translate(' + rp[0] + 'px,' + rp[1] + 'px) scale(' + rs.toFixed(3) + ')';

    // kinetic type
    TXT.forEach(function (g) {
      var ws = g[3] || (g[3] = $$('.w', $('#' + g[0])));
      ws.forEach(function (w, i) { word(w, t, g[1][i], g[2] ? g[2][i] : undefined); });
    });
  }

  window.render = render; window.READY = false;
  layout();
  (document.fonts ? document.fonts.ready : Promise.resolve()).then(function () {
    layout(); measure(); ready = true; window.READY = true;
    var q = /[?&]t=([\d.]+)/.exec(location.search); render(q ? parseFloat(q[1]) : 0);
  });
})();
