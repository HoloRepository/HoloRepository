import io
import stat
import tempfile
import zipfile

import numpy as np
import pytest
import SimpleITK as sitk

from models import FileType
from segmentation.ct_input import UploadError, read_upload, write_ct

ANGLE = np.radians(30)
OBLIQUE = (1, 0, 0, 0, np.cos(ANGLE), np.sin(ANGLE), 0, -np.sin(ANGLE), np.cos(ANGLE))


# ---------------------------------------------------------------- helpers


def write_series(folder, n=6, uid="1.2.3", modality="CT", gaps=None, tilt=0.0, hu=-500):
    """Write a synthetic axial CT series, one file per slice; slice i holds HU value hu + i."""
    folder.mkdir(parents=True, exist_ok=True)
    z = np.cumsum([0.0, *(gaps or [2.5] * (n - 1))])
    writer = sitk.ImageFileWriter()
    writer.KeepOriginalImageUIDOn()
    for i, depth in enumerate(z):
        image = sitk.GetImageFromArray(np.full((4, 5), hu + i, np.int16))
        image.SetSpacing((0.7, 0.8))
        tags = {
            "0008|0060": modality,
            "0020|000e": uid,
            "0008|0018": f"{uid}.{i}",
            "0020|0013": str(i),
            "0028|1052": "-1024",
            "0028|1053": "1",
            "0020|0037": "1\\0\\0\\0\\1\\0",
            "0020|0032": f"{-10 + tilt * depth}\\-20\\{depth}",
        }
        for key, value in tags.items():
            image.SetMetaData(key, value)
        writer.SetFileName(str(folder / f"{uid}-{i}.dcm"))
        writer.Execute(image)


def zip_upload(folder=None, entries=()):
    """ZIP a folder's files (paths relative to it) plus raw (name, bytes, mode) entries, in memory."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        for path in sorted(folder.rglob("*")) if folder else []:
            if path.is_file():
                archive.write(path, path.relative_to(folder).as_posix())
        for name, data, mode in entries:
            info = zipfile.ZipInfo(name)
            info.external_attr = mode << 16
            archive.writestr(info, data)
    return buffer


def nifti_upload(tmp_path, array, gz=False, dims=3):
    image = sitk.GetImageFromArray(array)
    if dims == 3:
        image.SetDirection(OBLIQUE)
        image.SetSpacing((0.7, 0.8, 2.5))
        image.SetOrigin((1, 2, 3))
    path = tmp_path / ("in.nii.gz" if gz else "in.nii")
    sitk.WriteImage(image, str(path))
    return image, io.BytesIO(path.read_bytes())


def assert_same_geometry(a, b):
    assert a.GetSize() == b.GetSize()
    for get in (sitk.Image.GetSpacing, sitk.Image.GetOrigin, sitk.Image.GetDirection):
        assert np.allclose(get(a), get(b))


def hu(image):
    """HU value of each slice (every voxel in a synthetic slice is the same)."""
    return sitk.GetArrayFromImage(image)[:, 0, 0].tolist()


@pytest.fixture
def temp_root(monkeypatch, tmp_path):
    """Send the module's temporary folders to a folder the test can inspect."""
    root = tmp_path / "temp"
    root.mkdir()
    monkeypatch.setattr(tempfile, "tempdir", str(root))
    return root


# ---------------------------------------------------------------- DICOM


def test_single_series(tmp_path):
    write_series(tmp_path / "scan")
    ct = read_upload(zip_upload(tmp_path / "scan"), FileType.dicom)
    assert ct.GetPixelID() == sitk.sitkInt16
    assert hu(ct) == [-500, -499, -498, -497, -496, -495]
    assert ct.GetSize() == (5, 4, 6)
    assert np.allclose(ct.GetSpacing(), (0.7, 0.8, 2.5))
    assert np.allclose(ct.GetOrigin(), (-10, -20, 0))
    assert np.allclose(ct.GetDirection(), np.eye(3).ravel())


def test_largest_series_chosen_over_localiser(tmp_path):
    write_series(tmp_path / "scan/localiser", n=3, uid="1.2.1")
    write_series(tmp_path / "scan/ct", n=40, uid="1.2.2")
    assert read_upload(zip_upload(tmp_path / "scan"), FileType.dicom).GetSize()[2] == 40


def test_series_in_nested_folders(tmp_path):
    write_series(tmp_path / "scan/a/b/c")
    assert read_upload(zip_upload(tmp_path / "scan"), FileType.dicom).GetSize()[2] == 6


def test_tie_goes_to_lowest_uid(tmp_path):
    write_series(tmp_path / "scan/x", uid="1.2.10", hu=100)
    write_series(tmp_path / "scan/y", uid="1.2.8", hu=200)  # 8 < 10 as numbers, though not as text
    assert hu(read_upload(zip_upload(tmp_path / "scan"), FileType.dicom))[0] == 200


def test_spacing_rounding_is_accepted(tmp_path):
    write_series(tmp_path / "scan", n=5, gaps=[2.5, 2.51, 2.49, 2.5])  # 0.4% off
    assert read_upload(zip_upload(tmp_path / "scan"), FileType.dicom).GetSize()[2] == 5


@pytest.mark.parametrize(
    ("series", "message"),
    [
        ({"modality": "MR"}, "'MR', not CT"),
        ({"n": 5, "gaps": [2.5, 2.5, 5.0, 2.5]}, "unevenly spaced"),  # missing slice
        ({"n": 5, "gaps": [2.5, 2.55, 2.5, 2.5]}, "unevenly spaced"),  # 2% off
        ({"tilt": 0.035}, "gantry tilt"),  # about 2 degrees
        ({"n": 1}, "only one slice"),
    ],
)
def test_unusable_series_rejected(tmp_path, series, message):
    write_series(tmp_path / "scan", **series)
    with pytest.raises(UploadError, match=message):
        read_upload(zip_upload(tmp_path / "scan"), FileType.dicom)


def test_multi_frame_file_rejected(tmp_path):
    image = sitk.GetImageFromArray(np.zeros((3, 4, 5), np.int16))
    image.SetMetaData("0008|0060", "CT")
    (tmp_path / "scan").mkdir()
    sitk.WriteImage(image, str(tmp_path / "scan/multi.dcm"))
    with pytest.raises(UploadError, match="one file per slice"):
        read_upload(zip_upload(tmp_path / "scan"), FileType.dicom)


def test_zip_without_dicom_rejected():
    with pytest.raises(UploadError, match="no DICOM series"):
        read_upload(zip_upload(entries=[("readme.txt", b"hi", 0o644)]), FileType.dicom)


# ---------------------------------------------------------------- ZIP safety


@pytest.mark.parametrize("upload", [io.BytesIO(b"\x00" * 200), io.BytesIO(b"not a zip")])
def test_non_zip_rejected(upload):
    with pytest.raises(UploadError, match="not a ZIP"):
        read_upload(upload, FileType.dicom)


def test_corrupt_zip_rejected():
    data = bytearray(zip_upload(entries=[("1.dcm", b"x" * 5000, 0o644)]).getvalue())
    data[40:60] = b"\xff" * 20  # damage the compressed data, keep the index
    with pytest.raises(UploadError, match="damaged"):
        read_upload(io.BytesIO(bytes(data)), FileType.dicom)


@pytest.mark.parametrize(
    ("name", "mode", "message"),
    [
        ("../evil", 0o644, "outside its folder"),
        ("a/../../evil", 0o644, "outside its folder"),
        ("..\\evil", 0o644, "outside its folder"),
        ("/evil", 0o644, "absolute path"),
        ("C:/evil", 0o644, "absolute path"),
        ("link", stat.S_IFLNK | 0o777, "symbolic link"),
    ],
)
def test_unsafe_entries_rejected(temp_root, name, mode, message):
    upload = zip_upload(entries=[("ok.dcm", b"x", 0o644), (name, b"x", mode)])
    with pytest.raises(UploadError, match=message):
        read_upload(upload, FileType.dicom)
    assert list(temp_root.parent.rglob("evil")) == []
    assert list(temp_root.iterdir()) == []  # nothing extracted, temp folder gone


def test_temp_folder_removed_after_success(temp_root, tmp_path):
    write_series(tmp_path / "scan")
    read_upload(zip_upload(tmp_path / "scan"), FileType.dicom)
    assert list(temp_root.iterdir()) == []


def test_temp_folder_removed_after_error(temp_root, tmp_path):
    write_series(tmp_path / "scan", modality="MR")
    with pytest.raises(UploadError):
        read_upload(zip_upload(tmp_path / "scan"), FileType.dicom)
    assert list(temp_root.iterdir()) == []


# ---------------------------------------------------------------- NIfTI


@pytest.mark.parametrize("gz", [False, True])
def test_nifti_3d_keeps_geometry(tmp_path, gz):
    source, upload = nifti_upload(tmp_path, np.arange(120, dtype=np.int16).reshape(4, 5, 6), gz=gz)
    ct = read_upload(upload, FileType.nifti)
    assert_same_geometry(ct, source)
    assert np.array_equal(sitk.GetArrayFromImage(ct), sitk.GetArrayFromImage(source))


def test_nifti_4d_uses_first_time_point(tmp_path):
    volume = np.arange(120, dtype=np.int16).reshape(4, 5, 6)
    frames = []
    for t in range(3):
        frame = sitk.GetImageFromArray(volume + t)
        frame.SetDirection(OBLIQUE)
        frames.append(frame)
    path = tmp_path / "in.nii.gz"
    sitk.WriteImage(sitk.JoinSeries(frames), str(path))
    ct = read_upload(io.BytesIO(path.read_bytes()), FileType.nifti)
    assert np.array_equal(sitk.GetArrayFromImage(ct), volume)
    assert np.allclose(ct.GetDirection(), OBLIQUE)


@pytest.mark.parametrize("array", [np.zeros((5, 6), np.int16), np.zeros((1, 5, 6), np.int16)])
def test_nifti_not_3d_rejected(tmp_path, array):
    _, upload = nifti_upload(tmp_path, array, dims=array.ndim)
    with pytest.raises(UploadError, match="not a 3D scan"):
        read_upload(upload, FileType.nifti)


def test_non_nifti_rejected():
    with pytest.raises(UploadError, match="not a NIfTI file"):
        read_upload(io.BytesIO(b"hello" * 100), FileType.nifti)


def test_float_nifti_rounded_to_int16(tmp_path):
    _, upload = nifti_upload(tmp_path, np.tile(np.float32([-1000.4, -0.6, 40.49]), (2, 2, 1)))
    ct = read_upload(upload, FileType.nifti)
    assert ct.GetPixelID() == sitk.sitkInt16
    assert sitk.GetArrayFromImage(ct)[0, 0].tolist() == [-1000, -1, 40]


def test_out_of_range_values_clipped(tmp_path):
    _, upload = nifti_upload(tmp_path, np.tile(np.int32([-40000, 5, 70000]), (2, 2, 1)))
    assert sitk.GetArrayFromImage(read_upload(upload, FileType.nifti))[0, 0].tolist() == [-32768, 5, 32767]


# ---------------------------------------------------------------- other


@pytest.mark.parametrize("file_type", [FileType.nifti_segmented, FileType.mz3])
def test_output_types_rejected(file_type):
    with pytest.raises(UploadError):
        read_upload(io.BytesIO(b""), file_type)


def test_write_ct_round_trip(tmp_path):
    _, upload = nifti_upload(tmp_path, np.arange(120, dtype=np.int16).reshape(4, 5, 6))
    ct = read_upload(upload, FileType.nifti)
    write_ct(ct, tmp_path / "ct.nii")
    back = sitk.ReadImage(str(tmp_path / "ct.nii"))
    assert_same_geometry(back, ct)
    assert np.array_equal(sitk.GetArrayFromImage(back), sitk.GetArrayFromImage(ct))
    assert (tmp_path / "ct.nii").stat().st_size == 352 + 120 * 2  # header + Int16 voxels: uncompressed
