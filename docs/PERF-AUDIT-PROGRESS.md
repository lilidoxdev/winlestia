# Dashboard perf audit

Live idle, 30 s, read-only: pythonw 1.25 % of one core, WS 546 MB, 1601 handles, 155 threads; Panneaux.Glass 1.77 % core, WS 246 MB, 1150 handles, 32 threads.

Harness: src/frontend/tests/perf_dashboard.py + DashHarness.qml (offscreen, software scenegraph, vsync-capped ~16 ms: frame intervals show dropped frames, not GPU cost).
Findings: hidden tabs idle (0 timers, 0 animations, none loaded until first visit); cold first switch costs 15 ms (Media), 3 ms (Performance), 26 ms (Workspaces) synchronous.
Prime suspect fixed: frontend.py update_state emitted Bridge.changed on every poll because performance/sysInfo (cpu %, etc.) differ each poll; every Bridge binding re-evaluated (1.6 ms/emit in stub, more with real getters). Poll is 55 ms (page 1 visible), 180 ms visible, 500 ms hidden.
Fix: perfChanged signal for performance/sysInfo; changed only for other keys.
Not changed: Wrapper.qml:48 layer.enabled on whole dashboard (not measurable offscreen), sync Loaders (Content sizing depends on currentItem).
Before/after: see final report. Backend signal rate not measured live (would require talking to the pipe).
