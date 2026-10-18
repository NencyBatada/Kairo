import hashlib
import json
import re
from copy import deepcopy
from pathlib import Path
from typing import Any

from kairo_agents.agents.check.models import (
    CopyCheckRequest,
    ImageCheckRequest,
    VideoCheckRequest,
)
from kairo_agents.agents.create.media_models import FootageRequest, ImagesRequest, Photo
from kairo_agents.agents.create.models import CopyRequest
from kairo_agents.agents.plan.models import SmartBriefRequest

DEFAULT_ROOT = Path(__file__).resolve().parents[3] / "evaluations" / "v1"


class Fixtures:
    def __init__(self, root: Path = DEFAULT_ROOT):
        self.root = root.resolve()
        self.manifest = json.loads((self.root / "manifest.json").read_text(encoding="utf-8"))
        for name in self.manifest["sha256"]:
            self.file(name)
        self.data = json.loads(self.file("cases.json").decode("utf-8"))
        self.golden = json.loads(self.file("golden.json").decode("utf-8"))
        self.cases = self.data["generation"] + self.data["guardian"]
        self.validate()

    def file(self, name: str) -> bytes:
        path = self.root / name
        if path.resolve().parent != self.root or path.name != name or path.is_symlink():
            raise ValueError("Fixture paths must stay inside their version directory")
        expected = self.manifest["sha256"].get(name)
        if not isinstance(expected, str):
            raise ValueError("A fixture is missing from its version manifest")
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError("A fixture differs from its version manifest")
        return data

    def validate(self) -> None:
        if self.data["schemaVersion"] != 1 or self.data["version"] != self.manifest["version"]:
            raise ValueError("Unsupported fixture version")
        if len({case["id"] for case in self.cases}) != len(self.cases):
            raise ValueError("Fixture identifiers must be unique")
        guardian = self.data["guardian"]
        if sum(bool(case.get("expectedRule")) for case in guardian) < 20:
            raise ValueError("At least twenty planted Guardian issues are required")
        if sum(not case.get("expectedRule") for case in guardian) < 20:
            raise ValueError("At least twenty clean controls are required")
        if not any(case.get("critical") for case in guardian):
            raise ValueError("Critical Guardian controls are required")
        coverage = {(case["kind"], case.get("language")) for case in self.data["generation"]}
        if not {("brief", "en"), ("brief", "hi"), ("copy", "en"), ("copy", "hi")} <= coverage:
            raise ValueError("English and Hindi brief/copy coverage is required")
        if not {"text", "image", "video"} <= {case["medium"] for case in guardian}:
            raise ValueError("All Guardian media must be covered")
        for case in self.cases:
            if not isinstance(case["id"], str) or not re.fullmatch(r"[a-z0-9-]{1,64}", case["id"]):
                raise ValueError("Invalid fixture identifier")
            if case["kind"] == "guardian":
                rule = case.get("expectedRule")
                known = {r["id"] for r in self.data["context"]["rules"]}
                known.update(r["id"] for r in self.data["context"]["customRules"])
                if rule is not None and rule not in known:
                    raise ValueError("Unknown planted Guardian rule")
                if case.get("critical") and not rule:
                    raise ValueError("A critical fixture must identify its blocking rule")
            if case["kind"] == "brief":
                self.brief_request(case)
            elif case["kind"] == "copy":
                self.copy_request(case)
            elif case["kind"] == "images":
                self.images_request(case)
            elif case["kind"] == "video":
                self.footage_request(case)
            elif case["kind"] == "guardian":
                if case["medium"] == "text":
                    self.text_request([case])
                else:
                    self.media_check(case)
            else:
                raise ValueError("Unknown evaluation fixture kind")

    def brief_request(self, case: dict[str, Any]) -> SmartBriefRequest:
        data = deepcopy(self.data["briefRequest"])
        hindi = case["language"] == "hi"
        data["languages"] = ["en", "hi"] if hindi else ["en"]
        data["seed"] = {
            "title": "मानसून के जूते" if hindi else "Rain-ready commutes",
            "goal": (
                "महाराष्ट्र में हिन्दी बोलने वाले यात्रियों के लिए हिन्दी विज्ञापन बनाएं।"
                if hindi
                else "Reach commuters in Maharashtra using English ads."
            ),
        }
        return SmartBriefRequest.model_validate(data)

    def copy_request(self, case: dict[str, Any]) -> CopyRequest:
        context = deepcopy(self.data["context"])
        return CopyRequest.model_validate(
            {
                **context,
                "language": case["language"],
                "campaignTypes": ["search", "shopping", "demand_gen"],
                "keywords": ["बारिश के जूते"] if case["language"] == "hi" else ["waterproof shoes"],
            }
        )

    def context(self, language: str = "en") -> dict[str, Any]:
        return {**deepcopy(self.data["context"]), "language": language}

    def asset_id(self, case: dict[str, Any]) -> str:
        value = (self.data["version"] + ":" + case["id"]).encode()
        return "eval-" + hashlib.sha256(value).hexdigest()[:20]

    def photo(self, name: str = "clean.png") -> Photo:
        return Photo(mime_type="image/png", data=self.file(name))

    def reference(self) -> dict[str, Any]:
        product = self.data["context"]["products"][0]
        return {
            "id": product["id"],
            "title": product["title"],
            "description": product["description"],
            "photos": [self.photo()],
        }

    def text_request(self, cases: list[dict[str, Any]]) -> CopyCheckRequest:
        if len({case["language"] for case in cases}) != 1:
            raise ValueError("A Guardian text batch must share its language")
        return CopyCheckRequest.model_validate(
            {
                **self.context(cases[0]["language"]),
                "assets": [
                    {"id": self.asset_id(case), "kind": "search_ad", "content": case["content"]}
                    for case in cases
                ],
            }
        )

    def media_check(self, case: dict[str, Any]) -> ImageCheckRequest | VideoCheckRequest:
        data = {
            **self.context(case["language"]),
            "assetId": self.asset_id(case),
            "references": [self.reference()],
        }
        if case["medium"] == "image":
            return ImageCheckRequest.model_validate({**data, "image": self.photo(case["file"])})
        return VideoCheckRequest.model_validate(
            {
                **data,
                "video": {"data": self.file(case["file"])},
                "seconds": case["seconds"],
            }
        )

    def images_request(self, case: dict[str, Any]) -> ImagesRequest:
        return ImagesRequest.model_validate(
            {
                "brief": self.data["context"]["brief"],
                "brandKit": self.data["context"]["brandKit"],
                "products": [self.reference()],
                "shots": [
                    {"id": "s1", "productId": "p1", "style": "product", "formats": case["formats"]}
                ],
                "instruction": (
                    "Preserve the blue shoe and white sole. Add no text or extra objects."
                ),
            }
        )

    def footage_request(self, case: dict[str, Any]) -> FootageRequest:
        return FootageRequest.model_validate(
            {
                "brief": self.data["context"]["brief"],
                "brandKit": self.data["context"]["brandKit"],
                "products": [{"id": "p1", "title": self.reference()["title"]}],
                "frames": [{"productId": "p1", **self.photo().model_dump()}],
                "clips": case["clips"],
                "clipSeconds": case["seconds"],
                "instruction": (
                    "Show the blue shoe with its white sole. Add no claims, people or text."
                ),
            }
        )
