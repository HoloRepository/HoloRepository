from typing import Annotated

from fastapi import Depends, FastAPI, File, Form, UploadFile, status
from fastapi.responses import FileResponse
from fastapi.security import OAuth2PasswordRequestForm

from auth import CurrentUser
from models import (
    FileType,
    Job,
    JobStatus,
    Record,
    SegmentationModel,
    Token,
    User,
)

DESCRIPTION = """
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
"""

app = FastAPI(title="HoloRepository API", version="0.1.0", description=DESCRIPTION)


# --- Auth ---


@app.post("/signup", tags=["auth"], status_code=status.HTTP_201_CREATED)
async def sign_up(
    username: Annotated[
        str, Form(min_length=3, max_length=64, pattern=r"^[A-Za-z0-9_.-]+$")
    ],
    password: Annotated[str, Form(min_length=8, max_length=128)],
) -> User:
    """Create an account."""
    ...


@app.post("/token", tags=["auth"])
async def sign_in(form: Annotated[OAuth2PasswordRequestForm, Depends()]) -> Token:
    """Sign in with `username` and `password` form fields and get an access token."""
    ...


@app.get("/me", tags=["auth"])
async def read_me(user: CurrentUser) -> User:
    """Return the signed-in user."""
    return user


# --- Jobs ---


@app.post("/new-job", tags=["jobs"])
async def create_job(
    user: CurrentUser,
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
async def get_job_status(user: CurrentUser, job_id: str) -> JobStatus:
    """Get a processing job's status."""
    ...


# --- Records & files ---


@app.get("/records", tags=["records"])
async def list_records(user: CurrentUser) -> list[Record]:
    """List records. Each record links all the files that belong to one upload."""
    ...


@app.get(
    "/files/{file_id}",
    tags=["records"],
    response_class=FileResponse,
    responses={200: {"content": {"application/octet-stream": {}}}},
)
async def download_file(user: CurrentUser, file_id: str) -> FileResponse:
    """Download a file's raw bytes."""
    ...
