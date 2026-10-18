import json
from types import SimpleNamespace
from typing import Any

from kairo_agents.evaluation.fixtures import Fixtures


class ReplayModels:
    def __init__(self, fixtures: Fixtures, case: dict[str, Any]):
        self.fixtures = fixtures
        self.answers = fixtures.golden.get(case["id"], {})
        self.calls: list[str] = []

    async def generate_content(self, **kwargs: Any) -> Any:
        schema = getattr(kwargs["config"], "response_schema", None)
        name = schema.__name__ if schema else "image"
        self.calls.append(name)
        if name == "image":
            inline = SimpleNamespace(data=self.fixtures.file("clean.png"))
            content = SimpleNamespace(parts=[SimpleNamespace(inline_data=inline)])
            return SimpleNamespace(
                candidates=[SimpleNamespace(content=content)], usage_metadata=None
            )
        if name == "ScenePlan":
            answer = {
                "scenes": [{"frame": 0, "prompt": "Show the blue rain shoe with its white sole."}]
            }
        elif name in ("DraftReview", "DraftVideoReview"):
            answer = {"findings": []}
        elif name == "DraftImageReview":
            answer = {"onImageText": [], "findings": []}
        else:
            answer = self.answers[name]
        return SimpleNamespace(text=json.dumps(answer), candidates=[], usage_metadata=None)

    async def generate_videos(self, **kwargs: Any) -> Any:
        self.calls.append("video")
        video = SimpleNamespace(video_bytes=self.fixtures.file("clean.mp4"))
        response = SimpleNamespace(generated_videos=[SimpleNamespace(video=video)])
        return SimpleNamespace(done=True, error=None, response=response)


def replay_client(fixtures: Fixtures, case: dict[str, Any]) -> Any:
    return SimpleNamespace(aio=SimpleNamespace(models=ReplayModels(fixtures, case)))
