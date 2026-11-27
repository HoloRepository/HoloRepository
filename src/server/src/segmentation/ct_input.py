import re
import shutil
import stat
import tempfile
import zipfile
import zlib
from collections.abc import Generator
from contextlib import contextmanager
from pathlib import Path, PurePosixPath
from typing import BinaryIO

import SimpleITK as sitk

from models import FileType

DRIVE = re.compile(r"^[A-Za-z]:")
# Errors zipfile raises for a damaged, encrypted or unsupported archive.
ZIP_ERRORS = (zipfile.BadZipFile, zlib.error, EOFError, RuntimeError, NotImplementedError)


class UploadError(Exception):
    """Rejected upload, the message is shown to the client"""


def read_upload(
    file: BinaryIO, file_type: FileType
) -> sitk.Image:  # ZIP of DICOM or NIfTI → Int16 HU CT, original grid
    if file_type != FileType.nifti and file_type != FileType.dicom:
        raise UploadError(f"Upload a ZIP of a DICOM folder or a NIfTI file, not '{file_type}'")

    if file_type == FileType.dicom:
        with extracted_zip(file) as folder:  # noqa: F841 (step 1.3 reads the series from it)
            ...

    if file_type == FileType.nifti:
        ...

    raise NotImplementedError


@contextmanager
def extracted_zip(file: BinaryIO) -> Generator[Path]:
    """Extract a ZIP upload into a temporary folder that is deleted afterwards, also on error.

    The whole upload is rejected if any entry is a symlink or would land outside the folder.
    """
    file.seek(0)
    if not zipfile.is_zipfile(file):  
        raise UploadError("The DICOM upload is not a ZIP file")
    file.seek(0)
    with tempfile.TemporaryDirectory(prefix="holo-upload-") as temp:
        root = Path(temp).resolve()
        try:
            with zipfile.ZipFile(file) as archive:
                entries = [(info, safe_target(root, info)) for info in archive.infolist()]
                for info, target in entries:
                    if info.is_dir():
                        target.mkdir(parents=True, exist_ok=True)
                        continue
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(info) as source, target.open("wb") as out:
                        shutil.copyfileobj(source, out)  # streamed, never the whole file in memory
        except ZIP_ERRORS as error:
            raise UploadError("The ZIP file is damaged or can't be read") from error
        yield root


def safe_target(root: Path, info: zipfile.ZipInfo) -> Path:
    """Where an entry goes inside root; UploadError if it is a symlink or escapes root."""
    name = info.filename.replace("\\", "/")
    if stat.S_ISLNK(info.external_attr >> 16):
        raise UploadError(f"The ZIP contains a symbolic link ('{info.filename}'), which is not allowed")
    if PurePosixPath(name).is_absolute() or DRIVE.match(name):
        raise UploadError(f"The ZIP contains an absolute path ('{info.filename}'), which is not allowed")
    target = (root / name).resolve()
    if not target.is_relative_to(root):
        raise UploadError(f"The ZIP contains a path outside its folder ('{info.filename}'), which is not allowed")
    return target


def write_ct(image: sitk.Image, path: Path) -> None:
    """Saves ct.nii, uncompressed"""
    sitk.WriteImage(image, str(path), useCompression=False)
