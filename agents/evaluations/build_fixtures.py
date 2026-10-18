import hashlib
import json
import subprocess
from pathlib import Path

from golden_v1 import golden
from PIL import Image, ImageDraw, ImageFont

from kairo_agents.media.ffmpeg import ffmpeg_path

ROOT = Path(__file__).parent / "v1"
VERSION = "2026-10-18.1"


def picture(name: str, caption: str, different: bool = False) -> None:
    image = Image.new("RGB", (1200, 1200), "#f4f4f0")
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default(size=54)
    draw.text((80, 100), "ACME RAIN SHOES", font=font, fill="#142e43")
    if different:
        draw.rectangle((420, 380, 720, 800), fill="#c53d2d")
        draw.ellipse((670, 470, 850, 700), outline="#c53d2d", width=50)
    else:
        draw.polygon(
            [
                (200, 740),
                (300, 640),
                (420, 430),
                (650, 500),
                (740, 660),
                (1000, 720),
                (1030, 840),
                (200, 840),
            ],
            fill="#14658a",
        )
        draw.rounded_rectangle(
            (180, 800, 1040, 870), radius=25, fill="white", outline="#142e43", width=5
        )
        for y in (570, 610, 650):
            draw.line((470, y, 650, y + 20), fill="white", width=12)
    draw.text((80, 1000), caption, font=font, fill="#142e43")
    image.save(ROOT / (name + ".png"), optimize=True)


def video(name: str, image: str, seconds: int = 8, size: tuple[int, int] = (1080, 1920)) -> None:
    subprocess.run(
        [
            ffmpeg_path(),
            "-loglevel",
            "error",
            "-y",
            "-loop",
            "1",
            "-i",
            str(ROOT / (image + ".png")),
            "-t",
            str(seconds),
            "-vf",
            f"scale={size[0]}:{size[0]},pad={size[0]}:{size[1]}:0:{(size[1] - size[0]) // 2}:white",
            "-r",
            "2",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            str(ROOT / (name + ".mp4")),
        ],
        check=True,
    )


def build() -> None:
    ROOT.mkdir(parents=True, exist_ok=True)
    pictures = {
        "clean": "Waterproof mesh for rainy days",
        "medical": "CURES DIABETES FOREVER",
        "price": "ONLY INR 99",
        "competitor": "BETTER THAN RIVAL",
        "banned": "CHEAP RAIN SHOES",
    }
    for name, caption in pictures.items():
        picture(name, caption)
    picture("different-product", "Waterproof mesh for rainy days", different=True)
    for name in ("clean", "medical", "competitor", "banned"):
        video(name, name)

    brief = {
        "title": "Rain-ready commutes",
        "objective": "Sell waterproof mesh shoes to commuters during the monsoon.",
        "audiences": ["Commuters in Maharashtra"],
        "keyMessages": ["Waterproof mesh for rainy days"],
        "offer": "Free delivery over ₹999",
        "landingPages": ["https://acme.example/rain-shoes"],
    }
    products = [
        {
            "id": "p1",
            "title": "Acme Waterproof Rain Shoes",
            "description": "Comfortable blue waterproof mesh shoes with a white rubber sole.",
            "brand": "Acme",
            "category": "Shoes > Rain",
            "price": 2499,
            "currency": "INR",
            "link": "https://acme.example/rain-shoes",
        }
    ]
    kit = {
        "rules": {"bannedWords": ["cheap"], "competitors": [{"name": "Rival", "policy": "never"}]}
    }
    rule_texts = {
        "brand.banned_words": "No words from the Brand Kit banned list.",
        "brand.competitors": "Never mention competitors marked never in the Brand Kit.",
        "policy.unsupported_claims": (
            "Every price, offer, health claim and guarantee must be supported "
            "by the supplied catalogue or brief."
        ),
        "language.match": (
            "Use the requested language and script, allowing "
            "the Acme brand name and price numerals."
        ),
        "accuracy.product": (
            "The product must match the supplied reference photo in shape and colour."
        ),
    }
    rules = [
        {
            "id": key,
            "name": key,
            "description": value,
            "appliesTo": ["image", "video"]
            if key == "accuracy.product"
            else ["text", "video"]
            if key == "language.match"
            else ["text", "image", "video"],
        }
        for key, value in rule_texts.items()
    ]
    custom = [
        {
            "id": "custom.no-sale",
            "text": "Never use the words sale or discount in an ad.",
            "appliesTo": "text",
        },
        {
            "id": "custom.delivery",
            "text": "Never promise delivery within 24 hours.",
            "appliesTo": "text",
        },
    ]
    pairs = [
        (
            "banned-en",
            "en",
            "brand.banned_words",
            "Cheap rain shoes",
            "Waterproof rain shoes",
            False,
        ),
        ("banned-hi", "hi", "brand.banned_words", "cheap बारिश के जूते", "बारिश के जूते", False),
        ("competitor-en", "en", "brand.competitors", "Better than Rival", "Acme rain shoes", False),
        ("competitor-hi", "hi", "brand.competitors", "Rival से बेहतर", "Acme बारिश के जूते", False),
        (
            "price-en",
            "en",
            "policy.unsupported_claims",
            "Runners only ₹99",
            "Runners only ₹2,499",
            False,
        ),
        ("price-hi", "hi", "policy.unsupported_claims", "जूते सिर्फ ₹99", "जूते सिर्फ ₹2,499", False),
        (
            "medical-en",
            "en",
            "policy.unsupported_claims",
            "Cures diabetes forever",
            "Waterproof mesh shoes",
            True,
        ),
        (
            "medical-hi",
            "hi",
            "policy.unsupported_claims",
            "मधुमेह हमेशा ठीक करे",
            "बारिश में पैर सूखे रखें",
            True,
        ),
        (
            "guarantee-en",
            "en",
            "policy.unsupported_claims",
            "Guaranteed lottery win",
            "Shoes for rainy commutes",
            True,
        ),
        (
            "guarantee-hi",
            "hi",
            "policy.unsupported_claims",
            "हर बीमारी का पक्का इलाज",
            "रोज़ के सफर के लिए जूते",
            True,
        ),
        ("language-en", "en", "language.match", "बारिश के लिए जूते", "Shoes for rainy days", False),
        (
            "language-hi",
            "hi",
            "language.match",
            "Waterproof shoes for rain",
            "बारिश के लिए आरामदायक जूते",
            False,
        ),
        ("custom-en", "en", "custom.no-sale", "Half-price sale today", "Explore rain shoes", False),
        ("custom-hi", "hi", "custom.delivery", "24 घंटे में घर पहुंचाएं", "बारिश के जूते देखें", False),
    ]
    guardian = []
    for name, language, rule, planted, clean, critical in pairs:
        for label, text in (("issue", planted), ("clean", clean)):
            guardian.append(
                {
                    "id": name + "-" + label,
                    "kind": "guardian",
                    "medium": "text",
                    "language": language,
                    "expectedRule": rule if label == "issue" else None,
                    "critical": critical and label == "issue",
                    "content": {
                        "adGroup": "Rain shoes",
                        "productIds": ["p1"],
                        "headlines": [
                            text,
                            "वॉटरप्रूफ जाली वाले जूते" if language == "hi" else "Rain-ready commutes",
                            "Acme ऑनलाइन देखें" if language == "hi" else "Explore Acme Shoes",
                        ],
                        "descriptions": (
                            ["वॉटरप्रूफ जूते रोज़ के सफर के लिए।", "Acme जूते देखें और अपना जोड़ा चुनें।"]
                            if language == "hi"
                            else [
                                "Waterproof mesh shoes for rainy commutes.",
                                "Explore Acme shoes and choose your pair.",
                            ]
                        ),
                        "path1": "rain",
                        "path2": "shoes",
                    },
                }
            )
    media_pairs = [
        ("image-medical", "image", "medical.png", "clean.png", "policy.unsupported_claims", True),
        ("image-price", "image", "price.png", "clean.png", "policy.unsupported_claims", False),
        ("image-product", "image", "different-product.png", "clean.png", "accuracy.product", False),
        ("video-medical", "video", "medical.mp4", "clean.mp4", "policy.unsupported_claims", True),
        ("video-competitor", "video", "competitor.mp4", "clean.mp4", "brand.competitors", False),
        ("video-banned", "video", "banned.mp4", "clean.mp4", "brand.banned_words", False),
    ]
    for name, medium, planted, clean, rule, critical in media_pairs:
        for label, file in (("issue", planted), ("clean", clean)):
            guardian.append(
                {
                    "id": name + "-" + label,
                    "kind": "guardian",
                    "medium": medium,
                    "language": "en",
                    "expectedRule": rule if label == "issue" else None,
                    "critical": critical and label == "issue",
                    "file": file,
                    **({"seconds": 8} if medium == "video" else {}),
                }
            )
    opportunity = {
        "topic": "Monsoon footwear",
        "category": "Shoes > Rain",
        "source": "radar",
        "summary": "Search demand for waterproof shoes is rising.",
        "keywords": ["waterproof shoes"],
        "lastMonthSearches": 48000,
        "growth": 0.62,
        "regions": [{"name": "Maharashtra", "lastMonthSearches": 21000, "growth": 0.8}],
    }
    brief_request = {
        "brand": "Acme",
        "market": "India",
        "opportunity": opportunity,
        "products": [{"id": "p1", "title": products[0]["title"], "price": 2499, "currency": "INR"}],
        "budget": {
            "low": 120000,
            "high": 260000,
            "suggested": 200000,
            "currency": "INR",
            "days": 14,
            "basis": "Observed search demand",
        },
        "dates": {
            "today": "2026-10-18",
            "suggestedStart": "2026-11-02",
            "suggestedEnd": "2026-11-15",
        },
        "brandKit": kit,
    }
    generation = []
    for language in ("en", "hi"):
        generation.extend(
            [
                {"id": "brief-" + language, "kind": "brief", "language": language},
                {"id": "copy-" + language, "kind": "copy", "language": language},
            ]
        )
    generation.extend(
        [
            {"id": "generated-images", "kind": "images", "formats": ["square", "landscape"]},
            {"id": "generated-video", "kind": "video", "clips": 1, "seconds": 8},
        ]
    )
    document = {
        "schemaVersion": 1,
        "version": VERSION,
        "briefRequest": brief_request,
        "context": {
            "brand": "Acme",
            "brief": brief,
            "products": products,
            "brandKit": kit,
            "rules": rules,
            "customRules": custom,
        },
        "generation": generation,
        "guardian": guardian,
    }
    (ROOT / "cases.json").write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    (ROOT / "golden.json").write_text(
        json.dumps(golden(), ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    hashes = {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(ROOT.iterdir())
        if path.name != "manifest.json"
    }
    (ROOT / "manifest.json").write_text(
        json.dumps({"version": VERSION, "sha256": hashes}, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    build()
