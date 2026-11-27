# Segmentation plan: CT → label map inside `POST /new-job`

Status: agreed 2026-11-24, updated 2026-11-26 with the demo eval results; not started. Meshing and 3D output belong to another team member and are out of
scope here, except for the handover in [section 8](#8-handover-to-meshing).

The prototype (`Holo-Prototype`, commit `df12711`) and its handoff are a **reference only**. The fixed points are
the NV-Segment-CT model and the optional TotalSegmentator kidney and liver add-ons. Everything else was decided
here.

**Goal:** the same or better accuracy than the prototype, at the fastest runtime we can get on NVIDIA, Intel Arc
and Apple GPUs, with the least code.

---

## 1. Scope

In scope: upload → standard CT → segmentation (NV-Segment-CT, optional add-ons) → post-processing → label map and
label table on disk → job status.

Out of scope (document as gaps): meshing/MZ3, lesion clicks, deployment, the client installer, a hardware-report
endpoint, a real job queue and persistent storage, proper out-of-memory handling.

## 2. API changes

- `model` names the base model only. Values: `nv-segment-ct`. `ts-specialist-head-n-neck` is dropped
  (peaked at 142 GB RAM in the prototype).
- New boolean form fields `kidney_addon` and `liver_addon` (default `false`).
- Bad uploads return **400** from the request itself (the CT is unzipped and read before the job id is returned).
- Regenerate `API contract.md` and `openapi.json`; tell the client team.

## 3. Pipeline

```
upload (DICOM zip | NIfTI)
  │  safe extract, pick series, checks  → 400 on failure
  ▼
standard CT in memory (Int16 HU, original grid)  ── also written as ct.nii (uncompressed / gzip level 1)
  │
  ▼  engine from lookup table: nv-segment-ct
NV-Segment-CT
  preprocessing  (copied exactly from the release, float32 on the GPU)
  streaming sliding-window inference  (labels per finished slab; scores never fully stored)
  labels back onto the CT grid
  │
  ▼  only if kidney_addon / liver_addon
TotalSegmentator add-ons, cropped on NV-Segment-CT's organs, merged by rule
  │
  ▼
post-processing: stray blobs removed from single-piece organs
  │
  ▼
labels.nii (uint16, CT grid) + labels.json  → handed to meshing
```

## 4. Engine and model choice

- Interface: `segment(ct) -> (label_map on the CT grid, {id: name})`.
- Lookup table: `SegmentationModel` value → engine. Second table: add-on name → add-on runner.
- Add-ons ask for organs by **shared name**, so any future base model works with them.
- One shared label list (`labels.json` in the repo): NV-Segment-CT's own ids kept as they are (1–132), new
  structures above 132, liver vessels reuse NV-Segment-CT's hepatic-vessel id 25. Each entry: id, name, parent.

## 5. NV-Segment-CT engine

**Model.** `nvidia/NV-Segment-CT`, revision `afb51518689f71e6abb367ee6301b2cd0225c66a`, file
`vista3d_pretrained_model/model.safetensors` (872 MB), loaded into `monai.networks.nets.vista3d132` (encoder
embed dim 48, 1 input channel). Licence notice kept next to the weights. Loaded once at startup, single server
process, warmed up with one dummy window.

**Classes ("everything").** 117 classes: 1–132 minus {2, 16, 18, 20, 21, 23, 24, 25, 26, 27, 128, 129, 130, 131,
132}. Names from the release's `metadata.json`.

**Preprocessing, copied exactly from the release (`vista3d_pipeline.py`):**
1. Resample to 1.5 mm, bilinear, on raw HU (`Spacingd`, `align_corners=False`, border padding).
2. Crop to the bounding box of voxels > 0 HU plus a 10-voxel margin (`CropForegroundd`, `allow_smaller=True`).
3. Scale HU from [-963.8247715525971, 1053.678477684517] to [0, 1], clipped.
4. Orient to RAS.

Resampling: MONAI's own geometry functions on the CPU (float64, tiny), then the interpolation with torch
`grid_sample` on the GPU in float32 (the same call MONAI makes). Confirmed by the demo eval: 99.996% of voxels
unchanged and 2.5% faster than float64. Fallback: MONAI's `Spacingd` unchanged on the
CPU, used automatically on any device without 3D `grid_sample`.

**Inference (automatic mode, as in the release):** 128³ windows, overlap **0.25**, **constant** blending,
replicate padding, `with_coord=True`, `transpose=True`. Overlap is fixed, not a setting (0.5 gave +0.0032 Dice for
1.64× the time). No flip test-time augmentation (left-right flip lost 0.012 Dice and took 2.75× the time).

Streaming instead of MONAI's helper: we run the same windows ourselves in z order. When every window covering a
slab of the scan has run, that slab's blended scores are final: pick its labels and drop its scores. The blended
totals are always accumulated in **float32**; only the network itself runs at reduced precision.

Demo eval: identical labels to the whole-volume run, peak GPU memory 12.55 → 6.45 GB, +2.2% time. The buffer
grows with the scan's width × height (not its length), so wide scans peaked at 8.4–8.8 GB (768 × 768 AMOS
scans) — over the RTX 5060's 8 GB. **Decision: no bound for now; measure first.** Our engine logs peak GPU
memory per scan, and step 3 runs the wide scans under an 8 GB cap. Only if they fail do we add one of: column
groups for scans whose buffer won't fit (identical labels, most code), CPU accumulation for those scans only
(simple, slower), or fp16 accumulation for those scans only (one line, needs a label check).

**Choosing labels (exactly like `VistaPostTransformd`):** label = most likely class; background where every class
scores ≤ 0.

Where labels are picked:
- `original` (**default**, accepted 2026-11-26 with its NSD cost, section 14): interpolate
  the scores onto the CT grid, then pick labels there. Demo eval: AMOS Dice +0.0074 (95% CI +0.0070 to
  +0.0078), all 15 organs up, NSD −0.0024, +1.1 s per scan.
- `nvidia` (kept as the switch's other value): pick labels on the 1.5 mm grid, then nearest-neighbour onto the
  CT grid with SimpleITK.

`original` inside streaming (built in this repo, not the demo): each finished slab's scores are interpolated onto
the CT voxels whose world position falls inside that slab, with a one-voxel margin so trilinear interpolation has
its neighbours. Matching is by world coordinates, so CT slices that are not aligned with the stream's z axis
still work. Each CT voxel is labelled exactly once. Sampling grids are built per slab in float32, never for the
whole CT grid: the demo's 12.1 GB peak on the 734-slice scan came from MONAI's `Invertd` building two float64
grids for the full 734 × 512 × 512 CT (5.73 + 4.30 GiB).

Outside the cropped body box the label is 0.

**Speed settings:** mixed precision per device. fp16 on CUDA is confirmed (demo eval: same Dice as fp32, fp32
inference 4× slower). bf16 or fp16 on Arc and Mac, kept only where the equivalence test shows no change; `cudnn.benchmark` on NVIDIA; windows per GPU pass chosen from free VRAM (1 on 8 GB cards).

**Memory guard:** a maximum voxel count per job, set per machine by environment variable. Larger scans fail with a
clear message. Proper out-of-memory handling is documented as future work.

## 6. Post-processing

Single-piece organs drop connected pieces (26-connected) smaller than 20% of their largest piece: liver, spleen,
pancreas, gallbladder, stomach, both kidneys, both adrenals, bladder, heart, brain, thyroid, sternum, sacrum,
prostate. Tubes (aorta, venae cavae, oesophagus, trachea, spinal cord) are left alone. Selected by shared name,
computed on each organ's bounding box. Removed voxels become background. No overlap-painting step: one label per
voxel already rules out overlaps.

## 7. TotalSegmentator add-ons

- Optional dependency (`addons` extra), version pinned (2.18.0 checked). Licences of `liver_vessels` and
  `kidney_cysts` checked by hand once (neither is marked commercial in 2.18.0); recorded here, no run-time check.
- Weights downloaded at startup if the extra is installed; models loaded on each call.
- Skipped when the organ is missing or tiny.
- Run through `totalsegmentator.nnunet.nnUNet_predict_image` with **NV-Segment-CT's organs as the crop mask**
  and TotalSegmentator's own organ lists and margins:
  - `liver_vessels` (task 8): liver, margin 20 voxels.
  - `kidney_cysts` (task 789): kidney left/right, liver, spleen, colon, margin 10 voxels.
  TotalSegmentator crops, runs and pastes back itself. No whole-body model, no patching.
- The CT is converted from SimpleITK (LPS) to an in-memory nibabel image (RAS).
- Merge: add-on voxels overwrite NV-Segment-CT only where the add-on finds a structure. Liver vessels → id 25
  (NV-Segment-CT's everything mode has no 25, so this is the add-on alone).
- **Kidney cysts: add-on cysts only** (confirmed by the demo eval, 2026-11-26). When the kidney add-on runs,
  NV-Segment-CT's cysts are turned back into their kidney (116 → 14, 117 → 5) and only the add-on's cysts are
  kept. KiTS23, 29 scans, Dice / NSD 2 mm: add-on only 0.513 / 0.589; prototype's per-side rule (NV-Segment-CT's
  cyst wins on a side) 0.295 / 0.307; union 0.508 / 0.575; NV-Segment-CT only 0.153 / 0.104 (8 scans empty).
  Add-on only beats the prototype by Dice +0.218 (95% CI +0.124 to +0.318).
- When the kidney add-on is off, NV-Segment-CT's own cysts (116, 117) are kept as it outputs them, with no
  special rule (decided 2026-11-26), although they are weak (Dice 0.153, empty on 8 of 29 KiTS23 scans).
- Few worker processes (TotalSegmentator's resampling/saving threads kept low) to keep RAM down on 16–32 GB
  machines; the prototype's add-on peaked at 23–27 GB median (over-counted across workers).
- Device: the same GPU as NV-Segment-CT. On Intel Arc, try XPU and fall back to the CPU on error.
- A failing add-on never fails the job: it is logged and the job returns NV-Segment-CT's result.

## 8. Handover to meshing

Proposed agreement (the user confirms it with the meshing team member before step 7):

- `labels.nii` (or `.nii.gz` at level 1): uint16 label map, 0 = background, on exactly the grid (shape,
  spacing, origin, direction) of the job's `ct.nii`. Written with SimpleITK; nibabel's `image.affine` gives RAS
  millimetres directly.
- `labels.json`: `{"<id>": {"name": "...", "parent": <id or null>}}` for every id present. `parent` marks sub-parts
  (e.g. a kidney cyst's kidney).
- Ids come from the shared list and do not change between models.
- Colours, display names and grouping belong to meshing.

## 9. Uploads

- ZIP: reject entries that escape the target folder (`../`, absolute paths); limits on total extracted size and
  file count; an upload size limit.
- DICOM: the series with the most files; `Modality` must be `CT`; non-uniform slice spacing or gantry tilt → 400.
- NIfTI: accepted without a modality check; 4D uses the first time point; anything not 3D → 400.
- Output: Int16 HU CT on the original grid, kept in memory for the job and written once as `ct.nii`
  (uncompressed or gzip level 1).

## 10. Devices and packaging

- Device order: CUDA → XPU (Intel Arc) → MPS (Apple) → CPU. The CPU is a supported fallback.
- Targets: Intel Arc B390 (integrated, 32 GB shared RAM; tested on an Arc 140V with 32 GB until a B390 is
  available); NVIDIA RTX 5060 (8 GB VRAM, 32 GB RAM) and up;
  Apple Silicon Macs on macOS 15 or later.
- Python 3.14 (3.13 fallback). Torch 2.15 pinned on every platform.
- uv extras: `cuda` (PyTorch `cu128` index; RTX 50 needs CUDA 12.8+) and `xpu`; plain `uv sync` on Mac.
  The installer team picks the command; a short note says which one per machine. uv's macOS target set to 15.
- The server must run natively on Windows and macOS.

## 11. Runtime

- Temporary runner: one worker thread, a queue and job status in memory. Jobs are lost on restart. Documented as
  "replace with a real queue and storage".
- Job status `in_queue` → `in_progress` → `done` / `error` with a message.
- Each job logs the time of every stage (upload reading, preprocessing, inference, mapping back, add-ons,
  post-processing, writing) and a run record: package versions, model revision, device, precision and settings.
  Written next to the job's outputs as `run.json`.

## 12. Code layout

```
src/segmentation/
  devices.py        device choice, precision, free memory
  ct_input.py       upload → standard CT (safe zip, series choice, checks)
  labels.py         shared label list (labels.json)
  engines/          lookup table + nv_segment_ct.py
  addons.py         TotalSegmentator add-ons
  postprocess.py    stray-blob removal
  pipeline.py       run one job end to end
  runner.py         temporary queue
tests/segmentation/ fast unit tests (default) + slow GPU/weights tests (opt-in)
tools/benchmark/    step 4 and step 8 scripts (score against expert labels and reference maps)
```

## 13. Build steps

| Step | Work | Tests / checks |
|---|---|---|
| 0 | Dependencies, extras, weights download; smoke script: device, float64, 3D `grid_sample`, fp16/bf16, one run | Run by the user on the Mac, the Arc 140V, the RTX 5060 laptop and cream (Turing GPU, needed for step 4); disk sizes for the install note |
| 1 | Upload → standard CT | Single/multiple series, non-CT modality, 4D, 2D, corrupt zip, path-escape zip, size limits, geometry kept exactly |
| 2 | Shared label list, engine interface, lookup tables | No duplicate ids or names; every engine class resolves |
| 3 | NV-Segment-CT engine | Streaming result equals whole-volume result (both label choices); `original` inside streaming equals interpolating the whole score volume; resampling matches MONAI; mapping back on synthetic oblique data; device choice; peak GPU memory logged per scan; wide (768 × 768) and long scans run under an 8 GB cap (fail → choose a buffer bound) |
| 4 | Equivalence test (on cream) | Ours (raw output, both label choices from one pass) vs the demo's Eval 4 label maps before clean-up: ≥ 99.9% voxel agreement on every scan; AMOS mean Dice within 0.001 of the reference and the `original` gain ≈ +0.0074; time and peak GPU memory logged. The `nvidia` reference is the release pipeline's own output, so no separate NVIDIA-pipeline environment. Mac and Arc: same comparison on a few scans (wide ones included) with reduced precision on/off, deciding precision per device. Maps on cream: `/cs/student/ug/2025/jborrajo/holorepo-gpu/engine_evals/eval4_labels/` (`baseline/` = `nvidia`, `E4_original/` = `original`; `raw/` before clean-up, `clean/` after; `manifest.csv` with SHA-256; 357 scans, none for `kits23_case_00165`) |
| 5 | Post-processing | Blobs removed from organs, tubes untouched |
| 6 | Add-ons | Crop mask built correctly; LPS/RAS round trip on an asymmetric volume; merge rules incl. cyst → kidney; skipped when organ missing; failure still returns a result; real peak RAM (whole process tree, shared memory counted once) on the largest KiTS23 and MSD scans on the Mac and the 32 GB laptops, with TotalSegmentator's worker processes kept to a minimum |
| 7 | Wire into `/new-job` | 400s for bad uploads; status sequence; voxel limit |
| 8 | Benchmark (separate stage) | Run after steps 0–7 on this repo's engine, through `pipeline.py` (the code `/new-job` runs). Against the prototype on every device: Dice, NSD, time per stage, peak GPU memory and RAM, failures. Scans: all 358 (300 AMOS, 30 KiTS, 28 MSD) on every device. Label maps are saved and scored afterwards, so scoring time stays out of the speed numbers. Add-ons scored too: kidney cysts on KiTS23, liver vessels on MSD hepatic vessel, against the reference add-on output on cream (`engine_evals/addon_labels/`: kidney `kidney_cysts/addon_only/` = add-on cysts before merging, Dice 0.518 / NSD 0.593 on 30 scans; liver `liver_vessels/final/`, Dice 0.664 / NSD 0.812 on 28 scans; prototype add-on time 15–18 s median on cream), with timings and real peak RAM. Add-on pass: not significantly worse than the reference (95% CI of the paired per-scan Dice difference reaches 0 or above), and median add-on time on cream no slower than the prototype's. Scored with the prototype's definitions (copied into `tools/benchmark/`): KiTS23 label 3, MSD label 1, NSD 2 mm with 6-connected erosion boundaries, empty prediction = 0, failures listed separately. Pass: AMOS mean Dice ≥ 0.8654 (0.860 + 0.0074 − 0.002), no failed scans, typical abdominal scan ≤ 45 s on the RTX 5060 laptop; Apple M4 16 GB median ≤ 113 s per scan including model load (the prototype's figure, like for like), also reported with the model already loaded; Arc 140V (32 GB) the same ≤ 113 s, rerun on the B390 when available; peak GPU memory reported for every scan, wide (768 × 768) and long scans included |

## 14. Decided by evaluation

Demo eval on cream (Quadro RTX 6000, 2026-11-25/26; baseline reproduced: AMOS Dice 0.8604, NSD 0.8624, 9.0 s per
scan with the model loaded):

| Item | Result | Decision |
|---|---|---|
| Streaming | Identical labels, GPU 12.55 → 6.45 GB, +2.2% time | Adopted |
| fp16 on CUDA | Same Dice as fp32; fp32 inference 4× slower | Adopted |
| float32 resampling | 99.996% voxels unchanged, −2.5% time | Adopted |
| `original` label choice | Dice +0.0074, NSD −0.0024, +1.1 s | Default; NSD cost accepted (below) |
| Overlap 0.5 | Dice +0.0032, 1.64× time | Not adopted; overlap fixed at 0.25 |
| Left-right flip | Dice −0.012, 2.75× time | Dropped |

- **NSD drop with `original`** (diagnosed 26 Nov, CPU only, 300 AMOS scans). Not caused by specks: `original`
  has fewer stray pieces than `nvidia` (160 vs 1,266 before clean-up). The outline is smoother and misses fine
  detail on thin and tubular organs (IVC −0.0083 NSD, duodenum −0.0067). Neither clean-up rule recovers it;
  the Dice gain survives both. Clean-up as planned (section 6) adds Dice +0.0003 / NSD +0.0007 on both choices;
  the 0.02 mL speck rule changes nothing. **Accepted** (2026-11-26): Dice is the target metric and `original` has
  fewer stray pieces.

Still open, settled by a build step's measurement:
- **Bounding the streaming buffer** for wide scans on 8 GB cards: not built; decided by the step 3 measurement
  under an 8 GB cap (section 5). Demo figures: inference alone peaks at 8.16 GB on the 734-slice scan; on the
  768 × 768 AMOS scans (8.41–8.79 GB) the stage that sets the peak was not measured.
- Mixed precision on Arc and Mac (equivalence test, then benchmark).

Considered and not adopted for now: automatic CPU retry when the GPU fails mid-job; cropping before resampling;
TensorRT (NVIDIA) / OpenVINO (Intel) runtimes, to revisit only if the step 0 timings disappoint.

## 15. Documented gaps

- Real queue and storage.
- Out-of-memory handling beyond the voxel limit.
- Deployment and installer.
- Hardware-report endpoint.
- RTX Spark / Windows on Arm (CUDA PyTorch is nightly-only there; SimpleITK, scikit-image, TotalSegmentator Arm
  builds unconfirmed). Not a target.
- Lesion clicks.
- **Blocking upload reading.** `/new-job` is `async def`; reading the CT inside it blocks the event loop, so every
  other request (including `/job-status` polls) waits until the upload is read. Whoever owns the server runtime
  should move that work onto a worker thread.
