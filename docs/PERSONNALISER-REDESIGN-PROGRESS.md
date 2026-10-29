# Personnaliser redesign - progress

Files: src/frontend/qml/ShellPreferences.qml (rewritten), src/frontend/qml/Main.qml (settings panel width 690 -> min(900, screenWidth-80)), tests/prefs_shots.py (offscreen screenshots, 60 s hard timeout).

## Inventory (before = after)
Bridge key / slot, old location -> new page.
1. theme (4 cards light/dark/custom/auto) + custom editor, Apparence -> Apparence / Theme
2. Modifier ma palette (custom), Apparence -> Apparence / Theme
3. Wallpaper palette dots (auto, display) -> Apparence / Theme
4. glassOpacity slider -> Apparence / Rendu (now always visible, disabled outside Automatique)
5. reducedEffects ("Effets allégés") -> Apparence / Rendu
6. accentBorders (Liseré) -> Apparence / Rendu (moved from Explorateur)
7. explorerTheme -> Explorateur et fichiers / Explorateur Windows
8. filesDefaultHandler -> Explorateur et fichiers / Winlestia Fichiers
9. filesPreload -> Explorateur et fichiers / Winlestia Fichiers
10. explorerFolder (Ajouter un dossier), 11. explorerOpen, 12. explorerRestore -> Explorateur Windows
    (the 4+ unlabeled icon buttons were decorative tiles, not controls: kept as a preview strip tied to "Icônes et contours assortis")
13. explorerStatus text + adaptive-icons note -> Explorateur Windows
14. autoTile -> Bureau et dock / Fenêtres et écran
15. desktopLogo -> idem
16. screenFrame -> idem
17. frameThickness slider -> idem (dimmed when frame off)
18. taskbar -> Bureau et dock / Dock et accès rapide
19. iconSize slider -> idem
20. Zone de détection + Calibrer (DetectionGuide) -> idem
21. setLanguage x4 (auto/fr/en/es) -> Au quotidien / Langue et services
22. shortcut panel (recorder) 23. paletteShortcut (recorder) -> Au quotidien / Raccourcis
24. autostart 25. clipboardHistory -> Au quotidien / Démarrage et données
26. chooseCity 27. refreshCatalogue (was an unlabeled refresh icon) -> Langue et services
28. restoreTaskbar, 29. navigate("dashboard") -> footer; 30. closePanel -> X (+ Esc via Main.qml Shortcut)
New: 31. motion (Animations Sans/Rapides/Caelestia = 0/.5/1; existing backend key "motion"/settings.motionScale, no backend change).
Count: 30 before, 31 after (the extra one is the Animations control; nothing removed).

## Structure
Header (title, subtitle, X), 4 tabs (Apparence, Bureau et dock, Explorateur et fichiers, Au quotidien), page = 2-column grid of cards (icon, title, subtitle; rows with label + explanation), footer (Rétablir la barre Windows, Tableau de bord).
Pages load through a Loader (only the current one, only while visible); a page scrolls inside itself only if the screen is too small. Panel height min(640, screenHeight-100); fits at 1366x768.
Keyboard: tabs, theme cards, chips are focusable (Tab, Enter/Space, Left/Right on tabs) with focus ring; switches/sliders are Qt Controls (Tab) and their row shows a ring.
Colours: palette tokens only; the only literals are the fixed Porcelaine/Nuit swatch colours (they depict themes independent of the live palette).

## Verification
tests/prefs_shots.py OUTDIR PREFIX WxH pages (python from installed frontend venv, preview bridge on its own pipe). Zero QML warnings (frontend QML_ISSUES empty). Bridge names grep-checked in QtShellBridge.cs / frontend.py.
