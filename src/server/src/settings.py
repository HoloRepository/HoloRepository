import os
import sys
from pathlib import Path


def data_dir() -> Path:
    """Server data folder: $HOLO_DATA_DIR, or the per-user app-data folder."""
    if value := os.environ.get("HOLO_DATA_DIR"):
        return Path(value).expanduser()
    if sys.platform == "darwin":
        base = Path.home() / "Library" / "Application Support"
    elif sys.platform == "win32":
        base = Path(os.environ["LOCALAPPDATA"])
    else:
        base = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
    return base / "HoloRepository"
