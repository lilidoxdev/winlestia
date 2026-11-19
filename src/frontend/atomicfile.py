# Crash-safe replacement of durable user data: write a unique temp file in the same dir, fsync, then os.replace. The old file stays
# intact on any failure.
import os, tempfile
from pathlib import Path


def write_text(path, text, encoding="utf-8"):
    write_bytes(path, text.encode(encoding))


def write_bytes(path, data):
    path = Path(path)
    fd, t = tempfile.mkstemp(dir=path.parent, prefix=path.name + ".", suffix=".tmp")
    tmp = Path(t)
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
    except BaseException:
        try:
            tmp.unlink()
        except OSError:
            pass
        raise
