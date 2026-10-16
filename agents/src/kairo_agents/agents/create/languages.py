import unicodedata
from dataclasses import dataclass

SCRIPTS: dict[str, tuple[tuple[int, int], ...]] = {
    "latin": ((0x0041, 0x005A), (0x0061, 0x007A), (0x00C0, 0x024F)),
    "devanagari": ((0x0900, 0x097F), (0xA8E0, 0xA8FF), (0x1CD0, 0x1CFF)),
    "bengali": ((0x0980, 0x09FF),),
    "gurmukhi": ((0x0A00, 0x0A7F),),
    "gujarati": ((0x0A80, 0x0AFF),),
    "oriya": ((0x0B00, 0x0B7F),),
    "tamil": ((0x0B80, 0x0BFF),),
    "telugu": ((0x0C00, 0x0C7F),),
    "kannada": ((0x0C80, 0x0CFF),),
    "malayalam": ((0x0D00, 0x0D7F),),
}
NATIVE_SHARE = 0.5
LATIN_SHARE = 0.9


@dataclass(frozen=True)
class Language:
    code: str
    name: str
    script: str
    voice_code: str | None = None
    voice: str | None = None
    font: str | None = None
    romanised_keywords: bool = False

    @property
    def speaks(self) -> bool:
        return self.voice is not None and self.font is not None


LANGUAGES: dict[str, Language] = {
    language.code: language
    for language in (
        Language("en", "English", "latin", "en-IN", "en-IN-Chirp3-HD-Achird", "Noto Sans"),
        Language(
            "hi",
            "Hindi",
            "devanagari",
            "hi-IN",
            "hi-IN-Chirp3-HD-Achird",
            "Noto Sans Devanagari",
            romanised_keywords=True,
        ),
        Language("mr", "Marathi", "devanagari"),
        Language("gu", "Gujarati", "gujarati"),
        Language("ta", "Tamil", "tamil"),
        Language("te", "Telugu", "telugu"),
        Language("kn", "Kannada", "kannada"),
        Language("ml", "Malayalam", "malayalam"),
        Language("bn", "Bengali", "bengali"),
        Language("pa", "Punjabi", "gurmukhi"),
        Language("or", "Odia", "oriya"),
    )
}


def language_for(code: str) -> Language | None:
    return LANGUAGES.get(code)


def script_of(character: str) -> str | None:
    point = ord(character)
    for script, ranges in SCRIPTS.items():
        if any(start <= point <= end for start, end in ranges):
            return script
    return None


def letters(text: str) -> list[str]:
    return [char for char in text if unicodedata.category(char)[0] in ("L", "M")]


def fits(text: str, code: str, names: frozenset[str] = frozenset()) -> bool:
    language = language_for(code)
    if language is None:
        return True
    kept = " ".join(word for word in text.split() if word.casefold() not in names)
    counts: dict[str | None, int] = {}
    for char in letters(kept):
        script = script_of(char)
        counts[script] = counts.get(script, 0) + 1
    native = counts.get(language.script, 0)
    other = sum(count for script, count in counts.items() if script != language.script)
    if native + other == 0:
        return bool(letters(text))
    share = native / (native + other)
    if language.script == "latin":
        return share >= LATIN_SHARE
    return native > 0 and share >= NATIVE_SHARE


def fits_keyword(text: str, code: str, names: frozenset[str] = frozenset()) -> bool:
    language = language_for(code)
    if language is not None and language.romanised_keywords and fits(text, "en", names):
        return True
    return fits(text, code, names)


def guidance(code: str) -> str:
    language = language_for(code)
    if language is None:
        return ""
    if language.script == "latin":
        return f"Write everything in {language.name}."
    note = (
        f"Write every headline, description and keyword in {language.name}, in "
        f"{language.script.capitalize()} script. Brand and product names can stay as they are."
    )
    if language.romanised_keywords:
        note += (
            f" Keywords can also be {language.name} written in Latin letters, the way people "
            "type it into search."
        )
    return note


def language_data(code: str) -> dict[str, str]:
    language = language_for(code)
    if language is None:
        return {}
    return {"languageName": language.name, "script": language.script}
