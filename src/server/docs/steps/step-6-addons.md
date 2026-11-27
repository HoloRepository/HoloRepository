# Step 6: TotalSegmentator add-ons

Part of the [segmentation plan](../segmentation-plan.md) (section 7; section 13, step 6).

| | |
|---|---|
| Depends on | step 2 (shared names, add-on lookup table), step 3 (the base label map the crop comes from) |
| Branch / PR base | from step 3's branch (or `main` once it is merged) |
| Unblocks | step 7 |
| Shipped code | ~180 lines, `src/segmentation/addons.py` (+ ~50-line RAM script in `tools/`, not shipped) |
| Runs on your machines | yes: RAM check on the Mac and the 32 GB laptops; a few scans on cream |

## Goal

Two optional add-ons, switched on per job: **kidney** (TotalSegmentator `kidney_cysts`) and **liver**
(TotalSegmentator `liver_vessels`). Each runs only around the organs NV-Segment-CT already found, and merges its
result into the base label map by a fixed rule. A failing add-on never fails the job.

## Decisions that apply (from the plan)

- Optional dependency: the `addons` extra, **TotalSegmentator 2.18.0** pinned. Licences of `liver_vessels` and
  `kidney_cysts` checked by hand once (neither is marked commercial in 2.18.0); no run-time check.
- **Weights downloaded at startup if the extra is installed; models loaded on each call.**
- **Skipped when the organ is missing or tiny.**
- Run through `totalsegmentator.nnunet.nnUNet_predict_image` with **NV-Segment-CT's organs as the crop mask** and
  TotalSegmentator's own task settings. TotalSegmentator crops, runs and pastes back itself. No whole-body model,
  no patching.

  | Add-on | Task | Crop organs (TotalSegmentator names) | Margin | Other settings (from TotalSegmentator's task config) |
  |---|---|---|---|---|
  | liver | `liver_vessels`, task 8 | `liver` | 20 voxels | trainer `nnUNetTrainer`, no resampling, `3d_fullres`, fold 0 |
  | kidney | `kidney_cysts`, task 789 | `kidney_left`, `kidney_right`, `liver`, `spleen`, `colon` | 10 voxels | resample 1.5 mm, trainer `nnUNetTrainer_DASegOrd0_NoMirroring`, `3d_fullres`, fold 0 |

- The CT is converted from SimpleITK (LPS) to an **in-memory nibabel image (RAS)**; so is the crop mask.
- **Merge:** add-on voxels overwrite NV-Segment-CT only where the add-on finds a structure.
  - **Liver vessels → id 25** (`hepatic vessel`). NV-Segment-CT never outputs 25 in automatic mode, so this is the
    add-on alone. Reference (MSD hepatic vessel, 28 scans): Dice 0.664, NSD 0.812.
  - **Kidney cysts: add-on cysts only.** When the kidney add-on runs, NV-Segment-CT's cysts are first turned back
    into their kidney (116 → 14, 117 → 5); then the add-on's left cyst → 116, right cyst → 117. Demo eval: Dice
    0.513 vs 0.295 for the prototype's per-side rule.
  - When the kidney add-on is **off**, NV-Segment-CT's own cysts stay as they are.
- **Few worker processes** (TotalSegmentator's resampling and saving threads kept at 1) to keep RAM down on 16–32 GB
  machines. The prototype's add-on reported 23–27 GB median (an over-count summed across workers).
- **Device:** the same GPU as NV-Segment-CT. On Intel Arc, try XPU and fall back to the CPU on error.
- **A failing add-on never fails the job:** it is logged and the job returns NV-Segment-CT's result.

## Work, in stages

### 6.1 Startup

- If `totalsegmentator` imports: set its home folder (open question 1) and download the weights for tasks 8 and 789
  if missing (`download_pretrained_weights`). If it doesn't import: the add-ons are unavailable and step 7 rejects
  a job that asks for one (open question 4).

### 6.2 Name mapping

- TotalSegmentator → shared names, used for the crop and the merge:
  `kidney_left` → `left kidney`, `kidney_right` → `right kidney`, `liver` → `liver`, `spleen` → `spleen`,
  `colon` → `colon`; `kidney_cyst_left` → `left kidney cyst`, `kidney_cyst_right` → `right kidney cyst`,
  `liver_vessels` → `hepatic vessel`.
- Crop organ lists and margins read from TotalSegmentator's own task config (`map_tasks_config.py`) where possible,
  so a TotalSegmentator upgrade can't silently diverge from it; resolved through the mapping above.

### 6.3 Crop mask and skip rule

- Binary mask of the crop organs from the base label map (by shared id).
- Skip the add-on when its main organ is missing or below the size threshold (open question 2): liver for the liver
  add-on; the kidneys for the kidney add-on (skipped only when both are missing or tiny; one kidney is enough to
  run).

### 6.4 LPS → RAS, in memory

- Build a nibabel image for the CT and one for the mask from the SimpleITK arrays and geometry (axis order and the
  LPS → RAS sign flip of the first two axes).
- Convert the add-on's output back onto the SimpleITK grid the same way.

### 6.5 Run

- Call `nnUNet_predict_image` with: the CT image, the task id, trainer, model, fold, resampling from the table, the
  mask as `crop`, the margin as `crop_addon`, `nr_threads_resampling=1`, `nr_threads_saving=1`, the device, quiet.
  It returns the add-on's labels on the CT grid (it undoes its own crop).
- Wrap the call: any exception → log, mark the add-on as failed in the job's record, continue with the base map.
- On XPU: on an exception, retry once on the CPU before giving up.

### 6.6 Merge

- Kidney: 116 → 14 and 117 → 5 in the base map; then add-on left cyst → 116, right cyst → 117 where the add-on > 0.
- Liver: add-on vessels → 25 where the add-on > 0. The tumour label is open question 3.
- If both add-ons run: kidney first, then liver (they touch different organs; a fixed order keeps results
  reproducible).

### 6.7 Timing and RAM

- Each add-on's time recorded as its own stage.
- RAM script (`tools/`): runs one add-on on one scan and records the **real** peak RAM of the whole process tree
  (shared memory counted once), for step 6's check.

## Tests (`tests/segmentation/test_addons.py`)

**Fast (no TotalSegmentator weights; the predict call replaced by a stand-in):**

| Case | Expected |
|---|---|
| Crop mask | Exactly the crop organs' voxels, by shared id |
| LPS/RAS round trip | An asymmetric synthetic volume (different values in each corner, oblique direction) goes SimpleITK → nibabel → SimpleITK unchanged, voxel for voxel and in world position |
| Kidney merge | NV cysts 116 / 117 become 14 / 5; add-on cysts become 116 / 117; voxels the add-on doesn't mark are unchanged |
| Liver merge | Add-on vessels become 25; everything else unchanged |
| Both add-ons | Kidney then liver; result independent of the order the job lists them |
| Organ missing or tiny | Add-on skipped, base map returned unchanged, skip recorded |
| Stand-in raises | Base map returned; failure recorded; no exception escapes |
| XPU failure | Retried on the CPU once (with a stand-in that fails on the first device) |
| Name mapping | Every TotalSegmentator name used resolves to a shared id |

**Slow (opt-in, TotalSegmentator weights, a GPU):** a few scans on cream (e.g. 3 KiTS23, 3 MSD hepatic vessel):
compare with the references in `engine_evals/addon_labels/` (kidney: `kidney_cysts/addon_only/`; liver:
`liver_vessels/final/`, id 25 only) and report Dice and voxel agreement. An early warning, not a pass/fail gate
(the crop comes from NV-Segment-CT's masks, not the prototype's box, so small differences are expected; step 8 has
the real pass condition).

## RAM check (on your machines)

Real peak RAM on the largest KiTS23 and MSD scans (by voxel count), each add-on, with workers kept at 1:

| Machine | Memory |
|---|---|
| Mac (M4) | 16 GB |
| Arc 140V | 32 GB |
| RTX 5060 laptop | 32 GB |

If a scan doesn't fit, stop and decide in the issue (e.g. CPU resampling settings, or a per-machine voxel limit for
add-ons).

## Out of scope

- Head and neck (dropped), lesion clicks and NV-Segment-CT's own tumour classes (future add-on).
- The form fields that switch the add-ons on (step 7).
- Scoring against the full datasets (step 8).

## Open questions (settle in the issue)

1. ~~Where TotalSegmentator's weights live~~ Settled 2026-11-27: the server sets `TOTALSEG_HOME_DIR` to
   `$HOLO_DATA_DIR/totalsegmentator` (one data root, step 0).
2. **"Tiny" threshold** for skipping: e.g. under 10 mL for the liver, under 5 mL for both kidneys together, or
   simply "absent".
3. **Liver tumours from the liver add-on.** `liver_vessels` outputs two labels: `liver_vessels` (1) and
   `liver_tumor` (2). The plan only covers vessels. Options:
   - drop the tumour label (vessels only, as the plan says now);
   - write it as NV-Segment-CT's 130 `liver tumor` (parent: liver), which brings tumour output forward from the
     "future add-on" list without being evaluated.
   The demo's liver reference scored vessels only.
4. **Add-on requested but the extra isn't installed:** reject the job with 400, or run without it and say so in
   the job status message.

## Done when

- Fast tests pass; slow test run on cream with results in the issue.
- RAM check done on the three machines.
- Open questions 1–4 settled and recorded in the plan (section 7).
- PR merged.
