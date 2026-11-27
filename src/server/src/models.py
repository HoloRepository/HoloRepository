from enum import StrEnum

from pydantic import BaseModel, Field


class Status(StrEnum):
    """Processing state of a job."""

    in_queue = "in_queue"
    in_progress = "in_progress"
    done = "done"
    error = "error"


class FileType(StrEnum):
    """Kind of file stored on the server."""

    dicom = "dicom"
    nifti = "nifti"
    nifti_segmented = "nifti_segmented"
    mz3 = "mz3"


class SegmentationModel(StrEnum):
    """Model used to segment the upload."""

    nv_segment_ct = "nv-segment-ct"
    ts_specialist_kidney = "ts-specialist-kidney"
    ts_specialist_liver = "ts-specialist-liver"
    ts_specialist_head_n_neck = "ts-specialist-head-n-neck"


class FileRecord(BaseModel):
    """A single file. Download it with `GET /files/{file_id}`."""

    id: str = Field(description="Use with `GET /files/{file_id}`.")
    file_type: FileType


class Record(BaseModel):
    """A record, linking all the files that belong to one upload."""

    id: str
    files: list[FileRecord]


class JobStatus(BaseModel):
    """Status of a processing job."""

    status: Status
    msg: str | None = None


class Job(BaseModel):
    """A newly started job."""

    id: str = Field(description="Use with `GET /job-status`.")


class User(BaseModel):
    """A signed-in user."""

    id: str
    username: str


class Token(BaseModel):
    """Access token returned by `POST /token`."""

    access_token: str = Field(
        description="Send as `Authorization: Bearer <token>` on every other request."
    )
    token_type: str = "bearer"
