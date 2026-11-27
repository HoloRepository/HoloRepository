"""Base segmentation models: the Engine interface and the lookup table from the API's `model` value to an engine.

A new base model = one package here (engine + its own model files) and one ENGINES entry.
"""

from models import SegmentationModel
from segmentation.engines.base import Engine
from segmentation.engines.nv_segment_ct import NVSegmentCT

__all__ = ["ENGINES", "Engine"]

# The ts-specialist-* values have no engine, removed from SegmentationModel.
ENGINES: dict[SegmentationModel, type[Engine]] = {
    SegmentationModel.nv_segment_ct: NVSegmentCT,
}
