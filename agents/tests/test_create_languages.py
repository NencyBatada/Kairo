import pytest

from kairo_agents.agents.create.languages import (
    LANGUAGES,
    fits,
    fits_keyword,
    guidance,
    language_data,
    language_for,
)
from kairo_agents.agents.create.voices import VOICES

BRIEF_LANGUAGES = ("en", "hi", "mr", "gu", "ta", "te", "kn", "ml", "bn", "pa", "or")


def test_every_brief_language_has_a_row_and_only_english_and_hindi_speak() -> None:
    assert set(LANGUAGES) == set(BRIEF_LANGUAGES)
    assert {code for code, language in LANGUAGES.items() if language.speaks} == {"en", "hi"}
    assert set(VOICES) == {"en", "hi"}
    assert language_for("hi").font == "Noto Sans Devanagari"
    assert language_for("xx") is None


@pytest.mark.parametrize(
    ("text", "code", "expected"),
    [
        ("बारिश में भी सूखे पैर", "hi", True),
        ("Stay dry in the rain", "hi", False),
        ("Rain shoes जूते", "hi", False),
        ("Acme के जूते", "hi", True),
        ("পায়ে শুকনো", "hi", False),
        ("Stay dry in the rain", "en", True),
        ("सूखे पैर", "en", False),
        ("மழையில் உலர்ந்த", "ta", True),
        ("पावसात कोरडे पाय", "mr", True),
        ("2499", "hi", False),
    ],
)
def test_fits_checks_the_script_of_the_language(text, code, expected) -> None:
    assert fits(text, code, frozenset({"acme"})) is expected


def test_brand_names_alone_count_as_fitting_any_language() -> None:
    assert fits("Acme Shoes", "hi", frozenset({"acme", "shoes"}))


def test_hindi_keywords_may_also_be_hinglish_but_marathi_ones_may_not() -> None:
    assert fits_keyword("barish ke joote", "hi")
    assert fits_keyword("बारिश के जूते", "hi")
    assert not fits_keyword("paus shoes", "mr")


def test_guidance_names_the_language_and_script() -> None:
    assert guidance("en") == "Write everything in English."
    assert "in Hindi, in Devanagari script" in guidance("hi")
    assert "Latin letters" in guidance("hi")
    assert "Latin letters" not in guidance("ta")
    assert guidance("xx") == ""


def test_language_data_describes_the_language_for_the_model() -> None:
    assert language_data("hi") == {"languageName": "Hindi", "script": "devanagari"}
    assert language_data("xx") == {}
