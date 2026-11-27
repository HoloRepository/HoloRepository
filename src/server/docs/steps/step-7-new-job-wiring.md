# Step 7: wire into `POST /new-job`

Part of the [segmentation plan](../segmentation-plan.md) (sections 2, 3, 8, 11; section 13, step 7).

| | |
|---|---|
| Depends on | steps 1, 2, 3, 5, 6 merged |
| Branch / PR base | from `main` once the others are merged |
| Unblocks | step 8 (benchmark through `pipeline.py`); meshing can start consuming `labels.nii` |
| Shipped code | ~250 lines: `src/segmentation/pipeline.py`, `src/segmentation/runner.py`, changes in `src/main.py` and `src/models.py` |
| Runs on your machines | no; tests use a stand-in engine. One manual end-to-end upload on the Mac at the end |

## Goal

A client can upload a CT to `POST /new-job` with the add-on toggles, get a job id straight away (or a 400 for a bad
upload), poll the job's status, and find `labels.nii` + `labels.json` on disk for meshing when it is done.

## Decisions that apply (from the plan)

**API (section 2):**
- `model` names the base model only: `nv-segment-ct`. The `ts-specialist-*` values are removed here, together with
  the add-on form fields (decided 2026-11-27, step 2 open question 4); drop step 2's lookup-test exception for them.
- New boolean form fields **`kidney_addon`** and **`liver_addon`**, default `false`.
- Bad uploads return **400 from the request itself**: the CT is unzipped and read before the job id is returned.
- Regenerate `API contract.md` and `openapi.json` (`uv run python scripts/generate_contract.py`); tell the client
  team.

**Pipeline (section 3):** upload → standard CT (also written as `ct.nii`) → NV-Segment-CT → add-ons if toggled →
post-processing → `labels.nii` (uint16, CT grid) + `labels.json` → handed to meshing.

**Handover to meshing (section 8, agreed with the meshing teammate):**
- `labels.nii` (or `.nii.gz` at level 1): uint16 label map, 0 = background, on exactly the grid of the job's
  `ct.nii`. Written with SimpleITK; nibabel's `image.affine` gives RAS millimetres directly (what `meshing.py`
  already uses).
- `labels.json`: `{"<id>": {"name": "...", "parent": <id or null>}}` for every id present.
- Ids come from the shared list and don't change between models. Colours, display names, grouping belong to meshing.

**Runtime (section 11):**
- Engines loaded **once at startup**, single server process, warmed up.
- **Temporary runner:** one worker thread, a queue, job status in memory. Jobs are lost on restart. Documented as
  "replace with a real queue and storage".
- Job status `in_queue` → `in_progress` → `done` / `error` with a message.
- Each job logs the time of every stage (upload reading, preprocessing, inference, mapping back, add-ons,
  post-processing, writing) and a run record (package versions, model revision, device, precision, settings),
  written next to the outputs as **`run.json`**.

**Memory guard (section 5):** a maximum voxel count per job, set per machine by an environment variable; larger
scans fail with a clear message.

**Documented gap:** `/new-job` is `async def`, so reading the upload blocks the event loop; not fixed here.

## Work, in stages

### 7.1 Startup

- FastAPI lifespan: choose the device, load every engine in `ENGINES` and warm it up, prepare the add-ons (step 6's
  startup), start the runner's worker thread.
- If the weights are missing, behave as step 0 decided (download, or refuse to start with a clear message).

### 7.2 `POST /new-job`

1. Form fields: `file`, `file_type`, `model` (default `nv-segment-ct`), `kidney_addon`, `liver_addon`.
2. `read_upload` (step 1). `UploadError` → **400** with its message.
3. Voxel count over the limit → **400** with the limit in the message.
4. Add-on requested but unavailable → as step 6 question 4 decided.
5. New job id; create the job's folder; status `in_queue`; put the job (CT in memory, model, add-on flags, user) on
   the queue.
6. Return `Job(id)`.

### 7.3 `runner.py`

- One worker thread taking jobs from a queue, one at a time (the GPU runs one job).
- In-memory `job id → (user, JobStatus)`; status `in_progress` when picked up, `done` or `error` at the end.
- Any exception in a job → `error` with a short message for the client; full traceback in the log and `run.json`.
- Lost on restart (documented).

### 7.4 `pipeline.py`: one job end to end

1. Write `ct.nii` (step 1's `write_ct`).
2. `segment(ct, timer)` with the engine from `ENGINES[model]` (the job's shared stage timer).
3. Add-ons from `ADDONS` for each toggle (step 6); a failed add-on is recorded, the job continues.
4. `remove_stray_pieces` (step 5).
5. Write `labels.nii` on the CT grid and `labels.json` (`table_for` the ids present, step 2).
6. Write `run.json`: versions (Python, torch, MONAI, SimpleITK, TotalSegmentator if used), model revision, device,
   precision, overlap, label choice, resampling dtype, streaming, add-ons requested / run / skipped / failed,
   stage times, peak GPU memory and RAM, voxel count.
7. Return; the runner sets `done`.

`pipeline.py` is also what step 8 calls directly (without HTTP).

### 7.5 `GET /job-status`

- Return the runner's `JobStatus` for that job id, only to the user who created it (404 otherwise).

### 7.6 API contract

- Update `models.py` / `main.py` docstrings and field descriptions (the contract is generated from them), regenerate
  `API contract.md` and `openapi.json`, and send the client team the change: two new form fields, `model` values
  reduced, 400 responses documented.

## Tests (`tests/segmentation/test_new_job.py`, fast)

FastAPI's test client, a stand-in engine (returns a fixed label map), stand-in add-ons, the auth dependency
overridden with a test user, a temporary jobs folder.

| Case | Expected |
|---|---|
| Valid NIfTI upload | 200 with a job id; status goes `in_queue` → `in_progress` → `done`; `ct.nii`, `labels.nii`, `labels.json`, `run.json` exist |
| Valid DICOM ZIP | Same |
| Each bad upload from step 1 (corrupt ZIP, MR modality, 2D NIfTI, path escape, `mz3` as `file_type`, …) | 400 with the message; no job created |
| Over the voxel limit (limit lowered in the test) | 400 |
| Engine raises | Status `error` with a short message; `run.json` records the error |
| Add-on toggles | Reach the pipeline; a failing stand-in add-on still ends `done`, recorded in `run.json` |
| `labels.nii` | Same size, spacing, origin, direction as `ct.nii`; uint16 |
| `labels.json` | Exactly the ids present in `labels.nii`, with names and parents |
| `run.json` | Has every field listed in 7.4 |
| `GET /job-status` for another user's job | 404 |
| Two jobs queued | Run one after the other; both `done` |
| Contract | `API contract.md` and `openapi.json` regenerated and committed |

## Manual check

On the Mac, with the real engine: start the server, upload one real CT through `/docs` with both add-ons on, poll
until `done`, open `labels.nii` over `ct.nii` in a viewer (e.g. NiiVue) and check they line up.

## Out of scope

- Meshing and MZ3 (teammate); `/records` and `/files/{file_id}` (whoever owns storage).
- Real queue and persistent storage, out-of-memory handling beyond the voxel limit, moving the upload read off the
  event loop (documented gaps).

## Open questions (settle in the issue)

1. ~~Where job folders live~~ Settled 2026-11-27: `$HOLO_DATA_DIR/jobs/<job id>/` (one data root, step 0). Tell
   whoever builds `/records` and `/files`, since they read from there.
2. **When a job is `done`:** after `labels.nii` is written (meshing then runs as its own stage and job), or only
   after meshing has also finished (meshing hooks into `pipeline.py`).
3. **Who owns `/job-status`:** wired here (needed for the status tests), or by whoever owns the rest of the job
   endpoints.
4. **Voxel limit default:** from step 3's measurements (largest scan that fit in 8 GB, and in RAM on the Mac).
5. **400 vs 413** for an upload over the size or voxel limit (413 "Payload Too Large" is the HTTP code for size;
   the plan says 400 for all bad uploads).

## Done when

- Tests pass; manual check done.
- Contract regenerated; client team told.
- PR merged; step 8 can run through `pipeline.py`.
