# Step 3: NV-Segment-CT engine

Part of the [segmentation plan](../segmentation-plan.md) (section 5; section 13, step 3).

| | |
|---|---|
| Depends on | step 0 (dependencies, weights), step 2 (engine interface, labels) |
| Branch / PR base | from step 2's branch (or `main` once it is merged) |
| Unblocks | step 4 (equivalence run), step 6 (add-ons crop on this engine's organs), step 7 |
| Shipped code | ~500 lines: `src/segmentation/engines/nv_segment_ct.py`, `src/segmentation/devices.py` |
| Runs on your machines | yes: 8 GB cap test on cream, then the RTX 5060 laptop |

The core of the work and the riskiest step (estimate 2–3 days). It reproduces NVIDIA's release exactly, with three
changes the demo eval validated: streaming inference, float32 resampling, and labels chosen at the CT's original
resolution.

## Goal

`segment(ct)` takes the standard CT (step 1) and returns a uint16 label map on exactly the CT's grid plus
`{id: name}` for the 117 classes, matching the demo's reference maps (checked in step 4), using at most one row of
windows of GPU memory, on CUDA, XPU, MPS and the CPU.

## Decisions that apply (from the plan)

**Model.** [`nvidia/NV-Segment-CT`](https://huggingface.co/nvidia/NV-Segment-CT), revision
`afb51518689f71e6abb367ee6301b2cd0225c66a`, `vista3d_pretrained_model/model.safetensors`, loaded into
`monai.networks.nets.vista3d132(encoder_embed_dim=48, in_channels=1)`. Loaded once at startup, warmed up with one
dummy window.

**Classes ("everything").** 117: ids 1–132 minus {2, 16, 18, 20, 21, 23, 24, 25, 26, 27, 128, 129, 130, 131, 132},
exactly the release's `EVERYTHING_LABEL`.

**Preprocessing, copied exactly from the release (`vista3d_pipeline.py`):**
1. Resample to 1.5 mm, bilinear, on raw HU (`Spacingd`, `align_corners=False`, border padding).
2. Crop to the bounding box of voxels > 0 HU plus a 10-voxel margin (`CropForegroundd`, `allow_smaller=True`).
3. Scale HU from [-963.8247715525971, 1053.678477684517] to [0, 1], clipped.
4. Orient to RAS. Then float32.

(The release also runs `VistaPreTransformd` between 2 and 3; it only rewrites label prompts for the parent classes
2, 20, 21 and doesn't touch the image, so automatic mode doesn't need it.)

**Resampling:** MONAI's own geometry functions on the CPU (float64, tiny), then the interpolation with torch
`grid_sample` on the device in **float32** (demo: 99.996% of voxels unchanged, 2.5% faster). Fallback: MONAI's
`Spacingd` unchanged on the CPU, on any device without 3D `grid_sample` (step 0's smoke result decides which).

**Inference (automatic mode, as in the release's `Vista3dInferer`):** 128³ windows, overlap **0.25** (MONAI's
default; the release's `overlap=0.3` setting is only used by the point-click path), **constant** blending,
**replicate** padding, `with_coord=True`, `transpose=True`, class vector = the 117 ids, no points. Overlap is fixed,
not a setting. No flip test-time augmentation.

**Streaming instead of MONAI's helper.** Same windows, same order, same padding as MONAI; scores accumulated in a
**float32** buffer that holds one row of windows (128 slices). When every window touching a slab has run, the slab
is final: choose its labels, drop its scores. Demo: identical labels, GPU 12.55 → 6.45 GB, +2.2% time.
**No buffer bound for now:** measured in this step under an 8 GB cap; a bound is added only if wide scans fail.

**Label rule (exactly like `VistaPostTransformd`):** label = most likely class; background where every class
scores ≤ 0.

**Where labels are chosen** (a switch):
- **`original` (default):** interpolate the scores onto the CT grid, then apply the label rule there. Demo: AMOS
  Dice +0.0074, NSD −0.0024 (accepted), +1.1 s.
- `nvidia`: apply the rule on the 1.5 mm grid, then nearest-neighbour onto the CT grid with SimpleITK.

**`original` inside streaming:** each finished slab's scores are interpolated onto the CT voxels whose world
position falls inside that slab, with a one-voxel margin so trilinear interpolation has its neighbours. Matched
through world coordinates, so CT slices not aligned with the stream's axis still work. Each CT voxel labelled exactly
once. Sampling grids built per slab in float32, **never for the whole CT** (the demo's 12.1 GB peak on the
734-slice scan was MONAI's `Invertd` building float64 grids for the full CT).

**Outside the cropped body box the label is 0.**

**Speed settings:**
- Mixed precision per device: **fp16 autocast on CUDA** (confirmed: same Dice, fp32 inference 4× slower). Arc and
  Mac: bf16 or fp16, decided in step 4. The release hard-codes `torch.autocast("cuda")`; ours uses the device's own
  autocast.
- `cudnn.benchmark` on NVIDIA.
- Windows per GPU pass chosen from free memory (1 on 8 GB cards).

**Devices:** CUDA → XPU → MPS → CPU; the CPU is a supported fallback.

## Work, in stages

### 3.1 `devices.py`

- Pick the device in the plan's order; report its name, total and free memory.
- Precision per device (CUDA fp16; XPU / MPS from step 4's result; CPU fp32).
- Peak memory per device, reset and read per job (CUDA and XPU have peak counters; MPS only reports current use,
  see open question 3).
- Whether 3D `grid_sample` is usable on the device (from a tiny check at startup, so the fallback is automatic).

### 3.2 Model loading and warm-up

- Build `vista3d132`, load the safetensors strictly (no missing or unexpected keys), eval mode, move to the device.
- Warm up with one dummy 128³ window in the chosen precision (also triggers `cudnn.benchmark`'s search).
- Record for `run.json`: model revision, device, precision, overlap, label choice, resampling dtype, streaming on.

### 3.3 Preprocessing

- From the SimpleITK CT: array + geometry. Compute the 1.5 mm target grid with MONAI's geometry helpers on the CPU
  in float64, so the output shape and affine are exactly what `Spacingd` would produce.
- Interpolate on the device with `grid_sample` in float32 (bilinear, `align_corners=False`, border padding); on a
  device without it, run MONAI's `Spacingd` on the CPU.
- Crop (> 0 HU, margin 10, `allow_smaller`), scale and clip, orient to RAS, exactly as the release.
- Keep the affine from the resampled-cropped-oriented volume to world coordinates: mapping back needs it.

### 3.4 Window plan

- Reproduce MONAI's window positions exactly: the same padding to at least 128 per axis (split as MONAI does, half
  before and half after, replicate mode), the same scan interval for overlap 0.25, the same `dense_patch_slices`.
- Order the windows by their start along the stream axis (the RAS z axis); windows starting at the same z form one
  row.

### 3.5 Streaming inference

- Score buffer: float32, 117 × X × Y × 128 (one row's depth), plus a per-voxel window count (blending is constant,
  so one count serves all channels).
- For each row: run its windows (several per pass if memory allows) under the device's autocast, with the window
  coordinates (`with_coord`) and `transpose=True` as MONAI passes them; add each window's 117 scores into the
  buffer.
- After a row, the slices before the next row's start are final: divide by the count, hand that slab to label
  choice (3.6), then shift the buffer by the row step.
- After the last row, finalise the rest.
- Remove MONAI's padding before labels are chosen, exactly as MONAI crops it.

### 3.6 Label choice and mapping back

- **`original`:** for each final slab, find the CT voxels whose world position, mapped into the 1.5 mm grid, falls
  inside the slab (plus a one-voxel margin for interpolation); interpolate the 117 scores at those positions in
  float32; apply the label rule; write those voxels' labels into the CT-grid label map. Voxels outside the cropped
  box stay 0. Each CT voxel is written once.
- **`nvidia`:** apply the label rule on the slab at 1.5 mm and collect the 1.5 mm label volume (small, uint8); at
  the end, undo the orientation and crop, and resample onto the CT grid with SimpleITK nearest-neighbour.
- Output: uint16 SimpleITK image with the CT's exact size, spacing, origin and direction, plus the 117-entry
  `{id: name}` from the shared list.

### 3.7 Timings and logs

- Stage times: preprocessing, inference, mapping back (the way they are reported depends on step 2's decision).
- Peak device memory and peak RAM per scan.

### 3.8 8 GB cap test (tools script, not shipped)

- Run the engine on the scans that went over 8 GB in the demo, with the GPU capped at 8 GB:
  - wide 768 × 768 AMOS scans: `amos_0052`, `amos_0015`, `amos_0048` and the fourth one from the demo's
    `engine_evals_per_scan.csv`;
  - long scans: `kits23_case_00165` (734 slices), `kits23_case_00544`.
- Record peak memory per stage. If any fails, stop and choose a bound with the user: column groups (identical
  labels, most code), CPU accumulation for those scans only (simple, slower), or fp16 accumulation for those scans
  only (one line, needs a label check). About 100–150 lines and a day if needed.

## Tests (`tests/segmentation/test_nv_segment_ct.py`)

**Fast (default, CPU, no weights):** a small stand-in network that returns 117 deterministic channels replaces
VISTA3D, so stitching and geometry are tested without the 872 MB model.

| Check | Expected |
|---|---|
| Window plan | Same window positions and padding as MONAI's `sliding_window_inference` for several shapes (smaller than 128 on an axis, exactly 128, odd sizes) |
| Streaming = whole volume | Scores identical (float32) to MONAI's `sliding_window_inference` with constant blending and replicate padding; labels identical for both label choices |
| `original` in streaming | Equals interpolating the whole score volume at once, then choosing labels |
| Label rule | Background where all ≤ 0; ties resolved as `argmax` does |
| Resampling | Our `grid_sample` resampling matches MONAI's `Spacingd` (float32) on shapes and affines, including an oblique direction matrix |
| Mapping back, synthetic oblique CT | A synthetic CT with a known shape (e.g. a sphere) on an oblique, anisotropic grid lands back in the right place on the CT grid for both label choices; output geometry identical to the CT |
| Crop | Labels outside the cropped box are 0 |
| Device choice | Respects the order; CPU fallback when nothing else is available |

**Slow (opt-in, real weights, a GPU):**
- One real scan end to end on each available device; output geometry equals the CT's; peak memory and stage times
  logged.
- The 8 GB cap test (3.8), run on cream and then on the RTX 5060 laptop.

## Runs on your machines

| Where | What | Time |
|---|---|---|
| cream | 8 GB cap test on the 6 scans | ~15 min |
| RTX 5060 laptop | Same 6 scans, real 8 GB | ~15 min |
| Mac, Arc 140V | One real scan each (slow test) | a few minutes |

## Out of scope

- Equivalence against the demo's maps and the precision choice for Arc and Mac (step 4).
- Post-processing (step 5), add-ons (step 6), the voxel limit and `/new-job` (step 7).
- Lesion clicks and tumour classes (future add-on, plan section 15).

## Open questions (settle in the issue)

1. **How the label-choice switch and a device override are set:** environment variables (e.g. for a machine where
   the GPU misbehaves), or constants in the code with no override.
2. **Windows per pass:** the rule for "from free memory", e.g. measure one window's peak during warm-up and fit as
   many as leave a safety margin, capped at a small number.
3. **Peak memory on MPS:** MPS has no peak counter. Sample current use after each window, or report RAM only on Mac.
4. **The fourth wide AMOS scan's id:** read it from the demo's per-scan CSV on cream.

## Done when

- Fast tests pass on the Mac; slow test passes on each available device.
- 8 GB cap results recorded in the issue (and a bound chosen with the user if needed).
- PR merged; step 4 can run.
