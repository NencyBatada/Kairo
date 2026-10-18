import asyncio
import hashlib
import json
import os
from pathlib import Path
from types import SimpleNamespace
from typing import Any

from google import genai
from google.genai import types

from kairo_agents.agents.check import api as check
from kairo_agents.agents.check.models import CheckResponse, ImageCheckRequest, VideoCheckRequest
from kairo_agents.agents.create import api as create
from kairo_agents.agents.create import media_api as media
from kairo_agents.agents.create.media_models import Photo
from kairo_agents.agents.plan import api as plan
from kairo_agents.evaluation.auth import live_credentials
from kairo_agents.evaluation.budget import Budget, BudgetedModels, Limits
from kairo_agents.evaluation.fixtures import Fixtures
from kairo_agents.evaluation.formats import brief_errors, copy_errors, image_errors, video_errors
from kairo_agents.evaluation.judge import RUBRIC, RUBRIC_VERSION, GoogleJudge
from kairo_agents.evaluation.regression import offline_regression
from kairo_agents.evaluation.scoring import score_run


def prompt_manifest() -> dict[str, str]:
    root = Path(__file__).resolve().parents[1]
    paths = [
        path
        for group in ("plan", "create", "check")
        for path in (root / "agents" / group / "prompts").glob("*.md")
    ]
    return {
        **{
            str(p.relative_to(root)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(paths)
        },
        RUBRIC_VERSION: hashlib.sha256(RUBRIC.encode()).hexdigest(),
    }


def checked(answer: Any) -> CheckResponse:
    if not isinstance(answer, CheckResponse):
        raise ValueError("incomplete_guardian_response")
    return answer


class LiveRunner:
    def __init__(self, fixtures: Fixtures, client: Any, judge: Any, output: Path):
        self.fixtures = fixtures
        self.client = client
        self.judge = judge
        self.output = output
        self.results: list[dict[str, Any]] = []

    def clients(self) -> Any:
        return self.client

    def guardian_results(self, answer: Any, cases: list[dict[str, Any]]) -> None:
        answer = checked(answer)
        by_id = {item.asset_id: item for item in answer.results}
        if len(by_id) != len(answer.results) or set(by_id) != {
            self.fixtures.asset_id(case) for case in cases
        }:
            raise ValueError("incomplete_guardian_results")
        for case in cases:
            rules = sorted(
                {finding.rule for finding in by_id[self.fixtures.asset_id(case)].findings}
            )
            self.results.append(
                {
                    "id": case["id"],
                    "complete": True,
                    "rules": rules,
                    "accepted": not rules,
                    "model": answer.model,
                    "promptVersion": answer.prompt_version,
                }
            )

    async def generation(self, case: dict[str, Any]) -> dict[str, Any]:
        f = self.fixtures
        kind = case["kind"]
        if kind in ("brief", "copy"):
            request = f.brief_request(case) if kind == "brief" else f.copy_request(case)
            answer = await (
                plan.smart_brief(request, self.clients)
                if kind == "brief"
                else create.copy(request, self.clients)
            )
            if not hasattr(answer, "model_dump"):
                raise ValueError("incomplete_generation_response")
            errors = (
                brief_errors(answer, request) if kind == "brief" else copy_errors(answer, request)
            )
            candidate = answer.model_dump(mode="json", by_alias=True, exclude={"usage"})
            self.write(case["id"] + ".json", candidate)
            score = await self.judge.score(
                request.model_dump(mode="json", by_alias=True), candidate
            )
            return {
                "id": case["id"],
                "complete": True,
                "formatPassed": not errors,
                "failures": errors,
                **score,
                "model": answer.model,
                "promptVersion": answer.prompt_version,
            }
        errors = []
        reviews = []
        if kind == "images":
            answer = await media.images(f.images_request(case), self.clients)
            if not hasattr(answer, "images") or answer.failures or len(answer.images) != 2:
                raise ValueError("incomplete_images")
            for index, image in enumerate(answer.images):
                errors += image_errors(image.data, image.format)
                (self.output / f"{case['id']}-{index}.jpg").write_bytes(image.data)
                review = checked(
                    await check.check_image(
                        ImageCheckRequest.model_validate(
                            {
                                **f.context(),
                                "assetId": f"generated-{index}",
                                "format": image.format,
                                "image": Photo(mime_type="image/jpeg", data=image.data),
                                "references": [f.reference()],
                            }
                        ),
                        self.clients,
                    )
                )
                if len(review.results) != 1:
                    raise ValueError("incomplete_generated_image_review")
                self.write(f"{case['id']}-{index}-guardian.json", review.model_dump(mode="json"))
                reviews.append(review)
        elif kind == "video":
            answer = await media.footage(f.footage_request(case), self.clients, self.clients)
            if not hasattr(answer, "clips") or len(answer.clips) != 1:
                raise ValueError("incomplete_video")
            clip = answer.clips[0]
            errors += await video_errors(clip.data, case["seconds"])
            (self.output / (case["id"] + ".mp4")).write_bytes(clip.data)
            review = checked(
                await check.check_video(
                    VideoCheckRequest.model_validate(
                        {
                            **f.context(),
                            "assetId": "generated-video",
                            "video": {"data": clip.data},
                            "seconds": case["seconds"],
                            "references": [f.reference()],
                        }
                    ),
                    self.clients,
                )
            )
            if len(review.results) != 1:
                raise ValueError("incomplete_generated_video_review")
            self.write(case["id"] + "-guardian.json", review.model_dump(mode="json"))
            reviews.append(review)
        else:
            raise ValueError("unknown_generation_case")
        return {
            "id": case["id"],
            "complete": True,
            "formatPassed": not errors,
            "failures": errors,
            "model": answer.model,
            "promptVersion": answer.prompt_version,
            "guardianAccepted": not any(r.findings for review in reviews for r in review.results),
            "guardianFindings": [
                finding.model_dump(mode="json")
                for review in reviews
                for result in review.results
                for finding in result.findings
            ],
        }

    def write(self, name: str, data: Any) -> None:
        (self.output / name).write_text(
            json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8"
        )

    async def run(self) -> None:
        for case in self.fixtures.data["generation"]:
            try:
                self.results.append(await self.generation(case))
            except Exception as error:
                self.results.append(
                    {"id": case["id"], "complete": False, "errorType": type(error).__name__}
                )
        for language in ("en", "hi"):
            cases = [
                c
                for c in self.fixtures.data["guardian"]
                if c["medium"] == "text" and c["language"] == language
            ]
            try:
                answer = await check.check_copy(self.fixtures.text_request(cases), self.clients)
                self.guardian_results(answer, cases)
            except Exception as error:
                self.results.extend(
                    {"id": c["id"], "complete": False, "errorType": type(error).__name__}
                    for c in cases
                )
        for case in self.fixtures.data["guardian"]:
            if case["medium"] == "text":
                continue
            try:
                request = self.fixtures.media_check(case)
                answer = await (
                    check.check_image(request, self.clients)
                    if case["medium"] == "image"
                    else check.check_video(request, self.clients)
                )
                self.guardian_results(answer, [case])
            except Exception as error:
                self.results.append(
                    {"id": case["id"], "complete": False, "errorType": type(error).__name__}
                )


async def evaluate(output: Path, live: bool, limits: Limits | None = None) -> bool:
    output.mkdir(parents=True, exist_ok=True)
    report: dict[str, Any] = {
        "schemaVersion": 1,
        "mode": "live" if live else "offline",
        "commit": os.environ.get("KAIRO_EVAL_COMMIT"),
        "liveEvaluationPassed": False,
        "promptManifest": prompt_manifest(),
        "failures": [],
    }
    budget = Budget(limits)
    runner = None
    clients = []
    try:
        fixtures = Fixtures()
        report.update(fixtureVersion=fixtures.data["version"], fixtureManifest=fixtures.manifest)
        if not live:
            report.update(await offline_regression(fixtures))
            return report["offlineRegressionPassed"]
        project = os.environ.get("KAIRO_EVAL_PROJECT", "")
        credentials = live_credentials(
            project,
            os.environ.get("KAIRO_EVAL_SERVICE_ACCOUNT", ""),
            os.environ.get("KAIRO_EVAL_PROVIDER", ""),
        )
        for location in ("global", "us-central1"):
            clients.append(
                genai.Client(
                    vertexai=True,
                    project=project,
                    location=location,
                    credentials=credentials,
                    http_options=types.HttpOptions(
                        timeout=90000, retry_options=types.HttpRetryOptions(attempts=1)
                    ),
                )
            )
        wrapped = SimpleNamespace(
            aio=SimpleNamespace(
                models=BudgetedModels(clients[0].aio.models, budget, clients[1].aio.models),
                operations=clients[1].aio.operations,
            )
        )
        runner = LiveRunner(
            fixtures, wrapped, GoogleJudge(project, budget, credentials=credentials), output
        )
        async with asyncio.timeout(budget.remaining()):
            await runner.run()
        report["scores"] = score_run(fixtures.cases, runner.results)
        report["liveEvaluationPassed"] = report["scores"]["passed"]
        report["failures"] = report["scores"]["failures"]
        return report["liveEvaluationPassed"]
    except Exception as error:
        report["failures"].append(type(error).__name__)
        return False
    finally:
        if runner is not None:
            report["results"] = runner.results
        report["usage"] = budget.report()
        (output / "report.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        for client in clients:
            try:
                await client.aio.aclose()
                client.close()
            except Exception:
                pass
