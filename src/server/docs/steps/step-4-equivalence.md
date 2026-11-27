# Step 4: equivalence test (on cream)

Part of the [segmentation plan](../segmentation-plan.md) (sections 5, 14; section 13, step 4).

| | |
|---|---|
| Depends on | step 3 (the engine) |
| Branch / PR base | from step 3's branch (or `main`); PR with `tools/benchmark/` scripts only |
| Unblocks | confidence to build steps 6–7 on the engine; the precision choice for Arc and Mac |
| Shipped code | 0 (tools only: ~200 lines + ~150 lines of scoring copied from the prototype) |
| Runs on your machines | yes: ~1 GPU hour on cream; a few scans on the Mac and the Arc 140V |

## Goal

Prove the new engine reproduces the demo before anything else is built on it: same labels voxel for voxel (within
float32 resampling noise), same Dice, and the same gain from choosing labels at the original resolution. Then decide
reduced precision on Arc and Mac.

No separate NVIDIA-pipeline environment: the demo's `nvidia` reference maps are the release pipeline's own output
(voxel-identical to the prototype's path), so comparing against them already covers "ours vs NVIDIA".

## Reference data (on cream)

`/cs/student/ug/2025/jborrajo/holorepo-gpu/engine_evals/eval4_labels/` (645 MB, backed up):

| Folder | Contents |
|---|---|
| `baseline/raw/<scan id>.nii.gz` | `nvidia` label choice, before clean-up (uint8) |
| `baseline/clean/` | `nvidia`, after the prototype's clean-up (uint16), as scored |
| `E4_original/raw/` | `original` label choice, before clean-up |
| `E4_original/clean/` | `original`, after clean-up |
| `manifest.csv` | per scan: dataset, CT path, size, spacing, the four map paths with SHA-256 |

- On the CT's own grid; values are NV-Segment-CT class ids (the shared list keeps them, so no mapping).
- 357 scans: 300 AMOS, 29 KiTS23, 28 MSD hepatic vessel. `kits23_case_00165` has none (its Eval 4 job ran out of
  GPU memory); it is still run here, for memory and time only.
- The reference was made with float64 resampling and MONAI's sliding window; ours uses float32 resampling and
  streaming. Expected agreement: about 99.996% (float32 alone) and 100% (streaming alone).

Demo reference numbers (AMOS, 300 scans, after clean-up): `nvidia` Dice 0.8602 / NSD 0.8622; `original` 0.8676 /
0.8598.

## Work, in stages

### 4.1 Scoring module (`tools/benchmark/scoring.py`)

Copied from the prototype's scoring code, unchanged in behaviour, so numbers compare like for like:
- Dice: 2|P∩G| / (|P| + |G|) on the CT's voxel grid.
- NSD at 2 mm: boundary = voxels removed by one 3D erosion with the 6-connected structure; Euclidean distances in mm
  from the voxel spacing, computed inside the two masks' combined box plus a margin; NSD = fraction of both masks'
  boundary voxels within 2 mm of the other boundary.
- Expert label on a different grid → resampled to the CT grid with nearest-neighbour.
- Empty prediction with expert voxels → Dice 0, NSD 0, included. No expert voxels → "absent", left out.
- Means over scored scans with a 10,000-sample bootstrap 95% CI (seed 0); failures listed separately.
- AMOS: the 15 organs, mapped to NV-Segment-CT ids exactly as the prototype's scoring code does (copy its mapping;
  prostate/uterus is expected to use 118 prostate, since 16 is never output, but confirm from the code).

Reused unchanged by step 8.

### 4.2 Equivalence script (`tools/benchmark/equivalence.py`)

For each scan in `manifest.csv`:
1. Read the CT path from the manifest (step 1's reader for NIfTI).
2. Run the engine **once**, producing both label choices from the same inference pass (one stream, both
   mappings), **before** post-processing.
3. Compare each with its reference `raw` map: voxel agreement (%), and the ids that differ most.
4. Score both against the expert labels with 4.1.
5. Log stage times, peak GPU memory, peak RAM; write one row per scan to a CSV, then a summary.

Check the SHA-256 of every reference map before using it.

### 4.3 Run on cream (CUDA, fp16)

- All 357 scans plus `kits23_case_00165`. About 9–10 s per scan with the model loaded once: roughly 1 GPU hour.
- Same 48 GB RAM and 10-minute caps per scan as the demo, so a runaway scan doesn't hide others.

### 4.4 Precision on the Mac and the Arc 140V

- A handful of scans each (about 5), including at least one wide 768 × 768 AMOS scan; copy only those scans' CTs and
  reference maps (a few MB each).
- Run each scan in fp32 and in the reduced precision that passed step 0's smoke (bf16 or fp16).
- Compare with the cream reference maps. Keep reduced precision on that device only if it agrees as well as fp32
  does (and fp32 itself agrees ≥ 99.9%).

## Pass conditions

| Check | Pass |
|---|---|
| Voxel agreement, each label choice vs its reference, every scan | ≥ 99.9% |
| AMOS mean Dice, each label choice (before clean-up) | Within 0.001 of the reference's mean computed from the same `raw` maps |
| Gain of `original` over `nvidia` (AMOS Dice) | ≈ +0.0074 (inside the demo's CI, +0.0070 to +0.0078) |
| Failures | None, except a documented memory limit on `kits23_case_00165` if the step 3 cap test already decided it |
| Mac / Arc | Reduced precision kept per device only where it matches fp32's agreement |

The demo's numbers above are after clean-up; for this comparison the reference means are recomputed from the `raw`
maps with the same scoring code, so post-processing (step 5) can't hide a difference.

## If something fails

- **Agreement below 99.9% on a few scans:** look at where the differences sit (scan border → padding or crop;
  everywhere by a half voxel → a geometry offset; one class → label rule). Fix in step 3's branch and rerun only the
  failing scans.
- **Agreement fine but Dice off:** check the scoring copy and the id mapping first.
- **Every scan off:** stop and compare preprocessing outputs with MONAI's on one scan before rerunning.

## Runs on your machines

| Where | What | Time |
|---|---|---|
| cream | `equivalence.py` on all scans | ~1 GPU hour |
| Mac | ~5 scans, fp32 and reduced precision | ~30 min |
| Arc 140V | ~5 scans, fp32 and reduced precision | ~30 min |

## Out of scope

- Post-processing and add-ons (steps 5, 6), the full benchmark (step 8).
- RTX 5060 laptop accuracy: CUDA fp16 is already checked on cream; the laptop's speed and 8 GB limit are steps 3 and
  8.

## Open questions (settle in the issue)

1. **Scans for the Mac and Arc check:** which ~5 (suggestion: 3 AMOS including one wide, 1 KiTS, 1 MSD).
2. **Where the CSV and summary go:** committed under `docs/` as a record, or only pasted into the issue.

## Done when

- Pass conditions met on cream; results summary in the issue.
- Precision per device recorded in the plan (section 5) and `devices.py`.
- PR (tools only) merged.
