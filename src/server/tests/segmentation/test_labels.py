import json
from pathlib import Path

import pytest

from segmentation import labels
from segmentation.engines.nv_segment_ct.weights import weights_dir

LABELS_JSON = Path(labels.__file__).parent / "labels.json"
# Organ names the add-ons (step 6) and post-processing (step 5) ask for; grows with those steps.
NAMES_USED = [
    "liver", "spleen", "colon", "left kidney", "right kidney",
    "left kidney cyst", "right kidney cyst", "hepatic vessel",
]  # fmt: skip


def test_labels_json_is_consistent():
    pairs = json.loads(LABELS_JSON.read_text(), object_pairs_hook=lambda p: p)  # keeps duplicate keys
    ids = [int(i) for i, _ in pairs]
    names = [dict(entry)["name"] for _, entry in pairs]
    assert len(ids) == len(set(ids)) and all(i > 0 for i in ids)
    assert len(names) == len(set(names))
    assert all(label.parent in labels.BY_ID for label in labels.BY_ID.values() if label.parent is not None)


def test_matches_nv_segment_ct_metadata():
    metadata = weights_dir() / "metadata.json"
    if not metadata.is_file():
        pytest.skip("NV-Segment-CT weights not downloaded")
    channels = json.loads(metadata.read_text())["network_data_format"]["outputs"]["pred"]["channel_def"]
    expected = {int(i): name for i, name in channels.items() if int(i) != 0}
    assert {i: labels.name_of(i) for i in range(1, 133)} == expected


@pytest.mark.parametrize("name", NAMES_USED)
def test_names_used_elsewhere_resolve(name):
    assert labels.name_of(labels.id_of(name)) == name


def test_parents():
    parent_of = {name: labels.BY_ID[labels.id_of(name)].parent for name in NAMES_USED}
    assert parent_of["left kidney cyst"] == labels.id_of("left kidney")
    assert parent_of["right kidney cyst"] == labels.id_of("right kidney")
    assert parent_of["hepatic vessel"] == labels.id_of("liver")
    assert parent_of["liver"] is None


def test_table_for_returns_only_given_ids():
    assert labels.table_for([0, 116, 1, 1]) == {
        "1": {"name": "liver", "parent": None},
        "116": {"name": "left kidney cyst", "parent": 14},
    }


def test_unknown_name_or_id_raises():
    with pytest.raises(KeyError, match="Unknown label name"):
        labels.id_of("not an organ")
    with pytest.raises(KeyError, match="Unknown label id"):
        labels.name_of(9999)
    with pytest.raises(KeyError):
        labels.table_for([9999])
