# Step 1: upload → standard CT

Part of the [segmentation plan](../segmentation-plan.md) (sections 2, 9; section 13, step 1).

| | |
|---|---|
| Depends on | step 0 (SimpleITK in the dependencies) |
| Branch / PR base | from step 0's branch (or `main` once step 0 is merged); runs in parallel with step 2 |
| Unblocks | step 7 (wiring into `/new-job`); steps 3–6 can use test NIfTI files without it |
| Shipped code | ~200 lines, `src/segmentation/ct_input.py` |
| Runs on your machines | no; unit tests only |

## Goal

Turn one upload (a ZIP of a DICOM folder, or one NIfTI file) into the **standard CT** every later stage uses: a
SimpleITK image in Int16 Hounsfield units on the scan's original grid, with its geometry (size, spacing, origin,
direction) kept exactly. Anything unusable is rejected with a clear message that step 7 returns as **400**.

## Decisions that apply (from the plan)

- Bad uploads return **400 from the request itself**: the CT is unzipped and read before the job id is returned.
  This step raises one error type with a user-readable message; step 7 maps it to HTTP 400.
- **ZIP:** reject entries that escape the target folder (`../`, absolute paths). No size or file-count limits for
  now (decided 2026-11-27, documented gap in plan section 15).
- **DICOM:** the series with the most files (ties → lowest `SeriesInstanceUID` (always present; not meaningful, just repeatable), logged); `Modality` must be `CT`; non-uniform slice
  spacing (slice gaps more than 1% of the spacing off even → non-uniform) or gantry tilt → 400.
- **NIfTI:** accepted without a modality check; 4D uses the first time point; anything not 3D → 400.
- **Output:** Int16 HU CT on the original grid, kept in memory for the job and written once as `ct.nii`
  (uncompressed). Meshing's `labels.nii` will sit on exactly this grid (section 8).
- Documented gap, not fixed here: `/new-job` is `async def`, so reading the upload blocks the event loop. Whoever
  owns the server runtime moves it to a worker thread.

## Work, in stages

### 1.1 Interface

- `read_upload(file, file_type) -> sitk.Image` (Int16 HU, original grid), raising `UploadError(message)` for every
  rejection.
- `write_ct(image, path)` writing `ct.nii`.
- `file_type` comes from the existing `FileType` enum. Only `dicom` and `nifti` are valid uploads;
  `nifti_segmented` and `mz3` are output types → `UploadError`.
- The upload is read from the `UploadFile`'s spooled file without loading the whole thing into memory twice.

### 1.2 Safe ZIP extraction (`file_type = dicom`)

- Check the upload is a ZIP (magic bytes, not the file name).
- Extract into a fresh temporary folder, deleted when reading is done (also on error).
- Reject the whole upload if any entry:
  - has an absolute path, a drive letter, or resolves outside the folder after normalising (`..`, backslashes on
    Windows);
  - is a symlink.
- Corrupt ZIP → `UploadError`.

### 1.3 DICOM series → image

- Find series in every folder of the extracted tree (SimpleITK's GDCM series lookup is per folder).
- Choose the series with the most files. Ties: the lowest `SeriesInstanceUID`; log which series was chosen and why.
- Read `Modality` (0008,0060) from the chosen series' first file; anything other than `CT` → `UploadError`.
- Read the series with SimpleITK's series reader (sorted by position; rescale slope and intercept applied, so values
  are HU).
- Reject, with a message that says why:
  - **non-uniform slice spacing**: positions along the slice normal differ from evenly spaced by more than 1% of
    the slice spacing (missing slices also show up here);
  - **gantry tilt**: the step between slices is not along the slice normal from `ImageOrientationPatient`;
  - fewer than 2 slices (not 3D).

### 1.4 NIfTI → image (`file_type = nifti`)

- Accept `.nii` and `.nii.gz` (detected from content).
- 4D: keep the first time point. 2D, or 5D and above: `UploadError`.
- Unreadable file → `UploadError`.
- No modality check (NIfTI has no reliable field for it).

### 1.5 Standardise

- Cast to Int16 with rounding and clipping to the Int16 range. Most CT is stored as Int16 already; for float NIfTI,
  rounding changes values by under 0.5 HU.
- Keep size, spacing, origin and direction exactly as read; no reorientation and no resampling here.

### 1.6 Write `ct.nii`

- Written once per job with SimpleITK, uncompressed.
- Called by the pipeline in step 7; this step only provides the function.

## Tests (`tests/segmentation/test_ct_input.py`, fast, no GPU)

Test data is generated in the tests (synthetic DICOM series written with SimpleITK, ZIPs built in memory). Nothing
large is committed.

| Case | Expected |
|---|---|
| Single DICOM series | Image read; HU values, size, spacing, origin and direction match what was written |
| Two series (e.g. a 3-slice localiser and a 40-slice CT) | The 40-slice series is chosen |
| Series in nested folders | Found |
| `Modality` = `MR` | `UploadError` naming the modality |
| Missing slice / uneven spacing | `UploadError` (non-uniform spacing) |
| Spacing off by under 1% (rounding) | Read |
| Two series with the same file count | Lowest `SeriesInstanceUID` chosen |
| Tilted series | `UploadError` (gantry tilt) |
| Corrupt ZIP, non-ZIP sent as `dicom` | `UploadError` |
| Entry with `../`, absolute path, backslash escape, symlink | `UploadError`; nothing written outside the temp folder |
| NIfTI 3D, `.nii` and `.nii.gz` | Read; geometry exact, including an oblique direction matrix |
| NIfTI 4D | First time point |
| NIfTI 2D | `UploadError` |
| `file_type` = `nifti_segmented` or `mz3` | `UploadError` |
| Float NIfTI | Rounded to Int16 |
| `write_ct` then read back | Identical voxels and geometry |
| Temp folder | Removed after success and after an error |

## Out of scope

- HTTP: status codes, the job id, storing files (step 7).
- Moving the read off the event loop (documented gap).
- Any resampling or orientation change (step 3 does that on its own copy).

## Open questions (settle in the issue)

1. **Limit values:** settled 2026-11-27: no limits for now; documented as a gap in plan section 15.
2. **Series ties:** settled 2026-11-27: the simplest fixed rule, lowest `SeriesInstanceUID`, logged. Either
   candidate is usually the same anatomy (two contrast phases or two reconstructions), and clients have no screen
   to choose a series.
3. **`ct.nii` compression:** settled 2026-11-27: uncompressed (fastest to write and read, ~2 bytes per voxel).
4. **Spacing tolerance:** settled 2026-11-27: 1% of the slice spacing (adapts to slice thickness; rounding never
   triggers it, a missing slice always does).

## Done when

- All tests pass on the Mac.
- `UploadError` messages are short enough to show to a client user.
- PR merged; step 7 can call `read_upload` and `write_ct`.
