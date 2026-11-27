from typing import Annotated

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import FileResponse

from models import (
    FileType,
    Job,
    JobStatus,
    Record,
    SegmentationModel,
)

DESCRIPTION = """
The API accepts one ZIP archive of DICOM files or one NIfTI file, segments it with a
selected model, and returns downloadable results, including an `.mz3` mesh.

## Client workflow

1. Upload a file and pick a model with `POST /new-job`.
2. Poll `GET /job-status?job_id=...` until `status` is `done`.
3. Find the record's files with `GET /records`, then download each with
   `GET /files/{file_id}`.

Durations are in seconds. Display `.mz3` meshes with
[NiiVue](https://niivue.com/docs/guide/meshes/).
"""

app = FastAPI(title="HoloRepository API", version="0.1.0", description=DESCRIPTION)


# --- Jobs ---


@app.post("/new-job", tags=["jobs"])
async def create_job(
    file: Annotated[
        UploadFile,
        File(description="One ZIP archive of a DICOM folder, or one NIfTI file."),
    ],
    file_type: Annotated[FileType, Form()],
    model: Annotated[SegmentationModel, Form()] = SegmentationModel.nv_segment_ct,
) -> Job:
    """Upload a file and start processing it.

    Send `multipart/form-data`. For DICOM, ZIP the folder and send the ZIP as `file`;
    the server extracts it.
    """
    ...


@app.get("/job-status", tags=["jobs"])
async def get_job_status(job_id: str) -> JobStatus:
    """Get a processing job's status."""
    ...


# --- Records & files ---


@app.get("/records", tags=["records"])
async def list_records() -> list[Record]:
    """List records. Each record links all the files that belong to one upload."""
    ...


@app.get(
    "/files/{file_id}",
    tags=["records"],
    response_class=FileResponse,
    responses={200: {"content": {"application/octet-stream": {}}}},
)
async def download_file(file_id: str) -> FileResponse:
    """Download a file's raw bytes."""
    ...
