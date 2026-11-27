"""NV-Segment-CT model files: find them in the data folder, or download the pinned revision.

Run on its own to fetch the files ahead of time:
    cd src && uv run python -m segmentation.weights
"""

from pathlib import Path

from huggingface_hub import hf_hub_download
from safetensors import safe_open

from settings import data_dir

REPO = "nvidia/NV-Segment-CT"
REVISION = "afb51518689f71e6abb367ee6301b2cd0225c66a"
WEIGHTS = "vista3d_pretrained_model/model.safetensors"
FILES = (WEIGHTS, "metadata.json", "LICENSE")


def weights_dir() -> Path:
    return data_dir() / "models" / "nv-segment-ct" / REVISION


def ensure_weights() -> dict[str, Path]:
    """Download any missing file of the pinned revision; return every file's path.

    Files already in the folder are used as they are, so this needs no network once they are there
    (or once an installer has copied them in).
    """
    folder = weights_dir()
    for name in FILES:
        if not (folder / name).is_file():
            try:
                hf_hub_download(REPO, name, revision=REVISION, local_dir=folder)
            except Exception as error:
                raise RuntimeError(
                    f"NV-Segment-CT file '{name}' is missing from {folder} and could not be "
                    f"downloaded ({error}). Connect to the internet, or copy the files there."
                ) from error
    with safe_open(folder / WEIGHTS, framework="pt"):  # fails if the file is damaged
        pass
    return {name: folder / name for name in FILES}


if __name__ == "__main__":
    for path in ensure_weights().values():
        print(path)
