"""Optional add-ons that refine a base label map, looked up by name. Stubs until step 6.

The runner's arguments are not settled yet (step 6), so the stubs accept anything for now.
"""

from collections.abc import Callable

import SimpleITK as sitk


def kidney_addon(*args: object, **kwargs: object) -> sitk.Image:
    raise NotImplementedError("Kidney add-on: step 6")


def liver_addon(*args: object, **kwargs: object) -> sitk.Image:
    raise NotImplementedError("Liver add-on: step 6")


ADDONS: dict[str, Callable[..., sitk.Image]] = {
    "kidney": kidney_addon,
    "liver": liver_addon,
}
