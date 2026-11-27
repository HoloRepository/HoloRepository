import logging
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

import numpy as np
import SimpleITK as sitk

from models import FileType

log = logging.getLogger(__name__)

DRIVE = re.compile(r"^[A-Za-z]:")
# Errors zipfile raises for a damaged, encrypted or unsupported archive.
ZIP_ERRORS = (zipfile.BadZipFile, zlib.error, EOFError, RuntimeError, NotImplementedError)

MODALITY, POSITION, ORIENTATION, FRAMES = "0008|0060", "0020|0032", "0020|0037", "0028|0008"
SPACING_TOLERANCE = 0.01  # fraction of the slice spacing
GZIP_MAGIC = b"\x1f\x8b"
INT16_MIN, INT16_MAX = -32768, 32767


class UploadError(Exception):
    """Rejected upload, the message is shown to the client"""


def read_upload(
    file: BinaryIO, file_type: FileType
) -> sitk.Image:  # ZIP of DICOM or NIfTI → Int16 HU CT, original grid
    if file_type != FileType.nifti and file_type != FileType.dicom:
        raise UploadError(f"Upload a ZIP of a DICOM folder or a NIfTI file, not '{file_type}'")

    if file_type == FileType.dicom:
        with extracted_zip(file) as folder:
            image = read_dicom(folder)
    else:
        image = read_nifti(file)
    return standardise(image)


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


def read_dicom(folder: Path) -> sitk.Image:
    """The CT series with the most files in the folder tree, read in HU; UploadError if it can't be used."""
    try:
        files = choose_series(find_series(folder))
        first = header(files[0])
        modality = first.GetMetaData(MODALITY).strip() if first.HasMetaDataKey(MODALITY) else "unknown"
        if modality != "CT":
            raise UploadError(f"The DICOM series is '{modality}', not CT")
        if len(files) < 2:
            if first.HasMetaDataKey(FRAMES) and int(first.GetMetaData(FRAMES).strip() or 1) > 1:
                # Enhanced (multi-frame) DICOM: not supported yet (plan section 15, decided 2026-11-27).
                raise UploadError("Single-file DICOM scans aren't supported; export the scan as one file per slice")
            raise UploadError("The DICOM series has only one slice; a 3D CT scan is needed")
        reader = sitk.ImageSeriesReader()
        reader.SetFileNames(check_slices(files))
        return reader.Execute()  # rescale slope and intercept applied, so values are HU
    except RuntimeError as error:  # SimpleITK's error for unreadable files
        raise UploadError("The DICOM files can't be read") from error


def find_series(folder: Path) -> dict[str, list[str]]:
    """Files per series UID, from every folder (GDCM only looks in one folder at a time)."""
    series: dict[str, list[str]] = {}
    folders = {path.parent for path in folder.rglob("*") if path.is_file()}  # skip folders with no files
    for directory in sorted(folders):
        for uid in sitk.ImageSeriesReader.GetGDCMSeriesIDs(str(directory)):
            series.setdefault(uid, []).extend(sitk.ImageSeriesReader.GetGDCMSeriesFileNames(str(directory), uid))
    if not series:
        raise UploadError("The ZIP contains no DICOM series")
    return series


def choose_series(series: dict[str, list[str]]) -> list[str]:
    """The series with the most files; ties go to the lowest SeriesInstanceUID (decided 2026-11-27)."""
    ranked = sorted(series.items(), key=lambda item: (-len(item[1]), uid_order(item[0])))
    uid, files = ranked[0]
    tied = [other for other, other_files in ranked[1:] if len(other_files) == len(files)]
    log.info("DICOM: %d series, chose %s (%d files)", len(series), uid, len(files))
    if tied:
        log.info("DICOM: tie on file count with %s; chose the lowest SeriesInstanceUID", ", ".join(tied))
    return files


def uid_order(uid: str) -> tuple[int, ...]:
    """UIDs are dotted numbers; compare them as numbers, so 1.2.8 comes before 1.2.10."""
    return tuple(int(part) if part.isdigit() else -1 for part in uid.split("."))


def check_slices(files: list[str]) -> list[str]:
    """Files sorted along the slice normal; UploadError for uneven spacing or a gantry tilt."""
    headers = [header(name) for name in files]
    if not all(h.HasMetaDataKey(POSITION) and h.HasMetaDataKey(ORIENTATION) for h in headers):
        raise UploadError("The DICOM files have no slice positions")
    orientation = np.array(headers[0].GetMetaData(ORIENTATION).split("\\"), dtype=float)
    normal = np.cross(orientation[:3], orientation[3:])
    positions = np.array([h.GetMetaData(POSITION).split("\\") for h in headers], dtype=float)
    order = np.argsort(positions @ normal)
    steps = np.diff(positions[order], axis=0)
    along = steps @ normal  # distance between neighbouring slices
    spacing = float(np.median(along))
    if spacing <= 0:
        raise UploadError("The DICOM series has slices at the same position")
    if np.any(np.abs(along - spacing) > SPACING_TOLERANCE * spacing):
        raise UploadError("The DICOM slices are unevenly spaced (a slice may be missing)")
    sideways = np.linalg.norm(steps - np.outer(along, normal), axis=1)
    if np.any(sideways > SPACING_TOLERANCE * spacing):
        raise UploadError("The DICOM series has a gantry tilt, which is not supported")
    return [files[i] for i in order]


def read_nifti(file: BinaryIO) -> sitk.Image:
    """One NIfTI file (.nii or .nii.gz, told apart by content) as a 3D image; 4D keeps the first time point."""
    file.seek(0)
    suffix = ".nii.gz" if file.read(2) == GZIP_MAGIC else ".nii"
    file.seek(0)
    with tempfile.TemporaryDirectory(prefix="holo-upload-") as temp:
        path = Path(temp) / f"upload{suffix}"
        with path.open("wb") as out:
            shutil.copyfileobj(file, out)  # SimpleITK reads from a path; streamed, not held in memory
        try:
            image = sitk.ReadImage(str(path), imageIO="NiftiImageIO")  # fails unless the content is NIfTI
        except RuntimeError as error:
            raise UploadError("The file is not a NIfTI file or is damaged") from error
    if image.GetNumberOfComponentsPerPixel() != 1:
        raise UploadError("The NIfTI file is not a grey-level scan")
    if image.GetDimension() == 4:
        size = image.GetSize()
        if size[3] > 1:
            log.info("NIfTI: 4D with %d time points, using the first", size[3])
        extract = sitk.ExtractImageFilter()
        extract.SetSize([*size[:3], 0])
        extract.SetIndex([0, 0, 0, 0])
        extract.SetDirectionCollapseToStrategy(extract.DIRECTIONCOLLAPSETOSUBMATRIX)  # keep the 3D orientation
        image = extract.Execute(image)
    if image.GetDimension() != 3 or min(image.GetSize()) < 2:
        raise UploadError("The NIfTI file is not a 3D scan")
    return image


def standardise(image: sitk.Image) -> sitk.Image:
    """Int16 HU, rounded and clipped to the Int16 range; size, spacing, origin and direction unchanged."""
    if image.GetPixelID() == sitk.sitkInt16:
        return image
    if image.GetPixelID() in (sitk.sitkFloat32, sitk.sitkFloat64):
        image = sitk.Round(image)  # float NIfTI: changes values by under 0.5 HU
    stats = sitk.MinimumMaximumImageFilter()
    stats.Execute(image)
    if stats.GetMinimum() < INT16_MIN or stats.GetMaximum() > INT16_MAX:
        log.warning("CT values %g to %g clipped to the Int16 range", stats.GetMinimum(), stats.GetMaximum())
    return sitk.Clamp(image, sitk.sitkInt16, INT16_MIN, INT16_MAX)


def header(path: str) -> sitk.ImageFileReader:
    reader = sitk.ImageFileReader()
    reader.SetFileName(path)
    reader.ReadImageInformation()
    return reader


def write_ct(image: sitk.Image, path: Path) -> None:
    """Saves ct.nii, uncompressed"""
    sitk.WriteImage(image, str(path), useCompression=False)
