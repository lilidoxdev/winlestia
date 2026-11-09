"""Construit un ZIP de Winjet reproductible : fichiers a la racine, ordre trie, dates fixes.

Usage : python tools/build_winjet_zip.py <dossier-du-paquet> [<fichier.zip>]
Sans second argument, le ZIP est ecrit a cote du dossier sous le nom <id>-<version>.zip.
Par defaut (sans argument), reconstruit l'exemple docs/winjets/examples/com.exemple.citation.
"""
import json
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT = ROOT / 'docs' / 'winjets' / 'examples' / 'com.exemple.citation'
FIXED_DATE = (2026, 11, 9, 0, 0, 0)


def build(folder: Path, target: Path | None = None) -> Path:
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))
    if target is None:
        target = folder.parent / f"{manifest['id']}-{manifest['version']}.zip"
    files = sorted(p for p in folder.rglob('*') if p.is_file() and p.name != '.gitkeep')
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for path in files:
            info = zipfile.ZipInfo(path.relative_to(folder).as_posix(), FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, path.read_bytes())
    return target


def main(argv):
    folder = Path(argv[1]).resolve() if len(argv) > 1 else DEFAULT
    target = Path(argv[2]).resolve() if len(argv) > 2 else None
    out = build(folder, target)
    print(out, out.stat().st_size, 'octets')


if __name__ == '__main__':
    main(sys.argv)
