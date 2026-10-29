# Workspaces tab redesign
- Rewrote src/frontend/qml/qs/modules/dashboard/Workspaces.qml (header, 4 category cards, favoris + recents cards, quick-action chips). Palette tokens only; handlers/Bridge calls unchanged (navigate workspace:*, clipboard, palette, settings; action launch/icon/websites).
- Render check: src/frontend/tests/render_workspaces.py (+ WorkspacesHarness.qml), offscreen, stub Bridge.
