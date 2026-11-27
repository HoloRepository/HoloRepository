from abc import ABC, abstractmethod

import SimpleITK as sitk
import torch

from segmentation.timing import StageTimer


class Engine(ABC):
    """A base segmentation model. Created and loaded once at startup, then runs every job."""

    @abstractmethod
    def load(self, device: torch.device) -> None:
        """Load the weights onto the device and warm up."""

    @abstractmethod
    def segment(self, ct: sitk.Image, timer: StageTimer) -> tuple[sitk.Image, dict[int, str]]:
        """Standard CT (Int16 HU) → uint16 label map on exactly the CT's grid, and {id: name} it can output."""

    @abstractmethod
    def describe(self) -> dict:
        """Engine name, model revision and settings, for the job's run.json."""
