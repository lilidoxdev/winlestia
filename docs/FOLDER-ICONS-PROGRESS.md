# Folder icons: stable path scheme (progress)

- Icons now live at `%LOCALAPPDATA%\Winlestia\Explorer\icons\<kind>-current.ico` (folder, desktop, download, document, picture, music, video); `icons\state.json` stores the current accent and the Migrated flag.
- Palette change: Sync arms a 1 s debounced regeneration (atomic temp + replace, off the UI thread, skipped if accent unchanged), then one SHCNE_ASSOCCHANGED and SHCNE_UPDATEITEM per manifest folder (64 per 250 ms; 32 per 500 ms with reduced effects). No desktop.ini rewrite.
- Migration: one background pass (MigrateAsync) rewrites manifest inis that point to legacy `<hex>-<kind>-v2.ico`; skipped when ini hash != AppliedHash; then unreferenced legacy icons are deleted.
- Junctions/reparse points stay skipped (their target is themed when visited).
- ColourVisibleFolder(path): thread safe, 3 s per-folder debounce, 12 calls / 10 s budget (not wired to Files yet).
- Shell cache measurement: a fresh process sees the new colour at the same path immediately; a warm process (proxy for explorer.exe) did not refresh in 9 s in ANY case, including the alternate-path control, so the proxy is inconclusive for explorer.exe. To verify manually after redeploy.
- Tests: `tests/FolderIcons` (18/18), `tests/Explorer077` (36/36).
