import sys
from pathlib import Path

import pytest

from settings import data_dir


def test_env_var_wins(monkeypatch, tmp_path):
    monkeypatch.setenv("HOLO_DATA_DIR", str(tmp_path))
    assert data_dir() == tmp_path


@pytest.mark.parametrize(
    ("platform", "env", "expected"),
    [
        ("darwin", {}, Path.home() / "Library" / "Application Support" / "HoloRepository"),
        ("win32", {"LOCALAPPDATA": "/appdata"}, Path("/appdata") / "HoloRepository"),
        ("linux", {}, Path.home() / ".local" / "share" / "HoloRepository"),
        ("linux", {"XDG_DATA_HOME": "/xdg"}, Path("/xdg") / "HoloRepository"),
    ],
)
def test_default_per_platform(monkeypatch, platform, env, expected):
    for name in ("HOLO_DATA_DIR", "XDG_DATA_HOME"):
        monkeypatch.delenv(name, raising=False)
    for name, value in env.items():
        monkeypatch.setenv(name, value)
    monkeypatch.setattr(sys, "platform", platform)
    assert data_dir() == expected
