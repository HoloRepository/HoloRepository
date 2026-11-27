# Eval brief: settings to decide for the new segmentation engine (run on cream)

## Context

A new server (HoloRepository `src/server`) is re-implementing the CT → label map part of this prototype's
NV-Segment-CT stack. Its plan is fixed (`docs/segmentation-plan.md` in that repo). A few choices were left to be
**decided by evaluation on this prototype**, on the cream GPU server, using the benchmark data and scoring the
prototype already has (`backend/holo/segmentation/bench.py`, `scoring.py`, `download_bench_data.py`).

**Your job:**
1. Add each variant below to the prototype **behind a flag**.
2. Run the evals in the order given.
3. Report at every milestone.
4. Write a final report with the evidence for each decision.

**Ground rules:**
- Keep the prototype's default behaviour unchanged.
- Don't merge to `main`. Work on a branch such as `evals/new-engine`.
- If something here doesn't match what you find in the code, stop and report it rather than guessing.

## Scope and order

| Phase | Evals | Scale |
|---|---|---|
| 1 | 0 baseline, 1 streaming, 2 mixed precision, 3 float32 resampling, 4 label choice | As listed per eval |
| 2 | 5 overlap 0.5, 6 left-right flip (2 passes), **screening only** | 50 scans each |

**Don't run anything beyond this.** In particular, no 300-scan runs for overlap or flips, even if screening looks
good; the user decides that from the phase 2 report. Out of scope: the add-ons, the clean-up effect, and the
combined configuration. The new repo's own benchmark covers those.

## Milestones and reporting

After each milestone, stop briefly and post a **milestone note** in the chat, and append it to `REPORT.md`:
- what ran: the eval, the scans, and how many finished out of how many;
- the headline numbers (see each eval);
- whether the proposed rule was met;
- anything surprising, with scan ids;
- the time used so far against the budget.

| Milestone | Done when |
|---|---|
| M0 | Variants implemented behind flags, plus a 3-scan dry run of every variant without errors |
| M1 | Eval 0 + Eval 4 finished (one pass) |
| M2 | Eval 1 finished |
| M3 | Evals 2 and 3 finished. **End of phase 1:** write the phase 1 summary |
| M4 | Eval 5 screening finished |
| M5 | Eval 6 (left-right flip) screening finished. **End of phase 2:** write the final report |

If Eval 0 doesn't reproduce the baseline (see Eval 0), stop at M1 and wait for the user.

## Caps

| Cap | Rule |
|---|---|
| Per scan | The existing per-process memory cap, plus a **10-minute timeout**. A scan that hits either is recorded as failed and not retried. |
| Early stop (Evals 5, 6) | Run all 50 screening scans, then report. Don't extend. |
| Total budget | **About 8 GPU hours** for phases 1 and 2 together. If you're heading past it, stop at the next milestone and report what's done. |

## Facts about the NV-Segment-CT release

Hugging Face `nvidia/NV-Segment-CT`, revision `afb51518689f71e6abb367ee6301b2cd0225c66a`, files
`vista3d_pipeline.py` and `scripts/inferer.py`:

- **Preprocessing, in this order:**
  1. `Spacingd` to 1.5 mm, bilinear, **dtype float64** by default.
  2. `CropForegroundd`: voxels > 0 HU, margin 10, `allow_smaller=True`.
  3. `ScaleIntensityRanged`: -963.8247715525971 to 1053.678477684517 → 0 to 1, clipped.
  4. `Orientationd` to RAS.
- **Inference in automatic mode (no click points):** `Vista3dInferer` calls `SlidingWindowInfererAdapt(roi_size=128³,
  sw_batch_size=1, with_coord=True, padding_mode="replicate")` with **no overlap argument**. The overlap is
  therefore MONAI's default **0.25**, with **constant** blending. The pipeline's `overlap=0.3` and Gaussian
  blending only apply to click mode.
- **Mixed precision** is hard-coded as `torch.autocast("cuda")`, which means fp16.
- **Choosing labels:** `VistaPostTransformd` takes, per voxel, the most likely class, or background if every class
  scores ≤ 0. `Invertd` with nearest-neighbour then maps the labels back to the original grid.
- **"Everything" classes (117):** 1–132 minus {2, 16, 18, 20, 21, 23, 24, 25, 26, 27, 128, 129, 130, 131, 132}.

## General rules

1. **Hardware and environment.** Use cream only, with nothing else running on the GPU. Record in the report:
   - the GPU model, driver and CUDA version;
   - the versions of torch and monai;
   - the prototype commit.
2. **Isolation.** Run each scan in its own process with the memory cap, as `bench.py` already does.
3. **Timing.**
   - Exclude model loading and one warm-up window from timings.
   - Time each stage separately: CT loading, preprocessing, inference, label choice, mapping back to the CT grid,
     post-processing, saving.
   - Report the median and p90 per variant.
4. **Memory.**
   - Peak system RAM per scan (as `bench.py` records it).
   - Peak GPU memory per scan: call `torch.cuda.reset_peak_memory_stats()` before the scan and
     `torch.cuda.max_memory_allocated()` after it.
5. **Accuracy.**
   - Dice and NSD per organ and per scan, exactly as `scoring.py` computes them today, with the same tolerance and
     label mapping.
   - Count missing organs the way the scorer does today.
6. **Paired statistics.**
   - Compare every variant with the baseline on the **same scans**.
   - Report the mean per-scan difference in Dice and NSD, with a 95% bootstrap confidence interval (10,000
     resamples over scans), overall and per organ.
   - Name the organs that move most.
7. **Equivalence metrics.** Where a variant should give the same output as the baseline, report:
   - the percentage of voxels with the same label, overall and for the worst scan;
   - per-organ Dice between the variant's and the baseline's label maps (not against ground truth).
8. **Outputs.**
   - One CSV row per scan and variant: scan id, dataset, variant, per-organ Dice and NSD, per-stage times, peak RAM,
     peak GPU memory, and status (finished, out of memory, timeout, or error).
   - Save every label map.
9. **Decision rules.** The rules below are proposals. State whether each was met; the final call is the user's.

## Datasets

| Set | Use | Prototype reference |
|---|---|---|
| AMOS22, all 300 CTs, 15 organs | Main accuracy set | Mean Dice 0.860, NSD 0.862. Weakest organs: adrenals 0.74, duodenum 0.76, prostate/uterus 0.81 |
| KiTS23 + MSD hepatic vessel, the prototype's 58 scans | Timing and memory | Segmentation median 26 s (kidney scans) and 15 s (liver scans); peak RAM median 4.0 GB and 3.2 GB |
| The 734-slice scan | Memory stress case | 30 GB peak RAM |
| **Quick subset:** the first 50 AMOS scans in sorted id order | Smaller evals and screening | — |

---

# Phase 1

## Eval 0: baseline (run in one pass with Eval 4)

**Purpose:** confirm that today's environment reproduces the prototype's numbers. Its output is the baseline for
every other eval.

**Run:** the prototype's default stack (NV-Segment-CT only, add-ons off, clean-up on) on all 300 AMOS scans and the
58 KiTS/MSD scans. Compute Eval 4's variant in the same process, from the same inference.

**Expected:** AMOS mean Dice 0.860 ± 0.002, and times and RAM close to the reference. **If not, stop at M1 and
report the gap.**

**Baseline settings:**
- labels chosen at 1.5 mm,
- overlap 0.25,
- no flips,
- fp16,
- float64 resampling,
- clean-up on.

**M1 headline:**
- mean Dice and NSD;
- per-organ Dice compared with the prototype reference;
- median times on the 58 scans;
- peak RAM.

## Eval 4: choosing labels at the original resolution

**Decides:** the default label-choice method:
- `nvidia`: labels chosen at 1.5 mm, then copied onto the CT grid with nearest-neighbour (the baseline);
- `original`: the scores interpolated onto the CT grid, then labels chosen there.

**Variant `original` (from the same inference pass as Eval 0):**
1. Take the averaged 117-channel scores (logits, before any label choice) on the 1.5 mm cropped RAS grid.
2. For every original CT voxel, find its position on that grid through world coordinates. Go from original voxel
   to world coordinates with the original image's affine, then from world coordinates to score-grid voxel with the
   inverse of the preprocessed image's affine. Use the same convention (RAS or LPS) on both sides. The
   preprocessed affine already includes the spacing, the crop offset and the orientation.
3. Interpolate the logits trilinearly at those positions with `torch.nn.functional.grid_sample` on the GPU.
   - Mind the voxel-centre convention: normalised coordinates with `align_corners=True` must map voxel index 0 to
     -1.
   - Work in chunks along the original z axis to bound memory.
4. Choose labels with the `VistaPostTransformd` rule.
5. Voxels outside the score grid (outside the crop box) are background.
6. Apply the same clean-up as the baseline before scoring.

**Sanity check first:** on 3 scans, overlay `nvidia` and `original` on the CT. Check that `original` is aligned (no
half-voxel shift or flip) before running all 300.

**Report:**
- Dice **and NSD** differences with confidence intervals, overall and per organ;
- the weak organs (adrenals, duodenum, oesophagus, gallbladder, prostate/uterus) on their own;
- the extra time for the label-choice stage (median and p90);
- peak GPU memory.

**Proposed rule:** make `original` the default if:
- the lower end of the mean Dice confidence interval is above 0;
- no organ drops by more than 0.005.

If it adds more than 3 s per scan at the median, state the time-for-accuracy trade-off explicitly.

## Eval 1: streaming sliding-window inference

**Decides:** whether the new engine replaces MONAI's sliding-window helper with streaming inference.

**What streaming is:**
- Run exactly the same windows as `SlidingWindowInfererAdapt`, ordered along the last axis (S in RAS).
- Keep a float32 running total and a window count only for the part of the scan still in progress.
- Once no remaining window touches a slab, that slab's average scores are final. Choose its labels with the
  `VistaPostTransformd` rule, then discard the scores.
- The full 117-channel score volume is never stored.

**Implementation:**
- **Window positions.** Reproduce MONAI's exactly:
  - replicate-pad to at least 128 per axis, as MONAI does;
  - get the steps from `monai.inferers.utils._get_scan_interval` (96 for overlap 0.25);
  - get the windows from `monai.data.utils.dense_patch_slices`.
- **Network call.** The same as MONAI's: `patch_coords`, `transpose=True`, `class_vector` = the 117 classes, and the
  same autocast.
- **Blending and precision.**
  - Blending is constant: the score is the total divided by the count.
  - Keep the running totals in float32 even under fp16 autocast.
- **Finishing up.**
  - Remove the padding before choosing labels.
  - Choose labels on the GPU.
  - Map back with the prototype's existing nearest-neighbour step, so only inference changes.

**Runs:**
- 1a, equivalence: the 50-scan quick subset, comparing streaming with the baseline output from Eval 0.
- 1b, memory and time: the 58 KiTS/MSD scans with streaming.
- 1c, stress: the 734-slice scan, with streaming and with the baseline method.
- **Every run uses one window per pass.** Record peak GPU memory: the new server must fit in **8 GB of VRAM**
  (RTX 5060).

**Report:**
- equivalence metrics and the Dice difference (1a);
- per-stage times, peak RAM and peak GPU memory, median and maximum, compared with Eval 0 on the same 58 scans (1b);
- for 1c: finished or failed, peak RAM, peak GPU memory and time for both methods.

**Proposed rule:** adopt if all of the following hold:
- voxel agreement is at least 99.9% on every scan;
- the mean Dice difference is within ±0.001;
- peak memory is lower;
- time is no more than 5% worse.

## Eval 2: mixed precision

**Decides:** whether fp16 on NVIDIA leaves Dice unchanged.

**Variants:**
- 2a: no autocast (fp32);
- 2b: fp16, taken from the Eval 0 output for the same 50 scans.

**Runs:** the 50-scan quick subset.

**Report:**
- the Dice difference of fp16 against fp32, with its confidence interval;
- equivalence metrics;
- inference time and peak GPU memory for both.

**Proposed rule:** keep fp16 if, against fp32:
- the Dice difference is at least -0.001, with a confidence interval that includes 0;
- no organ drops by more than 0.005.

## Eval 3: float32 resampling

**Decides:** whether the new engine can resample in float32. It plans to use MONAI's geometry maths on the CPU and
float32 interpolation on the GPU, which Apple and Intel GPUs need.

**Variant:** `Spacingd(..., dtype=torch.float32)`, with the CT on the GPU. On CUDA this is the same computation the
new engine will do.

**Runs:** the 50-scan quick subset, comparing the variant with the baseline output from Eval 0.

**Report:**
- equivalence metrics;
- the Dice difference with its confidence interval;
- preprocessing time for both.

**Proposed rule:** adopt if voxel agreement is at least 99.9% and the mean Dice difference is within ±0.001.

**At M3, write the phase 1 summary:** one row per decision for Evals 1–4, plus the baseline anchor.

---

# Phase 2 (screening only)

## Eval 5: window overlap 0.5

**Screens:** whether raising the window overlap from NVIDIA's 0.25 to 0.5 is worth a full run.

**Variant:**
- Pass `overlap=0.5` explicitly to `SlidingWindowInfererAdapt`, or to the streaming inferer if Eval 1 passed.
- Keep constant blending and NVIDIA's label-choice method.

**Runs:** the 50-scan quick subset, compared with the baseline output from Eval 0.

**Report:**
- Dice and NSD differences with confidence intervals, per organ, with the weak organs highlighted;
- the time ratio against the baseline;
- peak memory.

**Screening rule:** recommend a full 300-scan run only if mean Dice gains at least +0.003 on the subset. Adopting
it later would need at least +0.005 on all 300, with the lower end of the confidence interval above 0. Always
state the time cost next to the gain.

## Eval 6: left-right flip test-time augmentation (E6_flip_lr)

**Screens:** whether averaging the scan with its left-right mirror is worth a full run.

**Changed after M0.**
- The original flip4 (one flip per axis) can't work for this model:
  - it labels left and right structures separately, so a mirrored pass puts each side's evidence on the wrong side
    (amos_0004 lost 0.48 mean Dice);
  - the other two flips show upside-down or back-to-front anatomy.
- VISTA3D's training used no flips: the model-zoo `train.json` and the research repo's stage-1 config have no flip,
  rotation or affine transform.
- So only the left-right flip is screened.

**Variant E6_flip_lr (2 passes):**
1. Run the original, then a left-right flipped copy, and flip the second pass's scores back.
2. Paired classes average their own score with their mirror partner's score from the flipped pass, in float32
   system RAM.
3. Unpaired classes keep the original pass only. These are the midline and single-side organs, the right middle
   lobe and the left atrial appendage.
4. Choose labels with NVIDIA's method.

The swap table is built from `metadata.json` by name and listed with class ids in `REPORT.md`.

**Extra reporting:** the AMOS paired organs (kidneys, adrenals) separately from the unpaired ones.

**Runs:** the 50-scan quick subset, compared with the baseline output from Eval 0.

**Report and screening rule:** the same as Eval 5.

---

## Final report layout (at M5)

1. **Summary table:** one row per eval (0–6), with these columns:
   - decision,
   - variant,
   - ΔDice (95% CI),
   - ΔNSD (95% CI),
   - median time change,
   - peak memory change,
   - rule met (yes/no),
   - recommendation.

   For Evals 5 and 6, the recommendation is "run full / don't run full".
2. **Environment and baseline anchor (Eval 0).**
3. **One section per eval:** method, the exact flags used, tables, and anything surprising.
4. **Proposed settings for the new server** from phase 1:
   - streaming on or off,
   - precision,
   - resampling dtype,
   - label-choice method.
5. **Anomalies:** failed scans, timeouts, out-of-memory events, and scans where variants disagree a lot. Give scan
   ids.
6. **Budget used:** GPU hours per eval.
