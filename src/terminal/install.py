# Python 3.12 / install.py / Windows 11 per-user terminal integration.
from pathlib import Path
import json, os, sys, winreg, subprocess, shutil, ctypes, re, base64

ROOT = Path(__file__).resolve().parent
STATE = Path(os.environ["LOCALAPPDATA"]) / "Winlestia/terminal-backup"
RUN = r"Software\Microsoft\Windows\CurrentVersion\Run"
CMD = r"Software\Microsoft\Command Processor"


def registry(path, name, value=None, remove=False):
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, path) as key:
        if remove:
            try:
                winreg.DeleteValue(key, name)
            except FileNotFoundError:
                pass
        elif value is not None:
            winreg.SetValueEx(key, name, 0, winreg.REG_SZ, value)
        else:
            try:
                return winreg.QueryValueEx(key, name)[0]
            except FileNotFoundError:
                return None


def save(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8-sig")


def policy(restore=False):
    powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    shells = [str(powershell)]
    if shutil.which("pwsh.exe"):
        shells.append(shutil.which("pwsh.exe"))
    for shell in shells:
        state = STATE / ("policy-" + Path(shell).stem + ".json")
        command = [
            shell,
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(ROOT / "Configure-Policy.ps1"),
            "-StatePath",
            str(state),
        ]
        if restore:
            command.append("-Restore")
        subprocess.run(command, check=True, capture_output=True, creationflags=0x08000000)


def read_settings(path):
    text = path.read_text(encoding="utf-8-sig")
    out = []
    i = 0
    quoted = False
    escaped = False
    while i < len(text):
        c = text[i]
        if quoted:
            out.append(c)
            if escaped:
                escaped = False
            elif c == "\\":
                escaped = True
            elif c == '"':
                quoted = False
            i += 1
            continue
        if c == '"':
            quoted = True
            out.append(c)
            i += 1
            continue
        if text[i : i + 2] == "//":
            end = text.find("\n", i)
            i = len(text) if end < 0 else end
            continue
        if text[i : i + 2] == "/*":
            end = text.find("*/", i + 2)
            i = len(text) if end < 0 else end + 2
            continue
        if c == ",":
            j = i + 1
            while j < len(text) and text[j].isspace():
                j += 1
            if j < len(text) and text[j] in "}]":
                i += 1
                continue
        out.append(c)
        i += 1
    return json.loads("".join(out))


def spawn_detached(args):
    # Fully detached: no inherited stdio pipes, own process group, out of the installer's job, so Inno/Start-Process -Wait can finish.
    flags = 0x8 | 0x200
    kw = dict(stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, close_fds=True)
    try:
        return subprocess.Popen(args, creationflags=flags | 0x01000000, **kw)
    except OSError:
        return subprocess.Popen(args, creationflags=flags, **kw)


def stop_previous_sync(command):
    paths = re.findall(r'"([^"\n]+)"', command or "")
    script = next((p for p in paths if Path(p).name.lower() == "sync_theme.py"), None)
    if not script:
        return
    literal = re.sub(
        "(['\u2018\u2019\u201a\u201b])", lambda m: m.group(0) * 2, script
    )  # PowerShell also treats typographic single quotes as delimiters
    code = (
        "$target='"
        + literal
        + "'; Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('python.exe','pythonw.exe') -and $_.CommandLine -and $_.CommandLine.Contains($target) } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Wait-Process -Id $_.ProcessId -Timeout 10 -ErrorAction SilentlyContinue }"
    )
    powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    subprocess.run(
        [
            str(powershell),
            "-NoLogo",
            "-NoProfile",
            "-EncodedCommand",
            base64.b64encode(code.encode("utf-16le")).decode(),
        ],
        capture_output=True,
        creationflags=0x08000000,
        timeout=15,
    )


# Clink (third party, GPLv3) is not shipped in the repository: it is fetched at install time and pinned by hash.
CLINK_VERSION = "1.9.35"
CLINK_TAG = "v" + CLINK_VERSION
CLINK_REPO = "https://github.com/chrisant996/clink"
CLINK_ZIP_NAME = "clink.1.9.35.fa48d0.zip"
CLINK_URL = CLINK_REPO + "/releases/download/" + CLINK_TAG + "/" + CLINK_ZIP_NAME
CLINK_API = "https://api.github.com/repos/chrisant996/clink/releases/tags/" + CLINK_TAG
CLINK_ASSET_RE = re.compile(r"^clink\.1\.9\.35\.[0-9a-f]+\.zip$")
CLINK_ZIP_SHA256 = "5ae6862a703d43ff9250716277fd0811b7b6c344771d17911723e7078d9509f8"  # SHA256 of the official release zip, checked before extraction
CLINK_TIMEOUT = 30
CLINK_DIR = ROOT / "clink"
CLINK_SHA256 = {
    "clink_arm64.exe": "4500e427f846fcc0b7ae6618cbf51894476ee122d5785764e24ed96ddbc2fca7",
    "clink_x64.exe": "ccad4f88e703428e5e9f42334d82830848bc7ac08303136a2df31cc9ecac4a8c",
    "clink_x86.exe": "bb004e73c9b193db8719db055a87758f9f3fa15ca14d52251ec5e1539601747b",
    "clink_dll_arm64.dll": "ac13b9c15b2f2eb9010b6eb23ce26c2dcb57176025f95963986e4e1a4bf2632f",
    "clink_dll_x64.dll": "c35663aa4993d96b0ab397ea5bcf717c85b246103ec174baf3b43e22e66ca34a",
    "clink_dll_x86.dll": "0f62b8a0c3b8132cf2f1e3e39488309b7fbd36a8123209303c15f80e4092ea5f",
}


class ClinkError(Exception):
    pass


def sha256_file(path):
    import hashlib

    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_clink(folder=None):
    folder = Path(folder or CLINK_DIR)
    problems = []
    for name, expected in CLINK_SHA256.items():
        path = folder / name
        if not path.is_file():
            problems.append(name + ": missing")
        elif sha256_file(path) != expected:
            problems.append(name + ": SHA256 mismatch")
    return problems


def clink_manual():
    return (
        "Clink " + CLINK_VERSION + " could not be installed automatically.\n"
        "Manual install:\n"
        " 1. Download " + CLINK_ZIP_NAME + " from " + CLINK_REPO + "/releases/tag/" + CLINK_TAG + "\n"
        " 2. Extract clink_x64.exe, clink_x86.exe, clink_arm64.exe and the three clink_dll_*.dll files into:\n"
        "    " + str(CLINK_DIR) + "\n"
        " 3. Run the Winlestia installer again (or: python install.py)."
    )


def clink_fetch(url, accept=None):
    import urllib.request

    request = urllib.request.Request(url, headers={"User-Agent": "Winlestia-installer", "Accept": accept or "*/*"})
    with urllib.request.urlopen(request, timeout=CLINK_TIMEOUT) as response:
        return response.read()


def clink_asset_url():
    # Fallback when the pinned URL is gone: resolve the asset of the same tag through the GitHub API.
    release = json.loads(clink_fetch(CLINK_API, "application/vnd.github+json").decode("utf-8"))
    for asset in release.get("assets", []):
        if CLINK_ASSET_RE.match(asset.get("name", "")):
            return asset["browser_download_url"]
    raise ClinkError(
        "No asset matching " + CLINK_ASSET_RE.pattern + " in release " + CLINK_TAG + ".\n" + clink_manual()
    )


def clink_extract(archive, target):
    import zipfile

    # Basename only, pinned files only: nothing else from the archive reaches the disk (no zip-slip).
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            name = entry.filename.replace("\\", "/").rsplit("/", 1)[-1]
            if name in CLINK_SHA256 and not entry.is_dir():
                (target / name).write_bytes(bundle.read(entry))


def ensure_clink():
    if not verify_clink():
        return False
    import urllib.error, socket

    staging = ROOT / "clink-download"
    shutil.rmtree(staging, ignore_errors=True)
    staging.mkdir(parents=True)
    try:
        archive = staging / CLINK_ZIP_NAME
        try:
            try:
                data = clink_fetch(CLINK_URL)
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
                data = clink_fetch(clink_asset_url())
        except (urllib.error.URLError, socket.timeout, TimeoutError, ConnectionError, OSError) as error:
            raise ClinkError(
                "Network error while downloading Clink "
                + CLINK_VERSION
                + " ("
                + str(getattr(error, "reason", error))
                + ").\n"
                + clink_manual()
            ) from None
        archive.write_bytes(data)
        actual = sha256_file(archive)
        if CLINK_ZIP_SHA256 and actual.lower() != CLINK_ZIP_SHA256.strip().lower():
            archive.unlink(missing_ok=True)
            raise ClinkError(
                "The downloaded "
                + CLINK_ZIP_NAME
                + " does not match CLINK_ZIP_SHA256 (expected "
                + CLINK_ZIP_SHA256.lower()
                + ", got "
                + actual
                + "). The archive was deleted and nothing was extracted.\n"
                + clink_manual()
            )
        try:
            clink_extract(archive, staging)
        except Exception as error:
            raise ClinkError("Invalid Clink archive (" + str(error) + ").\n" + clink_manual()) from None
        problems = verify_clink(staging)
        if problems:
            raise ClinkError("Clink verification failed: " + "; ".join(problems) + ".\n" + clink_manual())
        CLINK_DIR.mkdir(parents=True, exist_ok=True)
        try:
            for name in CLINK_SHA256:
                os.replace(staging / name, CLINK_DIR / name)
        except OSError as error:
            raise ClinkError(
                "Cannot write Clink into "
                + str(CLINK_DIR)
                + " ("
                + str(error)
                + "). Close every cmd window and retry.\n"
                + clink_manual()
            ) from None
        return True
    finally:
        shutil.rmtree(staging, ignore_errors=True)


ABSENT = {"$absent": True}  # marker: key did not exist before Winlestia
W_DEFAULTS = {
    "colorScheme": "Winlestia Glass",
    "useAcrylic": True,
    "opacity": 65,
    "font": {"face": "Cascadia Code", "size": 12},
}
W_THEME = "Winlestia"
W_NAMES = ("Winlestia", "Winlestia Glass")


def injection():
    return '"' + str(ROOT / "clink/clink_x64.exe") + '" inject --quiet --profile "' + str(ROOT / "clink-profile") + '"'


def autorun_value(old):
    inj = injection()
    return old if inj in old else (inj if "clink" in old.lower() or not old else old + " & " + inj)


def run_command():
    return (
        '"'
        + str(ROOT.parent / "Caelestia-Windows/.venv/Scripts/pythonw.exe")
        + '" "'
        + str(ROOT / "sync_theme.py")
        + '"'
    )


def write_atomic(path, text):
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(text, encoding="utf-8-sig")
    os.replace(temporary, path)


def wt_entry(path, data, template):
    # What the settings looked like before Winlestia touched them, and what we write.
    profiles = data.get("profiles") if isinstance(data.get("profiles"), dict) else {}
    defaults = profiles.get("defaults") if isinstance(profiles.get("defaults"), dict) else {}
    entry = {
        "path": str(path),
        "had": {
            "profiles": "profiles" in data,
            "defaults": "defaults" in profiles,
            "schemes": "schemes" in data,
            "themes": "themes" in data,
        },
        "original": {"defaults": {k: defaults.get(k, ABSENT) for k in W_DEFAULTS}, "theme": data.get("theme", ABSENT)},
        "written": {"defaults": json.loads(json.dumps(W_DEFAULTS)), "theme": W_THEME},
        "added": {},
        "preexisting": {},
    }
    for key in ("schemes", "themes"):
        current = {x.get("name"): x for x in data.get(key, []) if isinstance(x, dict)}
        names = [i["name"] for i in template.get(key, []) if i.get("name") in W_NAMES]
        entry["added"][key] = [n for n in names if n not in current]
        entry["preexisting"][key] = [current[n] for n in names if n in current]
    return entry


def note_written(package, **values):
    # sync_theme.py reports the appearance values it keeps rewriting, so restore() still recognises them as ours.
    path = STATE / "state.json"
    try:
        if not path.exists():
            return  # never create state.json: its absence means restored or not integrated
        state = json.loads(path.read_text(encoding="utf-8-sig"))
        if state.get("version") != 2:
            return
        state["wt"][package]["written"]["defaults"].update(values)
        temporary = path.with_name(path.name + ".tmp")
        temporary.write_text(json.dumps(state, indent=2), encoding="utf-8-sig")
        if path.exists():
            os.replace(temporary, path)
        else:
            temporary.unlink(missing_ok=True)
    except (OSError, ValueError, KeyError, TypeError):
        pass


def restore_settings(path, entry):
    try:
        data = read_settings(path)
    except (OSError, ValueError):
        return
    before = json.dumps(data, sort_keys=True)
    for key in ("schemes", "themes"):
        if not isinstance(data.get(key), list):
            continue
        ours = entry["added"].get(key, []) + [x.get("name") for x in entry["preexisting"].get(key, [])]
        data[key] = [x for x in data[key] if not (isinstance(x, dict) and x.get("name") in ours)] + entry[
            "preexisting"
        ].get(key, [])
        if not data[key] and not entry["had"].get(key, True):
            del data[key]
    profiles = data.get("profiles") if isinstance(data.get("profiles"), dict) else {}
    defaults = profiles.get("defaults") if isinstance(profiles.get("defaults"), dict) else None
    original = entry.get("original")
    if original and defaults is not None:
        for key, written in entry["written"]["defaults"].items():
            if key in defaults and defaults[key] == written:
                if original["defaults"].get(key) == ABSENT:
                    del defaults[key]
                else:
                    defaults[key] = original["defaults"][key]
        if not defaults and not entry["had"].get("defaults", True):
            del profiles["defaults"]
        if "profiles" in data and not profiles and not entry["had"].get("profiles", True):
            del data["profiles"]
    if original and data.get("theme") == entry["written"]["theme"]:
        if original["theme"] == ABSENT:
            del data["theme"]
        else:
            data["theme"] = original["theme"]
    if json.dumps(data, sort_keys=True) != before:
        write_atomic(path, json.dumps(data, indent=2, ensure_ascii=False))


def v1_wt():
    # State written before the manifest existed: originals come from the single backup copy, ours are the known names/values.
    # Callers that restore without migrating must trim 'written' to colorScheme/font (sync may have changed opacity/useAcrylic).
    template = json.loads((ROOT / "theme-template.json").read_text(encoding="utf-8-sig"))
    out = {}
    for backup in STATE.glob("Microsoft.WindowsTerminal*.json"):
        path = Path(os.environ["LOCALAPPDATA"]) / "Packages" / backup.stem / "LocalState/settings.json"
        try:
            entry = wt_entry(path, read_settings(backup), template)
        except (OSError, ValueError):
            entry = wt_entry(path, {}, template)
            entry["original"] = None
        out[backup.stem] = entry
    return out


def configure():
    STATE.mkdir(parents=True, exist_ok=True)
    ensure_clink()
    policy()
    for d in ("backups", "validation"):
        (ROOT / d).mkdir(exist_ok=True)
    for p in ROOT.rglob("*"):
        if p.suffix not in (".ps1", ".lua", ".cmd"):
            continue
        text = p.read_text(encoding="utf-8-sig")
        text = text.replace("__WINLESTIA_TERMINAL_FWD__", ROOT.as_posix()).replace(
            "__WINLESTIA_TERMINAL__", str(ROOT).replace("'", "''") if p.suffix == ".ps1" else str(ROOT)
        )
        p.write_text(text, encoding="utf-8-sig" if p.suffix == ".ps1" else "utf-8")
    # Merge appearance into existing settings; never replace shortcuts, profiles or unrelated themes.
    template = json.loads((ROOT / "theme-template.json").read_text(encoding="utf-8-sig"))
    settings_paths = list(
        (Path(os.environ["LOCALAPPDATA"]) / "Packages").glob("Microsoft.WindowsTerminal*/LocalState/settings.json")
    )
    snapshot = STATE / "state.json"
    state = json.loads(snapshot.read_text(encoding="utf-8-sig")) if snapshot.exists() else None
    if state is not None and state.get("version") != 2:
        # Migrate a v1 state to the v2 manifest, keeping the v1 originals (taken from the backups, before we touch settings again).
        state["wt"] = v1_wt()
        state["version"] = 2
        save(snapshot, json.dumps(state, indent=2))
    v2 = True
    wt = {}
    for path in settings_paths:
        data = read_settings(path)
        backup = STATE / (path.parent.parent.name + ".json")
        if not backup.exists():
            shutil.copy2(path, backup)
        if v2 and path.parent.parent.name not in (state or {}).get("wt", {}):
            wt[path.parent.parent.name] = wt_entry(path, data, template)
        for key in ("schemes", "themes"):
            data.setdefault(key, [])
            for item in template.get(key, []):
                if item.get("name") not in ("Winlestia", "Winlestia Glass"):
                    continue
                data[key] = [x for x in data[key] if x.get("name") != item["name"]] + [item]
        defaults = data.setdefault("profiles", {}).setdefault("defaults", {})
        defaults.update(json.loads(json.dumps(W_DEFAULTS)))
        data["theme"] = W_THEME
        save(path, json.dumps(data, indent=2, ensure_ascii=False))
    try:
        with winreg.OpenKey(
            winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
        ) as key:
            documents = Path(os.path.expandvars(winreg.QueryValueEx(key, "Personal")[0]))
    except OSError:
        documents = Path.home() / "Documents"
    block = (
        "\n# >>> Winlestia Terminal >>>\n. '"
        + str(ROOT / "Profile.ps1").replace("'", "''")
        + "'\n# <<< Winlestia Terminal <<<\n"
    )
    profiles = []
    for shell in ("WindowsPowerShell", "PowerShell"):
        path = documents / shell / "profile.ps1"
        old = path.read_text(encoding="utf-8-sig") if path.exists() else ""
        profiles.append({"path": str(path), "before": old, "existed": path.exists()})
        import re

        save(
            path,
            re.sub(r"\n?# >>> Winlestia Terminal >>>.*?# <<< Winlestia Terminal <<<\n?", "", old, flags=re.S) + block,
        )
    if state is None:
        save(
            snapshot,
            json.dumps(
                {
                    "version": 2,
                    "profiles": profiles,
                    "autorun": registry(CMD, "AutoRun"),
                    "sync": registry(RUN, "WinlestiaTerminalTheme"),
                    "wt": wt,
                },
                indent=2,
            ),
        )
    current = registry(CMD, "AutoRun") or ""
    new = autorun_value(current)
    registry(CMD, "AutoRun", new)
    python = ROOT.parent / "Caelestia-Windows/.venv/Scripts/pythonw.exe"
    stop_previous_sync(registry(RUN, "WinlestiaTerminalTheme"))
    registry(RUN, "WinlestiaTerminalTheme", run_command())
    if v2:
        state = json.loads(snapshot.read_text(encoding="utf-8-sig"))
        state.setdefault("wt", {}).update(wt)
        state.update(version=2, sync_written=run_command())
        # Record the AutoRun we wrote only when we really changed it, and never replace it on upgrade (it would capture later user additions).
        if new != current and not state.get("autorun_written"):
            state["autorun_written"] = new
        save(snapshot, json.dumps(state, indent=2))
    (ROOT / "theme-sync.stop").unlink(missing_ok=True)
    initial_sync = subprocess.run(
        [str(python.with_name("python.exe")), str(ROOT / "sync_theme.py"), "--once"],
        capture_output=True,
        timeout=45,
        creationflags=0x08000000,
    )
    spawn_detached([str(python), str(ROOT / "sync_theme.py")])
    # Windows Terminal's packaged registry view needs the same registration.
    wt = shutil.which("wt.exe")
    if wt:
        spawn_detached(
            [
                wt,
                "-w",
                "new",
                "powershell.exe",
                "-NoLogo",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(ROOT / "ConfigurePackagedCmd.ps1"),
            ]
        )
    save(
        ROOT / "install-result.json",
        json.dumps({"ok": True, "profiles": len(profiles), "terminalSettings": len(settings_paths)}),
    )


def restore():
    path = STATE / "state.json"
    if not path.exists():
        return
    (ROOT / "theme-sync.stop").touch()
    # Stop the running sync for real, then take the manifest out of the way so a late sync write cannot see or recreate state.json.
    try:
        stop_previous_sync(registry(RUN, "WinlestiaTerminalTheme") or run_command())
    except (OSError, subprocess.SubprocessError):
        pass
    restored = STATE / "state.restored.json"
    os.replace(path, restored)
    data = read_settings(restored)
    import re

    for item in data["profiles"]:
        p = Path(item["path"])
        if p.exists():
            save(
                p,
                re.sub(
                    r"\n?# >>> Winlestia Terminal >>>.*?# <<< Winlestia Terminal <<<\n?",
                    "",
                    p.read_text(encoding="utf-8-sig"),
                    flags=re.S,
                ),
            )
    # Only undo what is still ours: a value the user changed since the install is left alone.
    current = registry(CMD, "AutoRun") or ""
    original = data["autorun"] or ""
    inj = injection()
    if current and current in (data.get("autorun_written"), autorun_value(original), inj):
        registry(CMD, "AutoRun", original, remove=not original)
    elif inj in current:
        rest = (
            current.replace(" & " + inj, "", 1)
            if " & " + inj in current
            else current.replace(inj + " & ", "", 1)
            if inj + " & " in current
            else current.replace(inj, "", 1)
        )
        registry(CMD, "AutoRun", rest.strip(), remove=not rest.strip())
    if registry(RUN, "WinlestiaTerminalTheme") == (data.get("sync_written") or run_command()):
        registry(RUN, "WinlestiaTerminalTheme", data["sync"], remove=data["sync"] is None)
    if data.get("version") == 2:
        entries = data["wt"].values()
    else:
        entries = list(v1_wt().values())
        for entry in entries:
            entry["written"]["defaults"] = {
                k: v for k, v in entry["written"]["defaults"].items() if k in ("colorScheme", "font")
            }
    for entry in entries:
        if Path(entry["path"]).exists():
            restore_settings(Path(entry["path"]), entry)
    try:
        policy(restore=True)
    except (OSError, subprocess.CalledProcessError):
        pass


if __name__ == "__main__":
    try:
        restore() if "--restore" in sys.argv else configure()
    except ClinkError as error:
        save(ROOT / "install-result.json", json.dumps({"ok": False, "error": str(error), "details": "clink"}))
        print(str(error), file=sys.stderr)
        sys.exit(2)
    except Exception as error:
        save(
            ROOT / "install-result.json",
            json.dumps(
                {
                    "ok": False,
                    "error": str(error),
                    "details": getattr(error, "stderr", b"").decode("utf-8", errors="replace")
                    if isinstance(getattr(error, "stderr", None), bytes)
                    else str(getattr(error, "stderr", "")),
                }
            ),
        )
        raise
