# HoloRepository API contract (v0.1.0)

> Generated from the server code by `scripts/generate_contract.py`. Don't edit this file by hand; change the code and regenerate.

The API accepts one ZIP archive of DICOM files or one NIfTI file, segments it with a
selected model, and returns downloadable results, including an `.mz3` mesh.

## Client workflow

1. Create an account with `POST /signup`, then sign in with `POST /token`.
2. Upload a file and pick a model with `POST /new-job`.
3. Poll `GET /job-status?job_id=...` until `status` is `done`.
4. Find the record's files with `GET /records`, then download each with
   `GET /files/{file_id}`.

Every endpoint except `/signup` and `/token` needs `Authorization: Bearer <token>`.

Durations are in seconds. Display `.mz3` meshes with
[NiiVue](https://niivue.com/docs/guide/meshes/).

## Auth

### `POST /signup`

Create an account.

**Auth:** none

**Request body** (`application/x-www-form-urlencoded`)

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `username` | `string` | yes | min length `3`, max length `64`, pattern `^[A-Za-z0-9_.-]+$` |
| `password` | `string` | yes | min length `8`, max length `128` |

**Response `201`:** [`User`](#user)

### `POST /token`

Sign in with `username` and `password` form fields and get an access token.

**Auth:** none

**Request body** (`application/x-www-form-urlencoded`)

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `grant_type` | `string` \| `null` | no |  |
| `username` | `string` | yes |  |
| `password` | `string` | yes |  |
| `scope` | `string` | no |  |
| `client_id` | `string` \| `null` | no |  |
| `client_secret` | `string` \| `null` | no |  |

**Response `200`:** [`Token`](#token)

### `GET /me`

Return the signed-in user.

**Auth:** bearer token

**Response `200`:** [`User`](#user)

## Jobs

### `POST /new-job`

Upload a file and start processing it.

Send `multipart/form-data`. For DICOM, ZIP the folder and send the ZIP as `file`;
the server extracts it.

**Auth:** bearer token

**Request body** (`multipart/form-data`)

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `file` | file | yes | One ZIP archive of a DICOM folder, or one NIfTI file. |
| `file_type` | [`FileType`](#filetype) | yes |  |
| `model` | [`SegmentationModel`](#segmentationmodel) | no | default `nv-segment-ct` |

**Response `200`:** [`Job`](#job)

### `GET /job-status`

Get a processing job's status.

**Auth:** bearer token

**Parameters**

| Name | In | Type | Required | Description |
| -- | -- | -- | -- | -- |
| `job_id` | query | `string` | yes |  |

**Response `200`:** [`JobStatus`](#jobstatus)

## Records

### `GET /records`

List records. Each record links all the files that belong to one upload.

**Auth:** bearer token

**Response `200`:** list of [`Record`](#record)

### `GET /files/{file_id}`

Download a file's raw bytes.

**Auth:** bearer token

**Parameters**

| Name | In | Type | Required | Description |
| -- | -- | -- | -- | -- |
| `file_id` | path | `string` | yes |  |

**Response `200`:** raw bytes (`application/octet-stream`)

## Models

### `FileRecord`

A single file. Download it with `GET /files/{file_id}`.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `id` | `string` | yes | Use with `GET /files/{file_id}`. |
| `file_type` | [`FileType`](#filetype) | yes |  |

### `FileType`

Kind of file stored on the server.

Values: `dicom`, `nifti`, `nifti_segmented`, `mz3`

### `Job`

A newly started job.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `id` | `string` | yes | Use with `GET /job-status`. |

### `JobStatus`

Status of a processing job.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `status` | [`Status`](#status) | yes |  |
| `msg` | `string` \| `null` | no |  |

### `Record`

A record, linking all the files that belong to one upload.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `id` | `string` | yes |  |
| `files` | list of [`FileRecord`](#filerecord) | yes |  |

### `SegmentationModel`

Model used to segment the upload.

Values: `nv-segment-ct`, `ts-specialist-kidney`, `ts-specialist-liver`, `ts-specialist-head-n-neck`

### `Status`

Processing state of a job.

Values: `in_queue`, `in_progress`, `done`, `error`

### `Token`

Access token returned by `POST /token`.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `access_token` | `string` | yes | Send as `Authorization: Bearer <token>` on every other request. |
| `token_type` | `string` | no | default `bearer` |

### `User`

A signed-in user.

| Field | Type | Required | Description |
| -- | -- | -- | -- |
| `id` | `string` | yes |  |
| `username` | `string` | yes |  |
