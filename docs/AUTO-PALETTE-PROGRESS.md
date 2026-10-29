# Auto palette progress (DONE, not deployed)
- chromatic_palette.py rewritten: analyze_images(paths,key) -> seeds/lightness/colorfulness; palette_from_images(); generate_palette() kept as wrapper.
- Pipeline: pool pixels (<=96px per frame) -> 5-bit histogram -> deterministic weighted k-means (24, OKLab) -> HCT per cluster -> MCU-style score + 15deg hue dedup -> seed gets chroma of its hue family (dark bulk is low chroma, accents lift it).
- Scheme: own MD3 tonal assembly (primary C boosted from seed; secondary same hue lower C; tertiary = 2nd seed if >=40deg else hue+60; neutral tinted by primary), tones MD3 (dark surface 6, containers 4/10/12/17/22), contrast guard.
- Light/dark from area-weighted L* with hysteresis (>=62 light, <=50 dark, band keeps previous, default dark). frontend.py `dark` property now follows theme in auto mode.
- Tests: src/frontend/tests/test_chromatic_palette.py (11 OK). Timing 8x320px: 10.8 ms median; 1 image 4.5 ms.
- Sheets in scratchpad: palette-<case>.png. Not verified: live shell with light auto palette, glass over light wallpapers.
