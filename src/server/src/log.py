"""Server logging: console plus a rotating file in $HOLO_DATA_DIR/logs/ (decided 2026-11-27).

Clients run an installed server with no terminal, so the file is how a traceback gets back to us.
Modules log with `logging.getLogger(__name__)`; level from HOLO_LOG_LEVEL (default INFO).
"""

import logging
import os
from logging.handlers import RotatingFileHandler
from pathlib import Path

from settings import data_dir

FORMAT = "%(asctime)s %(levelname)-8s %(name)s: %(message)s"
MAX_BYTES = 10 * 1024**2
BACKUPS = 5


def log_file() -> Path:
    return data_dir() / "logs" / "server.log"


def setup_logging() -> Path:
    """Configure the root logger once; safe to call again. Returns the log file's path."""
    path = log_file()
    root = logging.getLogger()
    level = os.environ.get("HOLO_LOG_LEVEL", "INFO").upper()
    root.setLevel(logging.getLevelNamesMapping().get(level, logging.INFO))
    if any(getattr(h, "holo", False) for h in root.handlers):
        return path

    path.parent.mkdir(parents=True, exist_ok=True)
    file = RotatingFileHandler(path, maxBytes=MAX_BYTES, backupCount=BACKUPS, encoding="utf-8")
    console = logging.StreamHandler()
    for handler in (file, console):
        handler.setFormatter(logging.Formatter(FORMAT))
        handler.holo = True  # type: ignore[attr-defined]
        root.addHandler(handler)
    # Uvicorn's own loggers don't reach the root; send its errors (unhandled route exceptions) to the file too.
    logging.getLogger("uvicorn.error").addHandler(file)
    return path
