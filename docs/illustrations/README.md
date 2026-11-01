# Illustrations

The images in this folder, the banner in `docs/media/` and the two videos are **motion-design illustrations made in HTML/CSS**. They are not screen captures of a running Winlestia. Layout, spacing, labels and colours follow the v0.9.0 QML sources; app icons are neutral stand-ins and the sample values (name "Alex", weather, readings) are placeholders.

Everything is generated from `docs/media/src/`:

- Stills: `docs/media/src/render.ps1` composes each page from `tokens*.css`, `components.css` and `fragments/*.html`, renders it with headless Microsoft Edge and writes WebP files here (and `banner.webp` in `docs/media/`).
- Videos: `render.ps1 -Shots`, then `python docs/media/src/video/build_scene.py`, then `python docs/media/src/video/capture.py --size 1920x1080 --seconds 28.8 --audio <theme.wav> --out ...`. `capture.py` drives Edge over the DevTools protocol, one frame per `render(t)` call, and encodes with ffmpeg.

Requirements: Windows, Microsoft Edge, ffmpeg in PATH, Python with `websocket-client`. Font: Rubik (OFL), see `THIRD_PARTY_NOTICES.md`.
