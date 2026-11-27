"""NV-Segment-CT model files: find them in the data folder, or download the pinned revision.

Run on its own to fetch the files ahead of time:
    cd src && uv run python -m segmentation.engines.nv_segment_ct.weights
"""

import logging
import time
from pathlib import Path

from huggingface_hub import hf_hub_download
from safetensors import safe_open

from settings import data_dir

log = logging.getLogger(__name__)

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
    log.info("NV-Segment-CT model files: %s", folder)
    for name in FILES:
        if (folder / name).is_file():
            log.debug("present: %s", name)
            continue
        log.info("downloading %s from %s@%s", name, REPO, REVISION[:12])
        start = time.perf_counter()
        try:
            hf_hub_download(REPO, name, revision=REVISION, local_dir=folder)
        except Exception as error:
            log.exception("download of %s failed", name)
            raise RuntimeError(
                f"NV-Segment-CT file '{name}' is missing from {folder} and could not be "
                f"downloaded ({error}). Connect to the internet, or copy the files there."
            ) from error
        log.info("downloaded %s in %.1f s", name, time.perf_counter() - start)
    try:
        with safe_open(folder / WEIGHTS, framework="pt"):  # fails if the file is damaged
            pass
    except Exception:
        log.exception("weights file is damaged: %s (delete it to download it again)", folder / WEIGHTS)
        raise
    log.info("NV-Segment-CT model files ready")
    return {name: folder / name for name in FILES}


if __name__ == "__main__":
    from log import setup_logging

    setup_logging()
    for path in ensure_weights().values():
        print(path)
