import pytest
import torch
from safetensors.torch import load_file, save_file

from segmentation import weights


@pytest.fixture
def folder(monkeypatch, tmp_path):
    monkeypatch.setenv("HOLO_DATA_DIR", str(tmp_path))
    return weights.weights_dir()


def write_file(folder, name):
    path = folder / name
    path.parent.mkdir(parents=True, exist_ok=True)
    if name == weights.WEIGHTS:
        save_file({"w": torch.zeros(2)}, path)
    else:
        path.write_text("{}")


@pytest.fixture
def downloads(monkeypatch):
    """Replace the Hugging Face download with one that writes small fake files; record what was asked for."""
    asked = []

    def fake(repo, name, revision, local_dir):
        assert (repo, revision) == (weights.REPO, weights.REVISION)
        asked.append(name)
        write_file(local_dir, name)

    monkeypatch.setattr(weights, "hf_hub_download", fake)
    return asked


def test_downloads_every_missing_file(folder, downloads):
    paths = weights.ensure_weights()
    assert downloads == list(weights.FILES)
    assert all(path.is_file() for path in paths.values())


def test_present_files_need_no_network(folder, downloads):
    for name in weights.FILES:
        write_file(folder, name)
    weights.ensure_weights()
    assert downloads == []


def test_fetches_only_what_is_missing(folder, downloads):
    write_file(folder, weights.WEIGHTS)
    weights.ensure_weights()
    assert downloads == ["metadata.json", "LICENSE"]


def test_logs_each_download(folder, downloads, caplog):
    with caplog.at_level("INFO", logger=weights.__name__):
        weights.ensure_weights()
    assert sum("downloaded" in r.message for r in caplog.records) == len(weights.FILES)


def test_offline_and_missing_names_the_folder(folder, monkeypatch):
    def offline(*args, **kwargs):
        raise OSError("no network")

    monkeypatch.setattr(weights, "hf_hub_download", offline)
    with pytest.raises(RuntimeError, match="could not be downloaded") as error:
        weights.ensure_weights()
    assert str(folder) in str(error.value)


def test_damaged_weights_file_fails(folder, downloads):
    for name in weights.FILES:
        write_file(folder, name)
    (folder / weights.WEIGHTS).write_bytes(b"not a safetensors file")
    with pytest.raises(Exception):
        weights.ensure_weights()


@pytest.mark.slow
def test_real_weights_load_into_vista3d132():
    """Needs the real files (downloads 872 MB if missing). The file's keys carry a 'network.' prefix."""
    from monai.networks.nets.vista3d import vista3d132

    state = load_file(weights.ensure_weights()[weights.WEIGHTS])
    state = {key.removeprefix("network."): value for key, value in state.items()}
    vista3d132(encoder_embed_dim=48, in_channels=1).load_state_dict(state, strict=True)
