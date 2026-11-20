# HoloRepository server

FastAPI server that takes a DICOM (zipped) or NIfTI upload, segments it with a selected model, and returns downloadable results, including an `.mz3` mesh for viewing in NiiVue.

## Setup

Requires [uv](https://docs.astral.sh/uv/) (Python 3.14 is installed automatically).

```sh
uv sync
```

## Run

```sh
cd src
uv run fastapi dev main.py
```

Then open <http://127.0.0.1:8000/docs> to browse every endpoint and model.

## API contract

`API contract.md` and `openapi.json` are generated from the code. Don't edit them by hand: change the endpoints in `src/main.py` or the models in `src/models.py` (docstrings and `Field(description=...)` become the contract text), then regenerate:

```sh
uv run python scripts/generate_contract.py
```

`openapi.json` can be fed to an OpenAPI client generator to produce typed client code for each platform.
