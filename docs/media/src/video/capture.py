"""Winlestia promo video - deterministic frame capture (Edge headless + CDP) and ffmpeg encode.

Every frame calls window.render(t) in scene.html, then Page.captureScreenshot.
Usage:  python capture.py [--seconds 5] [--fps 30] [--size 1080x1920] [--out ..\\preview-9x16-5s.mp4]
                         [--still 2.4 --still-out ..\\preview-still.png] [--keep]
Needs: Microsoft Edge, ffmpeg in PATH, `pip install websocket-client`.
"""
import argparse, multiprocessing, base64, json, os, shutil, socket, subprocess, sys, tempfile, time, urllib.request
from pathlib import Path
import websocket

HERE = Path(__file__).resolve().parent


def find_edge():
    for var in ("ProgramFiles(x86)", "ProgramFiles"):
        base = os.environ.get(var)
        if base:
            p = Path(base) / "Microsoft/Edge/Application/msedge.exe"
            if p.exists():
                return str(p)
    sys.exit("Microsoft Edge not found")


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class CDP:
    def __init__(self, url):
        self.ws = websocket.create_connection(url, max_size=None, timeout=60)
        self.i = 0

    def call(self, method, **params):
        self.i += 1
        self.ws.send(json.dumps({"id": self.i, "method": method, "params": params}))
        while True:
            msg = json.loads(self.ws.recv())
            if msg.get("id") == self.i:
                if "error" in msg:
                    raise RuntimeError(f"{method}: {msg['error']}")
                return msg.get("result", {})

    def js(self, expr):
        r = self.call("Runtime.evaluate", expression=expr, returnByValue=True, awaitPromise=True)
        return r.get("result", {}).get("value")


def session(w, h, tmp, tag):
    port = free_port()
    edge = subprocess.Popen([find_edge(), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--mute-audio", "--log-level=3",
                             "--allow-file-access-from-files", f"--remote-debugging-port={port}",
                             f"--user-data-dir={tmp / ('profile' + tag)}", f"--window-size={w},{h}", "about:blank"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    ws_url = None
    for _ in range(150):
        try:
            tabs = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json"))
            ws_url = next(t["webSocketDebuggerUrl"] for t in tabs if t["type"] == "page"); break
        except Exception:
            time.sleep(.2)
    if not ws_url:
        edge.terminate(); sys.exit("cannot reach Edge DevTools")
    c = CDP(ws_url)
    c.call("Page.enable")
    c.call("Emulation.setDeviceMetricsOverride", width=w, height=h, deviceScaleFactor=1, mobile=False)
    c.call("Page.navigate", url=(HERE / "scene.html").as_uri())
    for _ in range(300):
        if c.js("window.READY===true"):
            break
        time.sleep(.2)
    else:
        edge.terminate(); sys.exit("scene not ready")

    def shot(t, path):
        c.js(f"render({t})")
        data = c.call("Page.captureScreenshot", format="png", fromSurface=True)["data"]
        Path(path).write_bytes(base64.b64decode(data))
    return edge, shot


def worker(k, nw, n, fps, w, h, tmp, frames):
    edge, shot = session(w, h, Path(tmp), str(k))
    try:
        for i in range(k, n, nw):
            shot(i / fps, Path(frames) / f"f{i:04d}.png")
    finally:
        edge.terminate()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=float, default=5)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--size", default="1080x1920")
    ap.add_argument("--out", default=str(HERE.parent.parent / "preview-9x16-5s.mp4"))
    ap.add_argument("--crf", default="20")
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--still", "--poster", dest="still", type=float, default=None)
    ap.add_argument("--still-out", "--poster-out", dest="still_out", default=str(HERE.parent.parent / "preview-still.png"))
    ap.add_argument("--only", default=None, help="comma list of times (s) to dump as PNG into WL_DEBUG_DIR, no video")
    ap.add_argument("--frames-dir", default=None, help="reuse an existing frames dir (encode only, kept)")
    ap.add_argument("--keep", action="store_true")
    a = ap.parse_args()
    w, h = map(int, a.size.lower().split("x"))
    tmp = Path(tempfile.mkdtemp(prefix="winlestia-video-"))
    frames = Path(a.frames_dir) if a.frames_dir else tmp / "frames"; frames.mkdir(exist_ok=True)
    try:
        if a.only:
            edge, shot = session(w, h, tmp, "o")
            try:
                for t in a.only.split(","):
                    shot(float(t), frames / f"t{float(t):05.2f}.png")
            finally:
                edge.terminate()
            out = Path(os.environ.get("WL_DEBUG_DIR", Path(tempfile.gettempdir()) / "winlestia-frames-debug")); out.mkdir(parents=True, exist_ok=True)
            for f in frames.glob("t*.png"):
                shutil.copy(f, out / f.name)
            print("dumped to", out); return
        n = round(a.seconds * a.fps)
        if not a.frames_dir:
            t0 = time.time()
            ps = [multiprocessing.Process(target=worker, args=(k, a.workers, n, a.fps, w, h, str(tmp), str(frames))) for k in range(a.workers)]
            [p.start() for p in ps]; [p.join() for p in ps]
            got = len(list(frames.glob("f*.png")))
            print(f"captured {got}/{n} frames in {time.time() - t0:.1f}s")
            if got != n:
                sys.exit("missing frames")
        if a.still is not None:
            edge, shot = session(w, h, tmp, "s")
            try:
                shot(a.still, a.still_out); print("still", a.still_out)
            finally:
                edge.terminate()
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-framerate", str(a.fps), "-i", str(frames / "f%04d.png"),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", str(a.crf), "-movflags", "+faststart", "-an", a.out], check=True)
        print("wrote", a.out)
    finally:
        time.sleep(.5)
        if not a.keep and not a.frames_dir:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
