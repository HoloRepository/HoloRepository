from typing import BinaryIO

import SimpleITK as sitk

from models import FileType


class UploadError(Exception): 
     """Rejected upload, the message is shown to the client"""

def read_upload(file: BinaryIO, file_type: FileType) -> sitk.Image:    # ZIP of DICOM or NIfTI → Int16 HU CT, original grid
    if file_type != FileType.nifti and file_type != FileType.dicom:
        raise UploadError(f"Upload a ZIP of a DICOM folder or a NIfTI file, not {file_type}")

    if file_type == FileType.dicom:
        ...

    if file_type == FileType.nifti:
        ...

    raise NotImplementedError

def write_ct(image, path) -> None:      
    """Saves ct.nii, uncompressed"""          
    sitk.WriteImage(image, str(path), useCompression=False)
