# Step 0: dependencies, weights and smoke check

Part of the [segmentation plan](../segmentation-plan.md) (section 13, step 0).

| | |
|---|---|
| Depends on | nothing |
| Branch / PR base | from `main`, PR into `main` |
| Unblocks | every other step |
| Shipped code | ~80 lines (`pyproject.toml`, weights download); smoke script not counted |
| Runs on your machines | yes: Mac, Arc 140V, RTX 5060 laptop, cream |

## Goal

Every target machine can install the server with one `uv` command, get the pinned NV-Segment-CT weights, and pass a
smoke check that proves the GPU can do what the engine needs. Nothing about segmentation is built yet.

## Decisions that apply (from the plan)

- Python **3.14** (`.python-version` already says 3.14); 3.13 is the fallback if a package won't install.
- **Torch 2.15 pinned on every platform.** Mac only gets 2.15 when uv resolves for **macOS 15**; older macOS is not
  supported (plan section 10).
- Install options (uv extras): **`cuda`** (PyTorch `cu132` index, decided 2026-11-27: torch 2.15 has no `cu128`
  build; CUDA 13 needs NVIDIA driver 580+; the RTX 50 series needs CUDA 12.8+, so 13.2 is fine) and
  **`xpu`** (Intel Arc). Plain `uv sync` on the Mac (MPS). A short note says which command each machine uses; the
  installer team picks it up from there. No `--torch-backend` (that flag exists only for `uv pip`, not `uv sync`).
- **`addons` extra** for TotalSegmentator **2.18.0** (pulls nnU-Net v2 2.8.1). Optional; the server works without it.
- **No `transformers`.** The weights are loaded straight into MONAI's `vista3d132`, so the
  `transformers==4.46.3` pin (and the pre-release `tokenizers` it drags in on 3.14) stays out. The separate
  NVIDIA-pipeline environment was dropped (step 4 compares against the demo's maps instead).
- Model: [`nvidia/NV-Segment-CT`](https://huggingface.co/nvidia/NV-Segment-CT), revision
  `afb51518689f71e6abb367ee6301b2cd0225c66a`, file `vista3d_pretrained_model/model.safetensors` (872 MB), plus
  `metadata.json` (class names, used in step 2). Licence notice kept next to the weights: "Licensed by NVIDIA
  Corporation under the NVIDIA Open Model License".
- Device order everywhere: CUDA → XPU → MPS → CPU.
- Targets: Mac on macOS 15+ (M4, 16 GB); Intel Arc 140V 32 GB now, Arc B390 later; RTX 5060 laptop 8 GB VRAM; cream
  (Quadro RTX 6000, Turing, Linux) for steps 3, 4 and 8. The server must run natively on Windows and macOS.

## Work, in stages

### 0.1 Dependencies in `pyproject.toml`

Add to the base dependencies (exact pins chosen when the lock resolves; meshing's packages stay as they are):

| Package | Why |
|---|---|
| `torch==2.15.*` | the network; CPU/MPS build from PyPI by default |
| `monai==1.6.1` | `vista3d132`, the geometry helpers used by preprocessing, `Spacingd` fallback |
| `einops` | imported by MONAI's VISTA3D code |
| `SimpleITK` | DICOM reading, CT grid, nearest-neighbour mapping back, writing `labels.nii` |
| `safetensors` | reading `model.safetensors` |
| `huggingface_hub` | downloading the pinned revision |

`numpy`, `nibabel` and `scikit-image` (which brings `scipy`) are already there.

Extras:

| Extra | Contents |
|---|---|
| `cuda` | `torch` from the PyTorch `cu132` index (`2.15.0+cu132`) |
| `xpu` | `torch` and `triton-xpu` from the PyTorch `xpu` index (`2.15.0+xpu`, Windows and Linux) |
| `addons` | `TotalSegmentator==2.18.0` |

uv configuration:
- Two explicit indexes (`cu132`, `xpu`) and `[tool.uv.sources]` mapping `torch` to each index by extra.
  `triton-xpu` (needed by the XPU torch, only on PyTorch's index) is listed in the `xpu` extra and mapped to the
  `xpu` index too, because an explicit index only serves the packages mapped to it.
- The extras must be written in `[project.optional-dependencies]` by hand before `uv lock`: uv rejects a source
  that names an extra that doesn't exist yet, so `uv add --optional` can't create them.
- `cuda` and `xpu` declared as **conflicting** extras, so the lock holds both variants.
- The macOS target set to 15 so the lock picks torch 2.15 for Mac. Check which mechanism works with uv 0.13 (a
  uv setting or `MACOSX_DEPLOYMENT_TARGET=15.0` when locking) and write it in the README.
- Regenerate `uv.lock`. It must resolve for macOS arm64, Windows x86-64 and Linux x86-64.

### 0.2 Weights download

- A small function that downloads the three files of the pinned revision (`model.safetensors`, `metadata.json`,
  the licence) with `huggingface_hub`, checks the safetensors file opens, and returns their paths.
- **Where (decided 2026-11-27, seamless for the installer):** a folder the server owns, set by an environment
  variable, not the Hugging Face cache. Files are downloaded straight into it (`hf_hub_download(local_dir=...)`), so
  an installer can ship them pre-filled, or point the variable at a shared copy, and the server finds them with no
  network.
  - Folder (decided 2026-11-27): `$HOLO_DATA_DIR/models/`. `HOLO_DATA_DIR` defaults to the per-user data folder:
    `~/Library/Application Support/HoloRepository` on macOS, `%LOCALAPPDATA%\HoloRepository` on Windows
    (`~/.local/share/HoloRepository` on Linux, for cream). Steps 6 and 7 put `totalsegmentator/` and `jobs/` under
    the same root. For development, set it to a folder outside OneDrive (e.g. `~/holo-data`).
- **When (decided 2026-11-27, seamless for clients):** at server startup, only if a file is missing. The files are
  checked and loaded before the server accepts requests, so clients never see a half-ready server.
  - The first start downloads 872 MB (a few minutes); later starts don't touch the network.
  - Missing files and no network → the server refuses to start with a message naming the folder and the files.
  - A download that is interrupted leaves no half-written file in place (`huggingface_hub` writes to a temporary
    file and renames it when complete), and the next start resumes it.
  - The same function is callable on its own (e.g. `uv run python -m segmentation.weights`), so an installer or a
    machine without network at runtime can fetch the files ahead of time.
- Weights for the add-ons are **not** downloaded here; that is step 6 ("downloaded at startup if the extra is
  installed").

### 0.3 Smoke script (`tools/smoke.py`, not shipped)

One command per machine, printing a short report and writing it as JSON for you to paste back:

1. **Environment:** OS, Python, torch / MONAI / SimpleITK versions, the chosen device and its name, total and free
   memory (VRAM, or shared RAM on Mac and Arc).
2. **Device order:** which of CUDA, XPU, MPS is available; confirm the order picks the right one.
3. **float64 on the device:** create and multiply a float64 tensor. MPS is expected to fail; the plan already keeps
   float64 geometry on the CPU, so this records the fact rather than failing the smoke.
4. **3D `grid_sample` in float32 on the device**, bilinear, `align_corners=False`, border padding (the call
   preprocessing will use). Compare with the CPU result; report the maximum difference.
5. **Reduced precision:** a 3D convolution under autocast in fp16 and in bf16 on the device; report whether each
   runs and its difference from fp32.
6. **Model load:** build `vista3d132(encoder_embed_dim=48, in_channels=1)`, load the safetensors with a strict key
   match (no missing or unexpected keys), move it to the device.
7. **One window:** run one 128³ window in "everything" mode (117 classes, `with_coord=True`) after one warm-up
   window; report time and peak memory in fp32 and in the reduced precision that ran. This gives a first
   per-scan estimate (a typical abdominal scan is a few dozen windows). Full-scan timing comes with the engine in
   step 3.
8. **Disk:** size of `.venv` and of the weights, for the install note.

Each check reports pass / fail / not applicable and never stops the others.

### 0.4 README

- Install command per machine (Mac, Windows RTX, Windows Arc, Linux cream), with and without `addons`.
- Minimum macOS 15; NVIDIA driver 580 or newer (CUDA 13).
- How to run the smoke script.
- Disk sizes from 0.3.

## Tests and checks

- `uv lock` resolves for all three platforms; `uv sync`, `uv sync --extra cuda`, `uv sync --extra xpu` and each
  with `--extra addons` succeed where they apply.
- The existing server still starts (`cd src && uv run fastapi dev main.py`) and `scripts/generate_contract.py`
  still runs. Meshing code is untouched.
- The smoke script passes on each machine, or each failure is understood and recorded in the issue.

## Runs on your machines

| Machine | Command | What to send back |
|---|---|---|
| Mac (M4) | `uv sync` then the smoke script | JSON report |
| Arc 140V (Windows) | `uv sync --extra xpu` then the smoke script | JSON report |
| RTX 5060 laptop (Windows) | `uv sync --extra cuda` then the smoke script | JSON report |
| cream (Linux) | `uv sync --extra cuda` then the smoke script | JSON report; confirms torch 2.15 `cu132` runs on Turing (compute 7.5) with cream's driver (needs 580+; check `nvidia-smi` first), needed for step 4 |

About 30 minutes per machine, mostly the 2–3 GB torch download.

## What the results decide

- Whether the GPU path of preprocessing works on each device (check 4), or that device uses the CPU fallback
  (MONAI's `Spacingd` on the CPU).
- Which reduced precision to try per device in step 4 (check 5).
- Whether TensorRT or OpenVINO need revisiting (plan section 14: only if these timings disappoint).
- If Python 3.14 blocks any install: fall back to 3.13.

## Out of scope

- Any segmentation code (steps 1–7).
- Add-on weights (step 6).
- Installer, deployment, a hardware-report endpoint (documented gaps).

## Open questions (settle in the issue)

All settled 2026-11-27:

- The weights live in `$HOLO_DATA_DIR/models/`; `HOLO_DATA_DIR` defaults to the per-user data folder (0.2).
- They are downloaded at startup only if missing (0.2).
MONAI pinned to **1.6.1** (decided 2026-11-27): the demo's version, so step 4 compares like for like. 1.6.2 was
diffed: the code we use (`vista3d.py`, `segresnet_ds.py`, `ScaleIntensityRange`) is identical, and its resampling
fix only applies to the compiled / integer-mode path we don't use. Moving to 1.6.2 later is a one-line PR after
step 4 passes.

## Done when

- The lock and all install commands work on the four machines.
- Four smoke reports are in the issue, with failures explained.
- README has the install note and disk sizes.
- PR merged into `main`.
