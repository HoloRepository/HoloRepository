# Step 5: post-processing

Part of the [segmentation plan](../segmentation-plan.md) (section 6; section 13, step 5).

| | |
|---|---|
| Depends on | step 2 (organs are chosen by shared name) |
| Branch / PR base | from step 2's branch (or `main`); runs in parallel with step 3 |
| Unblocks | step 7 |
| Shipped code | ~60 lines, `src/segmentation/postprocess.py` |
| Runs on your machines | one CPU-only check on cream (~10 min) |

## Goal

Remove stray pieces from organs that should be a single piece, the same way the prototype did, so the final label
map matches what the demo scored.

## Decisions that apply (from the plan)

- **Single-piece organs** drop connected pieces (26-connected) smaller than **20% of their largest piece**.
- Organs, by shared name (`labels.json` names): `liver`, `spleen`, `pancreas`, `gallbladder`, `stomach`,
  `left kidney`, `right kidney`, `left adrenal gland`, `right adrenal gland`, `bladder`, `heart`, `brain`,
  `thyroid gland`, `sternum`, `sacrum`, `prostate`.
- **Tubes are left alone:** aorta, inferior and superior vena cava, esophagus, trachea, spinal cord (and everything
  else not in the list).
- Computed on each organ's **bounding box**, not the whole scan (fast).
- **Removed voxels become background** (0).
- **No overlap-painting step:** one label per voxel already rules out overlaps.
- Runs **after** the add-ons are merged (pipeline order in plan section 3), so it sees the final kidneys.
- From the demo's NSD check: this rule (the prototype's own clean-up, "rule ii") adds Dice +0.0003 and NSD +0.0007
  for both label choices. The 0.02 mL speck rule changes nothing, so it is **not** added.

## Work, in stages

### 5.1 Organ list

- The 16 names above as one constant, resolved to ids through `labels.py` at import, so a wrong name fails at
  startup (step 2's test "names used elsewhere" gains this list).

### 5.2 `remove_stray_pieces(label_map)`

For each organ id present:
1. Bounding box of that id.
2. Connected pieces inside the box with 26-connectivity (`scipy.ndimage.label` with a 3 × 3 × 3 structure).
3. Keep pieces with at least 20% of the largest piece's voxel count; set the others to 0.

Works in place on the label map's array; geometry untouched. Organs that are absent are skipped.

### 5.3 Timing

- Recorded as the "post-processing" stage in the job's timings (step 7).

## Tests (`tests/segmentation/test_postprocess.py`, fast)

| Case | Expected |
|---|---|
| Liver with one big piece and a piece at 10% of it | Small piece → 0 |
| Liver with a second piece at 30% | Both kept |
| Exactly 20% | Kept (rule is "smaller than 20%") |
| Two pieces touching only at a corner | One piece (26-connectivity) |
| Aorta in two pieces | Untouched (not in the list) |
| Other labels inside the organ's box | Untouched |
| Organ absent | No error |
| Geometry | Same size, spacing, origin, direction |

## Check against the demo (CPU only, on cream)

Apply `remove_stray_pieces` to every `raw` map in `engine_evals/eval4_labels/` (both label choices) and compare with
the matching `clean` map. Expected: identical, because both are the prototype's rule.

If they differ, the prototype's clean-up does something the plan's list doesn't (a different organ list, or an
extra rule). Report which ids differ and settle it in the issue before merging; the step 8 Dice target (0.8654)
assumes the demo's clean-up.

## Out of scope

- Any other clean-up (holes, smoothing): not in the plan.
- Add-on merging (step 6).

## Open questions (settle in the issue)

None expected. The cream comparison is the check that the organ list is right.

## Done when

- Tests pass; cream comparison identical (or the difference explained and decided).
- PR merged.
