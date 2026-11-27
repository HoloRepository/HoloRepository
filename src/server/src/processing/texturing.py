"""Per-vertex colouring for the two mesh styles.

MZ3 and NiiVue have no UVs or image textures, so the textured style is baked into
per-vertex colours instead: a procedural tissue texture is sampled along the X, Y
and Z planes at each vertex and blended by the vertex normal (triplanar mapping).
"""

from dataclasses import dataclass
from enum import StrEnum

import numpy as np
import numpy.typing as npt
import trimesh

from models import SegmentationModel

# One RGBA row per vertex, uint8 0-255, as MZ3 stores it.
VertexColours = npt.NDArray[np.uint8]
RGB = tuple[int, int, int]


@dataclass(frozen=True)
class TissueTexture:
    """Parameters for one procedural tissue texture."""

    base: RGB
    """Dominant surface colour."""
    accent: RGB
    """Colour mixed in by the noise, e.g. darker mottling or vessels."""
    scale_mm: float
    """Size of the noise features in millimetres."""
    contrast: float
    """How strongly the accent colour shows, 0-1."""
    seed: int = 0


class Tissue(StrEnum):
    """Tissue kinds that share a look. Many labels map to one tissue."""

    liver = "liver"
    kidney = "kidney"
    spleen = "spleen"
    bone = "bone"
    muscle = "muscle"
    artery = "artery"
    vein = "vein"
    other = "other"


@dataclass(frozen=True)
class TissueLook:
    """How a tissue appears in each mesh style."""

    colour: RGB
    """Flat colour for the coloured style."""
    texture: TissueTexture
    """Procedural texture for the textured style."""


TISSUES: dict[Tissue, TissueLook] = {
    Tissue.liver: TissueLook((150, 60, 50), TissueTexture((120, 45, 38), (80, 25, 22), 4.0, 0.4)),
    Tissue.kidney: TissueLook((170, 80, 70), TissueTexture((140, 60, 52), (95, 35, 30), 3.0, 0.35)),
    Tissue.spleen: TissueLook((130, 50, 70), TissueTexture((105, 40, 55), (70, 25, 40), 3.5, 0.35)),
    Tissue.bone: TissueLook((230, 220, 195), TissueTexture((225, 215, 190), (190, 175, 145), 1.5, 0.3)),
    Tissue.muscle: TissueLook((180, 70, 70), TissueTexture((160, 55, 55), (110, 30, 35), 1.0, 0.5)),
    Tissue.artery: TissueLook((200, 40, 40), TissueTexture((185, 35, 35), (140, 20, 25), 2.0, 0.25)),
    Tissue.vein: TissueLook((60, 70, 170), TissueTexture((70, 60, 130), (45, 35, 90), 2.0, 0.25)),
    Tissue.other: TissueLook((200, 170, 150), TissueTexture((190, 160, 140), (150, 120, 105), 3.0, 0.3)),
}
"""Starting values; tune by eye in the viewer."""

LABELS: dict[SegmentationModel, dict[int, Tissue]] = {
    # TODO: fill each model's label map from its label list. Labels missing here
    # use `Tissue.other`.
    SegmentationModel.nv_segment_ct: {},
    SegmentationModel.ts_specialist_kidney: {},
    SegmentationModel.ts_specialist_liver: {},
    SegmentationModel.ts_specialist_head_n_neck: {},
}


def colour_flat(mesh: trimesh.Trimesh, colour: RGB) -> VertexColours:
    """Give every vertex of `mesh` the same colour."""
    ...


def sample_texture(
    texture: TissueTexture, points_mm: npt.NDArray[np.float64]
) -> npt.NDArray[np.float64]:
    """Evaluate `texture` at 2D points (N x 2, in mm) and return N x 3 RGB in 0-1."""
    ...


def bake_triplanar(mesh: trimesh.Trimesh, texture: TissueTexture) -> VertexColours:
    """Bake `texture` into per-vertex colours with triplanar projection.

    Samples the texture on the YZ, XZ and XY planes at each vertex and blends the
    three by the absolute vertex normal, so no UV unwrapping is needed.
    """
    ...
