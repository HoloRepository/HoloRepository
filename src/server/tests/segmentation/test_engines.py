import pytest

from models import SegmentationModel
from segmentation.addons import ADDONS, AddOn
from segmentation.engines import ENGINES, Engine

# No engine until step 7 removes these values from SegmentationModel (step 2, open question 4).
NO_ENGINE = {"ts-specialist-kidney", "ts-specialist-liver", "ts-specialist-head-n-neck"}


@pytest.mark.parametrize("model", [m for m in SegmentationModel if m not in NO_ENGINE])
def test_every_model_has_an_engine(model):
    assert issubclass(ENGINES[model], Engine)


def test_engines_describe_themselves():
    assert all(engine().describe()["engine"] == model for model, engine in ENGINES.items())


def test_addons_table():
    assert list(ADDONS) == ["kidney", "liver"]  # run order
    assert all(issubclass(addon, AddOn) for addon in ADDONS.values())


def test_unknown_model_or_addon_raises():
    with pytest.raises(ValueError):
        SegmentationModel("not-a-model")
    with pytest.raises(KeyError):
        ADDONS["not-an-addon"]
