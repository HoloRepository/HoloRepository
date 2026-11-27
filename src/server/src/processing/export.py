"""Write coloured and textured mesh variants for a segmentation."""

from pathlib import Path

import trimesh
from nibabel.spatialimages import SpatialImage

from models import MeshStyle, SegmentationModel
from processing.texturing import VertexColours


def combine(
    meshes: dict[int, trimesh.Trimesh], colours: dict[int, VertexColours]
) -> tuple[trimesh.Trimesh, VertexColours]:
    """Merge per-label meshes and their vertex colours into one mesh."""
    ...


def write_mz3(mesh: trimesh.Trimesh, colours: VertexColours, path: Path) -> None:
    """Write `mesh` with per-vertex RGBA colours as an MZ3 file."""
    ...


def export_mesh_variants(
    segmentation: SpatialImage, model: SegmentationModel, out_dir: Path
) -> dict[MeshStyle, Path]:
    """Mesh each label once, then write one MZ3 per style.

    Both files share the same geometry so a viewer can switch between them without
    the camera moving.
    """
    ...
