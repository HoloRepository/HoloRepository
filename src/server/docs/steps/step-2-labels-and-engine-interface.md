# Step 2: shared label list, engine interface, lookup tables

Part of the [segmentation plan](../segmentation-plan.md) (sections 4, 8; section 13, step 2).

| | |
|---|---|
| Depends on | step 0 (`metadata.json` downloaded) |
| Branch / PR base | from step 0's branch (or `main`); runs in parallel with step 1 |
| Unblocks | steps 3, 5, 6, 7 all build on it. **Get it reviewed early**: changes here ripple into every later PR |
| Shipped code | ~100 lines (`labels.py`, `engines/__init__.py`) + generated `labels.json` (data) |
| Runs on your machines | no; unit tests only |

## Goal

Fix the three contracts the rest of the engine plugs into, so the design stays model-agnostic:

1. **One shared label list** (ids and names that never change between models).
2. **One engine interface** that any base model implements.
3. **Two lookup tables**: base model → engine, add-on name → add-on runner.

## Decisions that apply (from the plan)

- Interface: `segment(ct, timer) -> (label_map on the CT grid, {id: name})` (timer added 2026-11-27).
- Lookup table: `SegmentationModel` value → engine. Second table: add-on name → add-on runner.
- Add-ons and post-processing ask for organs by **shared name**, so any future base model works with them.
- One shared label list, `labels.json` in the repo:
  - NV-Segment-CT's own ids kept as they are (1–132), names from its `metadata.json` (e.g. `liver`,
    `right kidney`, `left kidney`, `esophagus`, `inferior vena cava`, `thyroid gland`).
  - New structures get ids above 132. None are needed yet: the add-ons reuse NV-Segment-CT's ids (kidney cysts
    116 / 117, liver vessels 25).
  - Each entry: id, name, parent.
- Handover to meshing (section 8): each job writes a `labels.json` with `{"<id>": {"name": "...", "parent": <id or
  null>}}` for every id present. Colours, display names and grouping belong to meshing.
- `model` names the base model only: `nv-segment-ct`. The three `ts-specialist-*` values are dropped (head and
  neck peaked at 142 GB RAM; kidney and liver become add-on toggles in step 7).
- Engines are loaded **once at startup**, single server process, warmed up with one dummy window (step 3 fills
  this in for NV-Segment-CT).

## Work, in stages

### 2.1 `labels.json` (generated, committed)

- A small generator script (in `tools/`, not shipped) reads the pinned `metadata.json` and writes
  `src/segmentation/labels.json`: ids 1–132 with NV-Segment-CT's names, `parent` set for 116 → 14, 117 → 5 and
  25 → 1, otherwise `null` (open question 1).
- Background (0) is implicit and not listed.
- The file is committed so the server never needs `metadata.json` to know names, and so reviews see every change.

### 2.2 `labels.py`

- Load `labels.json` once.
- `id_of(name)` and `name_of(id)`; unknown names or ids raise a clear error (a typo in an organ list must fail at
  startup, not silently skip an organ).
- `table_for(ids)`: the per-job `{"<id>": {"name", "parent"}}` dictionary for the ids present in a label map, as
  meshing expects.

### 2.3 Engine interface (`src/segmentation/engines/__init__.py`)

- A minimal protocol every base model implements:
  - `load(device)`: load weights, warm up. Called once at startup.
  - `segment(ct, timer) -> (label_map, {id: name})`: `ct` is the standard CT from step 1 (SimpleITK, Int16 HU);
    `label_map` is a uint16 SimpleITK image on exactly the CT's grid; the dictionary lists the ids the engine can
    output, using shared ids and names.
  - A short description for `run.json` (engine name, model revision, settings), used in step 7.
- `timer`: a small stage timer (`with timer("preprocessing"): ...`) the engine records its stages on; the
  pipeline uses the same one for its own stages and writes them to the log and `run.json` (open question 3).

### 2.4 Lookup tables

- `ENGINES`: `SegmentationModel` → engine class. One entry now: `nv-segment-ct` → the NV-Segment-CT engine
  (a stub until step 3, so this step's tests run without weights).
- `ADDONS`: add-on name → runner. Two entries, `kidney` and `liver`, as stubs until step 6.
- A new base model later = one engine file + one table entry; nothing else changes.

### 2.5 `SegmentationModel` enum

- Unchanged in this step: removing `ts-specialist-kidney`, `ts-specialist-liver`, `ts-specialist-head-n-neck` moves
  to step 7, together with the add-on form fields (open question 4).

## Tests (`tests/segmentation/test_labels.py`, `test_engines.py`, fast)

| Check | Expected |
|---|---|
| `labels.json` | No duplicate ids, no duplicate names, ids are positive integers, every `parent` points to an existing id |
| Matches `metadata.json` | Every id 1–132 has NV-Segment-CT's exact name (test skipped if the weights folder is missing) |
| Names used elsewhere | Every organ name the add-ons and post-processing will use resolves (`liver`, `spleen`, `colon`, `left kidney`, `right kidney`, `left kidney cyst`, `right kidney cyst`, `hepatic vessel`, and step 5's list); the lists themselves live in steps 5 and 6, so this test grows with them |
| `table_for` | Returns only the ids given, with names and parents |
| Lookup | Every `SegmentationModel` value has an engine; every add-on name has a runner |
| Unknown name / id | Raises |

## Out of scope

- The NV-Segment-CT engine itself (step 3), the add-on runners (step 6), the post-processing organ list (step 5).
- The new `kidney_addon` / `liver_addon` form fields (step 7).
- Colours, display names, grouping (meshing).

## Open questions (settle in the issue)

1. **Which parents to set:** settled 2026-11-27: kidney cysts → their kidney (116 → 14, 117 → 5) and hepatic
   vessel → liver (25 → 1), the two add-on structures that sit inside an organ we output. Everything else `null`;
   a parent must itself be output (lung 20 and bone 21 never are). `parent` changes no voxels: it lets meshing fill
   or group the parent around the child (check with the meshing teammate that they read it).
2. **Name style:** settled 2026-11-27: NV-Segment-CT's names exactly as in `metadata.json` (`inferior vena cava`),
   no separate key.
3. **Stage timings:** settled 2026-11-27: a small timer object passed into `segment`; the engine records its
   stages on it (`with timer("inference"): ...`) and the pipeline records its own stages on the same timer. No API
   change: timings go to the log and `run.json` only.
4. **When the enum change lands:** settled 2026-11-27: together with the add-on form fields in step 7, so the
   client team gets one API change. Until then the three `ts-specialist-*` values stay in `SegmentationModel`
   with no engine, and the lookup test skips them by name; step 7 removes them and that exception.

## Done when

- Tests pass; `labels.json` reviewed.
- Interface and lookup tables reviewed by whoever builds on them before steps 3, 5 and 6 stack on top.
- PR merged.
