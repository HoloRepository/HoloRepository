import logging

import pytest

from log import setup_logging


@pytest.fixture
def clean_root(monkeypatch, tmp_path):
    """Run setup against a temporary data folder and put the loggers back afterwards."""
    monkeypatch.setenv("HOLO_DATA_DIR", str(tmp_path))
    root, uvicorn = logging.getLogger(), logging.getLogger("uvicorn.error")
    saved = root.handlers[:], root.level, uvicorn.handlers[:]
    root.handlers = [h for h in root.handlers if not getattr(h, "holo", False)]
    yield
    for handler in root.handlers:
        if getattr(handler, "holo", False):
            handler.close()
    root.handlers, root.level, uvicorn.handlers = saved


def test_writes_to_file_once(clean_root, tmp_path):
    path = setup_logging()
    setup_logging()  # second call adds nothing
    logging.getLogger("holo.test").warning("hello")
    assert path == tmp_path / "logs" / "server.log"
    assert path.read_text().count("hello") == 1


def test_level_from_env(clean_root, monkeypatch):
    monkeypatch.setenv("HOLO_LOG_LEVEL", "debug")
    setup_logging()
    assert logging.getLogger().level == logging.DEBUG
