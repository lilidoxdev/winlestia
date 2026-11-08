# Composants tiers

- **Caelestia Shell**, caelestia-dots et contributeurs : GPLv3, https://github.com/caelestia-dots/shell (tag v1.4.0 portée, voir `docs/history/caelestia-windows/PORTAGE.md`). Le portage vit dans `src/frontend` (livré dans `Caelestia-Windows/`) ; la correspondance des fichiers est dans `src/frontend/UPSTREAM.json` et `src/frontend/PORT_PATCHES.json`. Ce dépôt ne contient pas de copie de l'amont (pas de dossier `upstream/`) : l'original est sur GitHub. Licence : `LICENSE` et `src/backend/LICENSE-CAELESTIA.txt`.
- **Qt / PySide6 / Shiboken6 6.10.3**, The Qt Company et contributeurs : licences du runtime Qt/PySide, dont LGPLv3/GPLv3 ; notices dans `licenses/` et les métadonnées du runtime `.venv/Lib/site-packages`. Modules dynamiques remplaçables, sans liaison statique. Sources : https://code.qt.io/ et https://download.qt.io/official_releases/QtForPython/ .
- **Rubik**, Google Fonts et auteurs : SIL Open Font License, `assets/fonts/Rubik-OFL.txt`.
- **Material Symbols Rounded**, Google : Apache 2.0, `assets/fonts/MaterialSymbols-LICENSE.txt`.
- **NAudio 2.2.1**, Mark Heath et contributeurs : MIT, `licenses/NAudio-MIT.txt`, https://github.com/naudio/NAudio .
- **LibreHardwareMonitor 0.9.6** : MPL 2.0 et dépendances du paquet NuGet, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor . Le backend conserve ses références de paquets et sources de construction.
- **Clink** (chrisant996/clink), Martin Ridgers, Christopher Antos et contributeurs : GPL-3.0 (texte vérifié dans `src/terminal/clink/LICENSE`), https://github.com/chrisant996/clink . Fichiers fournis dans `src/terminal/clink/` ; le binaire `clink_x64.exe` est ignoré par git et ajouté à la construction.
- **Thèmes Clink** (`src/terminal/clink/themes/`) : `*.clinktheme` et `*.clinkprompt` fournis avec Clink (GPL-3.0, voir ci-dessus). Les palettes `Dracula`, `Solarized Dark/Light` et `Tomorrow*` reprennent des schémas de couleurs tiers (Dracula Theme, Ethan Schoonover, Chris Kempson) ; leur licence propre (MIT) n'est PAS vérifiée par un fichier du dépôt. `agnoster`, `pure`, `starship`, `oh-my-posh`, `jonathan`, `darkblood`, `bureau` reproduisent l'apparence de prompts connus (noms cités pour référence) ; origine exacte non vérifiée.
- **CaskaydiaCove Nerd Font Mono**, sous-ensemble WOFF2 (`src/terminal/clink/CaskaydiaCoveNerdFontMono-Regular-Subset.woff2`) : police dérivée de Cascadia Code (Microsoft) corrigée par le projet Nerd Fonts. Licence attendue : SIL OFL 1.1 ; NON VÉRIFIÉE (aucun fichier de licence dans le dépôt pour cette police).
- **Whisper.net 1.9.1** et **Whisper.net.Runtime 1.9.1**, Sandro Hanea : MIT (métadonnées NuGet et fichier LICENSE du paquet), https://github.com/sandrohanea/whisper.net . Le modèle de dictée est téléchargé à l'exécution depuis https://huggingface.co/ggerganov/whisper.cpp ; sa licence n'a pas été vérifiée ici.
- **Microsoft.Web.WebView2 1.0.4314.35**, Microsoft : licence BSD de Microsoft fournie dans le paquet (`LICENSE.txt`), https://aka.ms/webview . Le runtime WebView2 lui-même est installé par Windows/Microsoft.
- **Microsoft.Data.Sqlite 8.0.11**, Microsoft : MIT (métadonnées NuGet). Dépendances SQLitePCLRaw (Eric Sink) : Apache-2.0 (métadonnées NuGet) ; la bibliothèque SQLite native incluse est dans le domaine public (non vérifié ici).
- **.NET 8 (runtime et bibliothèques, déployés avec les exécutables auto-suffisants)**, .NET Foundation et Microsoft : MIT, https://github.com/dotnet/runtime . Licence non copiée dans ce dépôt.
- **Python 3.12 embarqué** (installeur), Python Software Foundation : PSF License. Non vérifié dans ce dépôt (runtime ajouté à la construction).
- **numpy 2.5.4** : BSD-3-Clause et licences de composants embarqués, `src/frontend/licenses/NumPy/`.
- **Pillow 13.0.0** : licence MIT-CMU/HPND (texte vérifié dans `src/frontend/licenses/Pillow/licenses/LICENSE`).
- **material-color-utilities 0.2.6** : Apache 2.0 (texte vérifié dans `src/frontend/licenses/MaterialColorUtilities/licenses/LICENSE`).
- **PySide6 / Qt** (PySide6, PySide6-Essentials, PySide6-Addons, Shiboken6 6.10.3) : licence LGPL-3.0 / GPL-3.0 au choix, ou licence commerciale Qt (non utilisée) ; textes dans `src/frontend/licenses/` (`LGPL-3.0.txt`, `GPL-3.0.txt`).
- **Fonds d'écran** (`assets/wallpapers/`) : NON couverts par la GPL. Les 10 images fournies par le mainteneur de Winlestia appartiennent à leurs ayants droit respectifs ; leur licence n'est pas établie. Les ayants droit peuvent demander le retrait d'une image via une issue GitHub (voir `assets/wallpapers/CREDITS.txt`).
- **Open-Meteo** : données météo attribuées à Open-Meteo et ses fournisseurs, https://open-meteo.com/ .

**Licence de Winlestia** : GPL-3.0 (`LICENSE`). Les sources correspondant à une version publiée sont celles du tag git de cette version. Les binaires de `src/backend/native/` sont construits à partir de `src/backend/*.cpp` par les scripts `src/backend/build-*.cmd`.

Les sources modifiées du frontend et du backend sont présentes à côté des exécutables. Les anciennes sauvegardes du logiciel sont conservées dans `../Glass-v5/checkpoint-20261025-shell71`.

- **Material Color Utilities 0.2.6**, Ruurd Bijlsma et Google Material Foundation : Apache 2.0, liaison Python/C++ des algorithmes Material You. https://pypi.org/project/material-color-utilities/ ; licences dans `licenses/MaterialColorUtilities/`.
- **NumPy 2.5.4** : BSD et notices des bibliothèques embarquées, `licenses/NumPy/`. https://numpy.org/ .
- **Pillow 13.0.0** : HPND et notices des codecs, `licenses/Pillow/`. https://python-pillow.org/ .
