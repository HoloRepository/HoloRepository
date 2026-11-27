# HoloRepository server

FastAPI server that takes a DICOM (zipped) or NIfTI upload, segments it with a selected model, and returns downloadable results, including an `.mz3` mesh for viewing in NiiVue.

## Setup

Requires [uv](https://docs.astral.sh/uv/) (Python 3.14 is installed automatically). Pick the command for your machine:

| Machine | Command |
|---|---|
| Mac (Apple silicon, macOS 15+) | `uv sync` |
| NVIDIA GPU (driver 580+) | `uv sync --extra cuda` |
| Intel Arc GPU | `uv sync --extra xpu` |

Add `--extra addons` for the optional TotalSegmentator add-ons. Always repeat the same extras, or uv swaps torch back to the CPU build.

Disk: about 1 GB for `.venv` on a Mac (more with `cuda` / `xpu`), plus 0.8 GB of model files downloaded on first start.

## Run

```sh
cd src
uv run fastapi dev main.py
```

Then open <http://127.0.0.1:8000/docs> to browse every endpoint and model.

Data (model files, logs, jobs) lives in `HOLO_DATA_DIR`, by default the per-user app-data folder (`~/Library/Application Support/HoloRepository` on macOS, `%LOCALAPPDATA%\HoloRepository` on Windows). The log is `logs/server.log` there; set `HOLO_LOG_LEVEL=debug` for more detail.

## Tests

```sh
uv run pytest            # fast tests
uv run pytest -m slow    # slow tests only: use the real model files
uv run python tools/smoke.py   # GPU check for this machine, writes smoke-report.json
```

## API contract

`API contract.md` and `openapi.json` are generated from the code. Don't edit them by hand: change the endpoints in `src/main.py` or the models in `src/models.py` (docstrings and `Field(description=...)` become the contract text), then regenerate:

```sh
uv run python scripts/generate_contract.py
```

`openapi.json` can be fed to an OpenAPI client generator to produce typed client code for each platform.
