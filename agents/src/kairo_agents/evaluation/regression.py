from typing import Any

from kairo_agents.agents.check import api as check
from kairo_agents.agents.create import api as create
from kairo_agents.agents.create import media_api as media
from kairo_agents.agents.plan import api as plan
from kairo_agents.evaluation.fixtures import Fixtures
from kairo_agents.evaluation.formats import brief_errors, copy_errors, image_errors, video_errors
from kairo_agents.evaluation.replay import replay_client


async def offline_regression(fixtures: Fixtures) -> dict[str, Any]:
    results = []
    for case in fixtures.data["generation"]:
        client = replay_client(fixtures, case)
        errors = []
        if case["kind"] == "brief":
            request = fixtures.brief_request(case)
            answer = await plan.smart_brief(request, lambda client=client: client)
            errors = brief_errors(answer, request)
        elif case["kind"] == "copy":
            request = fixtures.copy_request(case)
            answer = await create.copy(request, lambda client=client: client)
            errors = copy_errors(answer, request)
        elif case["kind"] == "images":
            answer = await media.images(fixtures.images_request(case), lambda client=client: client)
            if answer.failures or len(answer.images) != 2:
                errors.append("incomplete_images")
            for image in answer.images:
                errors += image_errors(image.data, image.format)
        elif case["kind"] == "video":
            answer = await media.footage(
                fixtures.footage_request(case),
                lambda client=client: client,
                lambda client=client: client,
            )
            if len(answer.clips) != 1:
                errors.append("incomplete_video")
            for clip in answer.clips:
                errors += await video_errors(clip.data, case["seconds"])
        results.append({"id": case["id"], "passed": not errors, "failures": errors})
    for language in ("en", "hi"):
        cases = [
            case
            for case in fixtures.data["guardian"]
            if case["medium"] == "text" and case["language"] == language
        ]
        client = replay_client(fixtures, {"id": "deterministic-guardian"})
        answer = await check.check_copy(fixtures.text_request(cases), lambda client=client: client)
        by_id = {result.asset_id: result for result in answer.results}
        for case in cases:
            if not case.get("expectedRule"):
                results.append(
                    {
                        "id": case["id"],
                        "passed": not by_id[case["id"]].findings,
                        "failures": [finding.rule for finding in by_id[case["id"]].findings],
                    }
                )
            elif case["expectedRule"] in {
                "brand.banned_words",
                "brand.competitors",
                "language.match",
            }:
                found = any(
                    finding.rule == case["expectedRule"] for finding in by_id[case["id"]].findings
                )
                results.append(
                    {
                        "id": case["id"],
                        "passed": found,
                        "failures": [] if found else ["missing_deterministic_finding"],
                    }
                )
    return {
        "mode": "offline",
        "fixtureVersion": fixtures.data["version"],
        "fixtureManifest": fixtures.manifest,
        "offlineRegressionPassed": all(r["passed"] for r in results),
        "liveEvaluationPassed": False,
        "providerRequests": 0,
        "results": results,
    }
