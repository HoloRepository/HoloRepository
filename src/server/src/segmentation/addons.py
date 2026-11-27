"""Optional add-ons that refine a base label map: the AddOn interface and the lookup table. Stubs until step 6."""

from abc import ABC, abstractmethod

import SimpleITK as sitk
import torch


class AddOn(ABC):
    """An optional refinement of the base label map. Prepared once at startup, its model loaded on each run."""

    @abstractmethod
    def prepare(self, device: torch.device) -> bool:
        """Download the weights if missing and keep the device; False when unavailable (extra not installed)."""

    @abstractmethod
    def run(self, ct: sitk.Image, label_map: sitk.Image) -> sitk.Image | None:
        """The merged label map, or None when skipped (organ missing or tiny). Raises on failure."""

    @abstractmethod
    def describe(self) -> dict:
        """TotalSegmentator version, task id and settings, for the job's run.json."""


class KidneyAddOn(AddOn):
    def prepare(self, device: torch.device) -> bool:
        raise NotImplementedError("Kidney add-on: step 6")

    def run(self, ct: sitk.Image, label_map: sitk.Image) -> sitk.Image | None:
        raise NotImplementedError("Kidney add-on: step 6")

    def describe(self) -> dict:
        return {"addon": "kidney"}


class LiverAddOn(AddOn):
    def prepare(self, device: torch.device) -> bool:
        raise NotImplementedError("Liver add-on: step 6")

    def run(self, ct: sitk.Image, label_map: sitk.Image) -> sitk.Image | None:
        raise NotImplementedError("Liver add-on: step 6")

    def describe(self) -> dict:
        return {"addon": "liver"}


# Dictionary order is the run order: kidney before liver.
ADDONS: dict[str, type[AddOn]] = {
    "kidney": KidneyAddOn,
    "liver": LiverAddOn,
}
