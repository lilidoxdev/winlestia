# Python 3.12 / wallpaper_frames.py / Windows. Cache d'images représentatives d'un fond animé (images extraites via le
# backend natif ou mpv). Séparé de wallpaper_sources.py pour garder chaque fichier lisible ; les noms restent accessibles via
# wallpaper_sources (voir son __getattr__), donc les appelants existants ne changent pas.
from pathlib import Path
import os, shutil, subprocess, time

from wallpaper_sources import CACHE_LIMIT, EXTRACT_TIMEOUT, FRAME_COUNT, FRAME_SIZE, _mtime, _sha, default_bases


def cache_root(bases=None):
    return Path((bases or default_bases())['localappdata']) / 'Winlestia' / 'wallpaper-cache'


def backend_exe():
    override = os.environ.get('WINLESTIA_BACKEND_EXE')
    if override and os.path.isfile(override):
        return override
    here = Path(__file__).resolve().parent
    for base in (here, here.parent, here.parent.parent):
        for sub in ('', 'Glass-v5'):
            p = base / sub / 'Panneaux.Glass.exe'
            if p.is_file():
                return str(p)
    return None


def prune_cache(root, limit=CACHE_LIMIT):
    try:
        entries = sorted((p for p in Path(root).iterdir() if p.is_dir()), key=lambda p: p.stat().st_mtime)
        for p in entries[: max(0, len(entries) - limit)]:
            shutil.rmtree(p, ignore_errors=True)
    except OSError:
        # Nettoyage au mieux du cache : il sera refait au prochain passage.
        pass


def find_mpv():
    # Décodeur de dernier recours pour les codecs que Media Foundation n'a pas (HEVC sans l'extension du Store).
    # Uniquement un mpv déjà installé sur la machine ; rien n'est téléchargé.
    override = os.environ.get('WINLESTIA_MPV')
    if override and os.path.isfile(override):
        return override
    found = shutil.which('mpv')
    if found:
        return found
    # ne jamais emprunter le mpv d'une application de fond du Store : son dossier de paquet refuse l'exécution (accès
    # refusé) et ce n'est pas à nous de le lancer
    return None


def _extract_mpv(video, out_dir, deadline, mpv):
    # 8 processus mpv de courte durée en parallèle, chacun décodant une image à k % du fichier (sortie image, sans
    # fenêtre ni audio).
    processes = []
    for i in range(FRAME_COUNT):
        tmp = out_dir / f't{i}'
        tmp.mkdir(parents=True, exist_ok=True)
        command = [
            mpv,
            '--no-config',
            '--no-audio',
            '--no-terminal',
            '--really-quiet',
            '--no-sub',
            '--vo=image',
            '--vo-image-format=jpg',
            '--vo-image-jpeg-quality=85',
            f'--vo-image-outdir={tmp}',
            f'--start={(i + 0.5) * 100 / FRAME_COUNT:.2f}%',
            '--frames=1',
            f'--vf=scale=w={FRAME_SIZE}:h={FRAME_SIZE}:force_original_aspect_ratio=decrease',
            video,
        ]
        try:
            processes.append(
                (
                    i,
                    tmp,
                    subprocess.Popen(
                        command,
                        stdin=subprocess.DEVNULL,
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                        creationflags=0x08000000 | 0x00004000,
                    ),
                )
            )  # sans fenêtre, priorité inférieure à la normale
        except OSError:
            # Lancement impossible : pas de miniature pour cette vidéo.
            pass
    for i, tmp, process in processes:
        try:
            process.wait(timeout=max(0.1, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            process.kill()
    for i, tmp, process in processes:
        shots = sorted(tmp.glob('*.jpg'))
        if shots:
            try:
                os.replace(shots[0], out_dir / f'f{i}.jpg')
            except OSError:
                # Image disparue entre la capture et le déplacement : ignorée.
                pass
        shutil.rmtree(tmp, ignore_errors=True)


def extract_frames(video, out_dir, exe=None, timeout=EXTRACT_TIMEOUT, rect=None):
    # Niveau 1 : Media Foundation via l'exécutable du backend (sous-processus tuable). Niveau 2 : un mpv installé.
    # Budget total strict = timeout.
    started = time.monotonic()
    deadline = started + timeout
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    exe = exe or backend_exe()
    if exe:
        try:
            process = subprocess.Popen(
                [exe, '--extract-frames', video, str(out_dir), str(FRAME_COUNT), str(FRAME_SIZE)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=0x08000000,
            )
            try:
                process.wait(timeout=min(4.5, timeout))
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(2)
        except OSError:
            # Processus déjà terminé ou inaccessible : rien à tuer.
            pass
    frames = sorted(str(p) for p in out_dir.glob('f*.jpg') if p.stat().st_size > 0)
    if len(frames) < 2 and exe and time.monotonic() < deadline - 1.5:
        # Niveau 2 : Windows.Graphics.Capture de l'hôte animé (la vidéo est lue à l'écran). Atteint seulement lors
        # d'un changement de fond, résultat mis en cache par clé.
        for p in out_dir.glob('f*.jpg'):
            p.unlink(missing_ok=True)
        remaining = deadline - time.monotonic()
        try:
            process = subprocess.Popen(
                [
                    exe,
                    '--capture-frames',
                    str(out_dir),
                    str(FRAME_COUNT),
                    str(FRAME_SIZE),
                    '400',
                    str(int((remaining - 0.4) * 1000)),
                ]
                + ([str(v) for v in rect] if rect else []),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=0x08000000,
            )
            try:
                process.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(2)
        except OSError:
            # Processus déjà terminé ou inaccessible : rien à tuer.
            pass
        frames = sorted(str(p) for p in out_dir.glob('f*.jpg') if p.stat().st_size > 0)
    if len(frames) < 2 and time.monotonic() < deadline - 0.5:
        mpv = find_mpv()
        if mpv:
            for p in out_dir.glob('f*.jpg'):
                p.unlink(missing_ok=True)
            _extract_mpv(video, out_dir, deadline, mpv)
            frames = sorted(str(p) for p in out_dir.glob('f*.jpg') if p.stat().st_size > 0)
    return frames


def representative_images(source, bases=None, exe=None, timeout=EXTRACT_TIMEOUT):
    # Image fixe -> ce fichier. Vidéo -> ~8 images en cache (réutilisées). Repli sur la vignette ou les images. Ne
    # lève jamais.
    try:
        if source.video and os.path.isfile(source.video):
            root = cache_root(bases)
            folder = root / _sha(source.key + str(_mtime(source.video)))
            frames = sorted(str(p) for p in folder.glob('f*.jpg')) if folder.is_dir() else []
            if len(frames) < FRAME_COUNT // 2:
                shutil.rmtree(folder, ignore_errors=True)
                frames = extract_frames(source.video, folder, exe, timeout, source.rect)
                if len(frames) < 2:
                    shutil.rmtree(folder, ignore_errors=True)
                    frames = []
            if frames:
                try:
                    os.utime(folder, None)
                except OSError:
                    # Date du dossier facultative : le cache reste valide.
                    pass
                prune_cache(root)
                return frames
        return [p for p in source.images if p and os.path.isfile(p)] or ([source.thumbnail] if source.thumbnail else [])
    except Exception:
        return [p for p in source.images if p]
