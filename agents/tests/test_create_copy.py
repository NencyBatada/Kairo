import json
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from kairo_agents.agents.create import limits
from kairo_agents.agents.create.api import client_source
from kairo_agents.agents.create.copy import (
    business_name,
    call_to_action,
    guard_for,
    keywords,
    lines,
    path,
)
from kairo_agents.agents.create.models import (
    CopyBrief,
    CopyProduct,
    CopyRequest,
    DraftDemandGen,
    DraftKeyword,
    DraftRevision,
    DraftSearch,
    DraftTitles,
)
from kairo_agents.agents.create.prompt import (
    COPY_INSTRUCTION_PROMPT_VERSION,
    COPY_PROMPT_VERSION,
    copy_instruction_instructions,
    demand_gen_instructions,
    search_instructions,
    titles_instructions,
)
from kairo_agents.agents.sense.prompt import data_block
from kairo_agents.main import app

BRIEF = {
    "title": "Monsoon runners",
    "objective": "Sell waterproof shoes while monsoon searches peak.",
    "audiences": ["Commuters caught in the rain"],
    "keyMessages": ["Stay dry on the way to work.", "Dries in minutes."],
    "offer": "Free delivery over ₹999",
    "landingPages": ["https://acme.test/monsoon"],
}

PRODUCTS = [
    {
        "id": "p1",
        "title": "Acme Monsoon Runner",
        "description": "Waterproof mesh running shoe that dries in minutes.",
        "brand": "Acme",
        "category": "Shoes > Running",
        "price": 2499,
        "currency": "INR",
        "link": "https://acme.test/runner",
    },
    {
        "id": "p2",
        "title": "Acme Rain Boot",
        "brand": "Acme",
        "price": 1799,
        "salePrice": 1499,
        "currency": "INR",
        "link": "https://acme.test/boot",
    },
]

KIT = {
    "voice": {"summary": "Warm and practical"},
    "rules": {
        "bannedWords": ["cheap"],
        "competitors": [
            {"name": "Rival", "policy": "never"},
            {"name": "Friendly", "policy": "neutral"},
        ],
    },
}

REQUEST = {
    "brand": "Acme Shoes",
    "language": "en",
    "campaignTypes": ["search", "shopping", "demand_gen"],
    "brief": BRIEF,
    "products": PRODUCTS,
    "keywords": ["waterproof shoes", "rain boots"],
    "locked": [{"kind": "search_ad", "adGroup": "Runners", "texts": ["Stay Dry All Day"]}],
    "brandKit": KIT,
}

HEADLINES = [
    "Waterproof Running Shoes",
    "Stay Dry On Your Commute",
    "Dries In Minutes",
    "Shop Acme Runners!",
    "This headline is far too long for search ads",
    "Cheap Rain Shoes",
    "Better Than Rival",
    "Runners At ₹1999",
    "Runners At ₹2,499",
    "dries in minutes",
]

SEARCH = {
    "adGroups": [
        {
            "name": "Monsoon runners",
            "productIds": ["p1", "p9"],
            "keywords": [
                {"text": "Waterproof Running Shoes", "match": "Phrase"},
                {"text": "rain shoes", "match": "fuzzy"},
                {"text": "shoes!!!", "match": "exact"},
                {"text": "waterproof running shoes", "match": "exact"},
            ],
            "headlines": HEADLINES,
            "descriptions": [
                "Mesh runners that keep your feet dry and dry out in minutes.",
                "Free delivery over ₹999 on every pair.",
                "Get 20% off today only.",
            ],
            "path1": "rain shoes",
            "path2": "runners",
        },
        {
            "name": "Broken group",
            "productIds": [],
            "keywords": [],
            "headlines": ["Only one"],
            "descriptions": [],
            "path1": "",
            "path2": "",
        },
    ]
}

DEMAND_GEN = {
    "headlines": [
        "Monsoon-ready shoes that dry in minutes",
        "Dry feet all monsoon",
        "Cheap shoes for the rain",
    ],
    "longHeadlines": ["Waterproof runners and boots for every rainy commute this monsoon"],
    "descriptions": ["Mesh that keeps water out and dries fast.", "Free shipping today"],
    "callToAction": "Shop Now",
}

TITLES = {
    "titles": [
        {"productId": "p2", "title": "Acme Rain Boot - Waterproof Rubber, Black"},
        {"productId": "p1", "title": "Acme Monsoon Runner - Waterproof Mesh Running Shoe"},
        {"productId": "p7", "title": "Unknown product"},
    ]
}


def answer_for(schema: type) -> dict:
    return {DraftSearch: SEARCH, DraftDemandGen: DEMAND_GEN, DraftTitles: TITLES}[schema]


class FakeModels:
    def __init__(self) -> None:
        self.answers: dict[type, list[str]] = {}
        self.error: Exception | None = None
        self.calls: list[dict] = []

    async def generate_content(self, **kwargs):
        self.calls.append(kwargs)
        if self.error is not None:
            raise self.error
        schema = kwargs["config"].response_schema
        queued = self.answers.get(schema)
        text = queued.pop(0) if queued else json.dumps(answer_for(schema))
        usage = SimpleNamespace(
            prompt_token_count=100, candidates_token_count=20, thoughts_token_count=5
        )
        return SimpleNamespace(text=text, candidates=[], usage_metadata=usage)


@pytest.fixture
def models():
    fake = FakeModels()
    client = SimpleNamespace(aio=SimpleNamespace(models=fake))
    app.dependency_overrides[client_source] = lambda: lambda: client
    yield fake
    app.dependency_overrides.clear()


def guard():
    return guard_for(
        CopyBrief.model_validate(BRIEF),
        [CopyProduct.model_validate(product) for product in PRODUCTS],
        KIT,
    )


def test_length_counts_double_width_characters_twice() -> None:
    assert limits.length("Dry feet") == 8
    assert limits.length("बारिश के जूते") == len("बारिश के जूते") == 13
    assert limits.length("हिंदी") == 5
    assert limits.length("雨靴") == 4


def test_guard_drops_banned_words_competitors_and_unsupported_claims() -> None:
    rules = guard()

    assert rules.allows("Stay dry on the way to work")
    assert rules.allows("Better than Friendly")
    assert rules.allows("Runners at ₹2,499")
    assert rules.allows("Boots now ₹1,499 in the sale")
    assert rules.allows("Free delivery over ₹999")
    assert rules.allows("We offer a great deal of comfort")
    assert not rules.allows("Cheap rain shoes")
    assert not rules.allows("Better than rival brands")
    assert not rules.allows("Runners at Rs. 1999")
    assert not rules.allows("Get 20% off today")
    assert not rules.allows("Free shipping on all orders")
    assert not rules.allows("Lowest price in India")


def test_guard_names_the_rule_behind_each_finding() -> None:
    rules = guard()

    assert rules.findings("Stay dry on the way to work") == []
    assert [(item.rule, item.evidence) for item in rules.findings("Cheap shoes, unlike Rival")] == [
        ("brand.banned_words", "cheap"),
        ("brand.competitors", "Rival"),
    ]
    found = rules.findings("Get 20% off at Rs. 1999 with free shipping")
    assert [(item.rule, item.evidence) for item in found] == [
        ("policy.unsupported_claims", "Rs. 1999"),
        ("policy.unsupported_claims", "20%"),
        ("policy.unsupported_claims", "free shipping"),
    ]
    assert all(item.message for item in found)
    hindi = rules.findings("आज छूट पाएं")
    assert [(item.rule, item.evidence) for item in hindi] == [("policy.unsupported_claims", "छूट")]


def test_lines_keep_unique_texts_within_the_limit() -> None:
    kept = lines(HEADLINES, limits.SEARCH_HEADLINE, 15, guard(), True)

    assert kept == [
        "Waterproof Running Shoes",
        "Stay Dry On Your Commute",
        "Dries In Minutes",
        "Runners At ₹2,499",
    ]
    assert lines(["Rain 🌧 ready", "Dry!! fast", "Fine  copy "], 90, 4, guard()) == ["Fine copy"]


def test_keywords_are_lower_case_unique_and_valid() -> None:
    kept = keywords(
        [
            DraftKeyword(text="Waterproof Shoes", match="EXACT"),
            DraftKeyword(text="waterproof shoes", match="broad"),
            DraftKeyword(text="rain boots", match="fuzzy"),
            DraftKeyword(text="shoes!!!", match="exact"),
            DraftKeyword(text="one two three four five six seven eight nine ten eleven", match=""),
            DraftKeyword(text="cheap boots", match="phrase"),
            DraftKeyword(text="बारिश के जूते", match="phrase"),
            DraftKeyword(text="rain (boots)", match="phrase"),
        ],
        guard(),
    )

    assert [(keyword.text, keyword.match) for keyword in kept] == [
        ("waterproof shoes", "exact"),
        ("rain boots", "phrase"),
    ]


def hindi_guard():
    return guard_for(
        CopyBrief.model_validate(BRIEF),
        [CopyProduct.model_validate(product) for product in PRODUCTS],
        KIT,
        "hi",
        "Acme Shoes",
    )


def test_hindi_keywords_keep_devanagari_and_hinglish_but_not_other_scripts() -> None:
    kept = keywords(
        [
            DraftKeyword(text="बारिश के जूते", match="phrase"),
            DraftKeyword(text="barish ke joote", match="phrase"),
            DraftKeyword(text="acme जूते", match="exact"),
            DraftKeyword(text="மழை காலணி", match="phrase"),
            DraftKeyword(text="সস্তা জুতো", match="phrase"),
        ],
        hindi_guard(),
    )

    assert [keyword.text for keyword in kept] == [
        "बारिश के जूते",
        "barish ke joote",
        "acme जूते",
    ]


def test_hindi_lines_must_be_mostly_devanagari() -> None:
    rules = hindi_guard()

    assert lines(
        [
            "बारिश में भी सूखे पैर",
            "Acme Shoes के वाटरप्रूफ जूते",
            "Stay dry on your commute",
            "Waterproof shoes जूते",
            "மழையில் உலர்ந்த கால்கள்",
        ],
        limits.SEARCH_HEADLINE,
        15,
        rules,
        True,
    ) == ["बारिश में भी सूखे पैर", "Acme Shoes के वाटरप्रूफ जूते"]


def test_english_lines_drop_other_scripts() -> None:
    assert lines(["Dry feet all day", "सूखे पैर पूरे दिन"], 30, 15, guard(), True) == [
        "Dry feet all day"
    ]


def test_hindi_claims_need_the_same_support_as_english_ones() -> None:
    rules = hindi_guard()

    assert rules.allows("₹999 से ऊपर मुफ्त डिलीवरी")
    assert not rules.allows("आज 20% छूट")
    assert not rules.allows("सबसे सस्ते जूते")
    assert not rules.allows("कैशबैक पाएं")
    assert not rules.allows("आज छूट, पाएं नए जूते")
    assert rules.allows("बारिश में कोई सफर न छूटे")
    assert rules.allows("कूपनों की ज़रूरत नहीं")


def test_paths_names_and_calls_to_action_are_cleaned() -> None:
    assert path("rain shoes") == "rain-shoes"
    assert path("a-very-long-path-name") is None
    assert path("  ") is None
    assert path("जूते/बारिश") == "जूते-बारिश"
    assert path("rain, shoes!") == "rain-shoes"
    assert business_name("Acme Shoes") == "Acme Shoes"
    assert business_name("Acme Waterproof Footwear Company of India") == "Acme Waterproof Footwear"
    assert call_to_action("Learn More") == "learn_more"
    assert call_to_action("click here") == "shop_now"


def test_copy_writes_each_campaign_type_in_parallel_and_cleans_it(models, monkeypatch) -> None:
    monkeypatch.setenv("KAIRO_CREATE_MODEL", "gemini-flash-test")

    response = TestClient(app).post("/create/copy", json=REQUEST)

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "gemini-flash-test"
    assert body["promptVersion"] == COPY_PROMPT_VERSION
    assert body["usage"] == {"inputTokens": 300, "outputTokens": 75}
    assert body["assets"]["adGroups"] == [
        {
            "name": "Monsoon runners",
            "productIds": ["p1"],
            "keywords": [
                {"text": "waterproof running shoes", "match": "phrase"},
                {"text": "rain shoes", "match": "phrase"},
            ],
            "headlines": [
                "Waterproof Running Shoes",
                "Stay Dry On Your Commute",
                "Dries In Minutes",
                "Runners At ₹2,499",
            ],
            "descriptions": [
                "Mesh runners that keep your feet dry and dry out in minutes.",
                "Free delivery over ₹999 on every pair.",
            ],
            "path1": "rain-shoes",
            "path2": "runners",
            "finalUrl": "https://acme.test/runner",
        }
    ]
    assert body["assets"]["demandGen"] == {
        "headlines": ["Monsoon-ready shoes that dry in minutes", "Dry feet all monsoon"],
        "longHeadlines": ["Waterproof runners and boots for every rainy commute this monsoon"],
        "descriptions": ["Mesh that keeps water out and dries fast."],
        "businessName": "Acme Shoes",
        "callToAction": "shop_now",
        "finalUrl": "https://acme.test/monsoon",
    }
    assert body["assets"]["shoppingTitles"] == [
        {"productId": "p1", "title": "Acme Monsoon Runner - Waterproof Mesh Running Shoe"},
        {"productId": "p2", "title": "Acme Rain Boot - Waterproof Rubber, Black"},
    ]
    schemas = {call["config"].response_schema: call for call in models.calls}
    assert set(schemas) == {DraftSearch, DraftDemandGen, DraftTitles}
    assert schemas[DraftSearch]["config"].system_instruction == search_instructions()
    assert schemas[DraftDemandGen]["config"].system_instruction == demand_gen_instructions()
    assert schemas[DraftTitles]["config"].system_instruction == titles_instructions()
    assert all(call["model"] == "gemini-flash-test" for call in models.calls)
    assert all(
        call["contents"]
        == data_block(
            {
                **CopyRequest.model_validate(REQUEST).model_dump(by_alias=True),
                "languageName": "English",
                "script": "latin",
            }
        )
        for call in models.calls
    )


def test_copy_only_writes_the_campaign_types_in_the_brief(models) -> None:
    response = TestClient(app).post("/create/copy", json={**REQUEST, "campaignTypes": ["search"]})

    assert response.status_code == 200
    body = response.json()["assets"]
    assert len(body["adGroups"]) == 1
    assert body["demandGen"] is None
    assert body["shoppingTitles"] == []
    assert [call["config"].response_schema for call in models.calls] == [DraftSearch]


def test_copy_uses_a_flash_model_by_default(models, monkeypatch) -> None:
    monkeypatch.delenv("KAIRO_CREATE_MODEL", raising=False)

    response = TestClient(app).post("/create/copy", json=REQUEST)

    assert response.json()["model"] == "gemini-3.5-flash"


def test_copy_asks_once_more_when_a_part_breaks_the_rules(models) -> None:
    broken = {**DEMAND_GEN, "headlines": ["A headline that is longer than forty characters"]}
    models.answers[DraftDemandGen] = [json.dumps(broken)]

    response = TestClient(app).post(
        "/create/copy", json={**REQUEST, "campaignTypes": ["demand_gen"]}
    )

    assert response.status_code == 200
    first, second = models.calls
    assert "rejected" not in first["config"].system_instruction
    assert "Your last answer was rejected because" in second["config"].system_instruction
    assert "at least one of up to 30" in second["config"].system_instruction
    assert response.json()["assets"]["demandGen"]["headlines"] == [
        "Monsoon-ready shoes that dry in minutes",
        "Dry feet all monsoon",
    ]


def test_copy_fails_when_a_part_breaks_the_rules_twice(models) -> None:
    broken = json.dumps({"titles": [{"productId": "p9", "title": "Nothing"}]})
    models.answers[DraftTitles] = [broken, broken]

    response = TestClient(app).post("/create/copy", json={**REQUEST, "campaignTypes": ["shopping"]})

    assert response.status_code == 502
    assert len(models.calls) == 2


def test_copy_retries_an_answer_that_is_not_json(models) -> None:
    models.answers[DraftTitles] = ["not json"]

    response = TestClient(app).post("/create/copy", json={**REQUEST, "campaignTypes": ["shopping"]})

    assert response.status_code == 200
    assert "matches the schema" in models.calls[1]["config"].system_instruction


def test_copy_reports_a_model_error(models) -> None:
    models.error = RuntimeError("quota")

    response = TestClient(app).post("/create/copy", json=REQUEST)

    assert response.status_code == 502
    assert response.json() == {"error": "model_failed"}


def test_copy_rejects_a_bad_language_code(models) -> None:
    response = TestClient(app).post("/create/copy", json={**REQUEST, "language": "english"})

    assert response.status_code == 422


INSTRUCTION = {
    "brand": "Acme Shoes",
    "language": "en",
    "instruction": "Mention quick-dry",
    "kind": "search_ad",
    "current": {
        "adGroup": "Monsoon runners",
        "productIds": ["p1"],
        "headlines": ["Waterproof Running Shoes", "Stay Dry", "Dries Fast"],
        "descriptions": ["Keeps feet dry.", "Dries in minutes."],
        "path1": "rain",
    },
    "brief": BRIEF,
    "products": PRODUCTS,
    "brandKit": KIT,
}

REVISION = {
    "keywords": [],
    "headlines": ["Quick-Dry Running Shoes", "Dries In Minutes", "Stay Dry Today", "Cheap Shoes"],
    "longHeadlines": [],
    "descriptions": ["Quick-dry mesh keeps feet dry.", "Dries in minutes after rain."],
    "path1": "quick dry",
    "path2": "",
    "callToAction": "",
    "title": "",
    "summary": "  Led with   quick-dry. ",
}


def test_copy_instruction_rewrites_one_asset_within_the_rules(models) -> None:
    models.answers[DraftRevision] = [json.dumps(REVISION)]

    response = TestClient(app).post("/create/copy-instruction", json=INSTRUCTION)

    assert response.status_code == 200
    body = response.json()
    assert body["promptVersion"] == COPY_INSTRUCTION_PROMPT_VERSION
    assert body["summary"] == "Led with quick-dry."
    assert body["asset"]["adGroup"] == "Monsoon runners"
    assert body["asset"]["headlines"] == [
        "Quick-Dry Running Shoes",
        "Dries In Minutes",
        "Stay Dry Today",
    ]
    assert body["asset"]["path1"] == "quick-dry"
    assert body["asset"]["path2"] is None
    [call] = models.calls
    assert call["config"].system_instruction == copy_instruction_instructions()
    assert call["config"].response_schema is DraftRevision


@pytest.mark.parametrize(
    ("kind", "revision", "expected"),
    [
        (
            "keyword_set",
            {"keywords": [{"text": "Quick Dry Shoes", "match": "exact"}]},
            {"keywords": [{"text": "quick dry shoes", "match": "exact"}]},
        ),
        (
            "demand_gen_text",
            {
                "headlines": ["Dry in minutes"],
                "longHeadlines": ["Quick-dry runners for the monsoon"],
                "descriptions": ["Mesh that dries fast."],
                "callToAction": "learn more",
            },
            {
                "headlines": ["Dry in minutes"],
                "businessName": "Acme Shoes",
                "callToAction": "learn_more",
            },
        ),
        (
            "shopping_title",
            {"title": "Acme Monsoon Runner Quick-Dry Mesh Shoe"},
            {"title": "Acme Monsoon Runner Quick-Dry Mesh Shoe"},
        ),
    ],
)
def test_copy_instruction_handles_each_text_kind(models, kind, revision, expected) -> None:
    models.answers[DraftRevision] = [json.dumps({**REVISION, **revision})]

    response = TestClient(app).post("/create/copy-instruction", json={**INSTRUCTION, "kind": kind})

    assert response.status_code == 200
    asset = response.json()["asset"]
    for key, value in expected.items():
        assert asset[key] == value


def test_copy_instruction_fails_when_the_rewrite_breaks_the_rules_twice(models) -> None:
    broken = json.dumps({**REVISION, "headlines": ["Only One"]})
    models.answers[DraftRevision] = [broken, broken]

    response = TestClient(app).post("/create/copy-instruction", json=INSTRUCTION)

    assert response.status_code == 502


HINDI_SEARCH = {
    "adGroups": [
        {
            "name": "बारिश के जूते",
            "productIds": ["p1"],
            "keywords": [
                {"text": "बारिश के जूते", "match": "phrase"},
                {"text": "barish ke joote", "match": "phrase"},
                {"text": "மழை காலணி", "match": "phrase"},
            ],
            "headlines": [
                "बारिश में भी सूखे पैर",
                "वाटरप्रूफ रनिंग जूते",
                "मिनटों में सूखें",
                "Stay dry on your commute",
            ],
            "descriptions": [
                "मेश रनर जो आपके पैर सूखे रखें और मिनटों में सूख जाएं।",
                "Acme Shoes के जूते हर मौसम के लिए।",
            ],
            "path1": "जूते",
            "path2": "बारिश",
        }
    ]
}

ENGLISH_ONLY = {
    "adGroups": [
        {
            **SEARCH["adGroups"][0],
            "headlines": ["Stay dry on your commute", "Dries in minutes", "Rain shoes"],
        }
    ]
}


def test_copy_writes_hindi_in_devanagari_with_hinglish_keywords(models) -> None:
    models.answers[DraftSearch] = [json.dumps(HINDI_SEARCH)]

    response = TestClient(app).post(
        "/create/copy", json={**REQUEST, "language": "hi", "campaignTypes": ["search"]}
    )

    assert response.status_code == 200
    group = response.json()["assets"]["adGroups"][0]
    assert [keyword["text"] for keyword in group["keywords"]] == [
        "बारिश के जूते",
        "barish ke joote",
    ]
    assert group["headlines"] == [
        "बारिश में भी सूखे पैर",
        "वाटरप्रूफ रनिंग जूते",
        "मिनटों में सूखें",
    ]
    assert "Stay dry on your commute" not in group["headlines"]
    call = models.calls[0]
    assert '"languageName": "Hindi"' in call["contents"]
    assert '"script": "devanagari"' in call["contents"]


def test_copy_asks_again_in_hindi_when_the_answer_is_in_english(models) -> None:
    models.answers[DraftSearch] = [json.dumps(ENGLISH_ONLY), json.dumps(HINDI_SEARCH)]

    response = TestClient(app).post(
        "/create/copy", json={**REQUEST, "language": "hi", "campaignTypes": ["search"]}
    )

    assert response.status_code == 200
    first, second = models.calls
    assert "rejected" not in first["config"].system_instruction
    assert "in Hindi, in Devanagari script" in second["config"].system_instruction
    assert "Latin letters" in second["config"].system_instruction


def test_hindi_shopping_titles_skip_the_script_check(models) -> None:
    response = TestClient(app).post(
        "/create/copy", json={**REQUEST, "language": "hi", "campaignTypes": ["shopping"]}
    )

    assert response.status_code == 200
    assert [title["productId"] for title in response.json()["assets"]["shoppingTitles"]] == [
        "p1",
        "p2",
    ]


def test_copy_sends_brand_memory_lessons_with_the_brief(models) -> None:
    lesson = "Worked (message): Quick-dry claims beat price claims."
    request = {**REQUEST, "campaignTypes": ["search"], "memory": [lesson]}

    response = TestClient(app).post("/create/copy", json=request)

    assert response.status_code == 200
    [call] = models.calls
    assert lesson in call["contents"]
    assert "Brand Memory" in call["config"].system_instruction


def test_copy_refuses_too_many_memory_lessons(models) -> None:
    request = {**REQUEST, "memory": [f"Worked (message): lesson {index}" for index in range(11)]}

    response = TestClient(app).post("/create/copy", json=request)

    assert response.status_code == 422
    assert models.calls == []
