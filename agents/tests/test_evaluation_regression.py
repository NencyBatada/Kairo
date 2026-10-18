import asyncio
import shutil

import pytest

from kairo_agents.evaluation.fixtures import Fixtures
from kairo_agents.evaluation.formats import video_errors
from kairo_agents.evaluation.regression import offline_regression


def test_versioned_fixtures_exercise_real_generation_cleaners_and_deterministic_guardian_checks():
    result = asyncio.run(offline_regression(Fixtures()))
    assert result["offlineRegressionPassed"], result
    assert result["providerRequests"] == 0
    assert result["liveEvaluationPassed"] is False
    assert {item["id"] for item in result["results"]} >= {
        "brief-en",
        "brief-hi",
        "copy-en",
        "copy-hi",
        "generated-images",
        "generated-video",
    }


def test_fixture_hashes_detect_modified_media_before_any_model_call(tmp_path):
    source = Fixtures()
    target = tmp_path / "fixtures"
    shutil.copytree(source.root, target)
    (target / "clean.png").write_bytes(b"not the versioned fixture")
    with pytest.raises(ValueError, match="manifest"):
        Fixtures(target)


@pytest.mark.parametrize("name", ["../secret", "/secret", "unknown.png"])
def test_only_manifest_files_inside_the_version_directory_can_be_read(name):
    with pytest.raises(ValueError):
        Fixtures().file(name)


def test_clean_video_controls_have_the_same_duration_and_dimensions_as_production_clips():
    fixtures = Fixtures()
    assert asyncio.run(video_errors(fixtures.file("clean.mp4"), 8)) == []
    assert asyncio.run(video_errors(fixtures.file("clean.mp4"), 6))


def test_guardian_inputs_do_not_reveal_the_expected_label_or_rule_in_asset_identifiers():
    fixtures = Fixtures()
    cases = [
        case
        for case in fixtures.data["guardian"]
        if case["medium"] == "text" and case["language"] == "en"
    ]
    request = fixtures.text_request(cases)
    for case, asset in zip(cases, request.assets, strict=True):
        assert asset.id == fixtures.asset_id(case)
        assert asset.id.startswith("eval-")
        assert not any(word in asset.id for word in ("issue", "clean", "medical", "price"))
        assert "expectedRule" not in asset.model_dump_json()
