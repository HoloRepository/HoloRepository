"""NV-Segment-CT engine (nvidia/NV-Segment-CT in MONAI's vista3d132). A stub until step 3."""

import SimpleITK as sitk
import torch

from segmentation.engines.base import Engine
from segmentation.engines.nv_segment_ct.weights import REVISION
from segmentation.timing import StageTimer


class NVSegmentCT(Engine):
    def load(self, device: torch.device) -> None:
        raise NotImplementedError("NV-Segment-CT engine: step 3")

    def segment(self, ct: sitk.Image, timer: StageTimer) -> tuple[sitk.Image, dict[int, str]]:
        raise NotImplementedError("NV-Segment-CT engine: step 3")

    def describe(self) -> dict:
        return {"engine": "nv-segment-ct", "revision": REVISION}
