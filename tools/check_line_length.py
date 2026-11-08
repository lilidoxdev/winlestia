"""Echoue si une ligne depasse la limite de .editorconfig (140 colonnes) dans le code source suivi par git.

Perimetre : src/**/*.cs, src/frontend/**/*.py|*.qml, src/terminal/**/*.py.
Exclus : src/terminal/clink, src/backend/VsCodeTheme*, fichiers generes (*.g.cs, *.Designer.cs, obj/, bin/).
Usage : python tools/check_line_length.py [--max 140]
"""
from __future__ import annotations

import argparse
import fnmatch
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INCLUDE = ("src/**/*.cs", "src/frontend/**/*.py", "src/frontend/**/*.qml", "src/terminal/**/*.py")
EXCLUDE = ("src/terminal/clink/*", "src/backend/VsCodeTheme*", "*.g.cs", "*.Designer.cs", "*/obj/*", "*/bin/*")


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z", "--", "src"], cwd=ROOT, check=True,
                         capture_output=True, text=True, encoding="utf-8").stdout
    return [p for p in out.split("\0") if p]


def in_scope(path: str) -> bool:
    # fnmatch : '*' traverse les '/', donc 'src/*.cs' couvre tous les sous-dossiers.
    included = any(fnmatch.fnmatch(path, g.replace("**/", "")) for g in INCLUDE)
    return included and not any(fnmatch.fnmatch(path, g) for g in EXCLUDE)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--max", type=int, default=140)
    args = parser.parse_args()
    bad: list[tuple[str, int, int]] = []
    for rel in tracked_files():
        if not in_scope(rel) or not (ROOT / rel).is_file():
            continue
        text = (ROOT / rel).read_text(encoding="utf-8-sig", errors="replace")
        for number, line in enumerate(text.splitlines(), 1):
            if len(line) > args.max:
                bad.append((rel, number, len(line)))
    for rel, number, length in bad:
        print(f"{rel}:{number}: {length} > {args.max}")
    print(f"{len(bad)} ligne(s) au-dela de {args.max} colonnes.")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
