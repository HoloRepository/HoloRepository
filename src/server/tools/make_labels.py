"""Write src/segmentation/labels.json, the shared label list, from NV-Segment-CT's metadata.json (not shipped).

Run from src/server (downloads the model files first if they are missing):
    uv run python tools/make_labels.py

Ids and names are NV-Segment-CT's own, exactly as in metadata.json of the pinned revision; background (0) is left
out. Parents are set only where a structure sits inside an organ that is itself output (decided 2026-11-27).
"""

import json
import sys
from pathlib import Path

SERVER = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SERVER / "src"))

from segmentation.engines.nv_segment_ct.weights import ensure_weights

OUT = SERVER / "src" / "segmentation" / "labels.json"
PARENTS = {
    "left kidney cyst": "left kidney",
    "right kidney cyst": "right kidney",
    "hepatic vessel": "liver",
}


def main() -> None:
    metadata = json.loads(ensure_weights()["metadata.json"].read_text())
    names = {int(i): name for i, name in metadata["network_data_format"]["outputs"]["pred"]["channel_def"].items()}
    names.pop(0)  # background is implicit
    ids = {name: i for i, name in names.items()}
    labels = {str(i): {"name": names[i], "parent": ids.get(PARENTS.get(names[i]))} for i in sorted(names)}
    missing = [name for pair in PARENTS.items() for name in pair if name not in ids]
    if missing:
        raise SystemExit(f"Names not in metadata.json: {missing}")
    lines = [f"  {json.dumps(i)}: {json.dumps(entry)}" for i, entry in labels.items()]  # one label per line
    OUT.write_text("{\n" + ",\n".join(lines) + "\n}\n")
    print(f"Wrote {len(labels)} labels to {OUT}")


if __name__ == "__main__":
    main()
