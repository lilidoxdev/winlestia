# Espace ("Tout ton espace") redesign progress
- File: src/frontend/qml/WorkspaceBrowser.qml (always instantiated in Main.qml, visible toggled by pageMode).
- Python: AppData/Local/Programs/Winlestia/Caelestia-Windows/.venv/Scripts/python.exe (PySide6 ok if version printed).
- Harness: src/frontend/tests/render_espace.py + EspaceHarness.qml (new). Scratch PNGs in session scratchpad.
## Log
- [1] Located panel, read it. Candidate causes: per-delegate C.ToolTip (Popup) + StateLayer, duplicate Bridge.action("icon") per delegate, full JS recompute (indexed/base/categories/entries) on becoming visible.
- [2] Harness done (tests/render_espace.py + EspaceHarness.qml, stub 372 items). BEFORE (orig WorkspaceBrowser, saved at scratchpad/WorkspaceBrowser.orig.qml): open->first-frame median 43.4 ms (runs 40.6-47.2), of which ~35 ms is synchronous inside setting visible (JS index/filter only 1-3 ms; rest = ~34 delegate creations, each with StateLayer+C.ToolTip Popup+dup Bridge.action("icon")); delegates destroyed on every hide (model:[] when hidden). ToolTip removal alone = -4 ms.
- [3] WorkspaceBrowser.qml rewritten (persistent ListModel with incremental diff, recycled delegates, shared tooltip, sliding nav pill, wrapped chips, springs, staggered reveal, fx gating). NEXT: run harness, fix warnings, shots, bench after.
- [4] File WAS written (coordinator note was stale). Harness check_espace.py: 16/16 PASS (hidden=0 frames, settled idle=0 frames, click/right-click/Ctrl+F/type/Down+Enter/chips/scope/list/empty/catalog change). AFTER bench median ~7.4 ms (before 43.4). Shot espace-before.png saved. Popup menu not captured by grabWindow (opened=true verified). NEXT: reduced-effects run, final shots red/teal/1366/search, final report. Do NOT touch Workspaces.qml/Content.qml/Models.cs.
- [5] DONE. Final: before median 43.5 ms, after 7.9 ms (fx) / 4.9 ms (reduced); check_espace 16/16 in fx and reduced; all PNGs read. Only report remains.
