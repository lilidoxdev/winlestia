# Workspaces tab v2 (progress)
- Regression analysis done: QML data source (Bridge.favorites + Bridge.iconUrl(id, iconRevision)) is IDENTICAL to pre-redesign (backup step6b). Cause is data: preferences-glass.json Favorites = ['dock:...\Discord\app-1.0.9265\Discord.exe'] only; that folder no longer exists (Discord is app-1.0.9266) so icon extraction fails -> letter "D". Claude (uwp:Claude_...) / ChatGPT (uwp:OpenAI.Codex_...) / named:discord are not in State.Favorites any more.
- Plan: rewrite Workspaces.qml (blob shapes, Entrance stagger gated by tabActive&&visible, spring hover), Content.qml passes tabActive, harness stub gets icons.
- DONE: Workspaces.qml rewritten; Content.qml passes tabActive (currentTab===3); render_workspaces.py extended (icons stub, tertiary/error palette, anim frame). Renders ws2-{red,teal,red-light,anim}.png OK, only stub Bridge-null warnings.
- NOT verified: hover/press spring, reduced-effects look, real dashboard gating, live data.
