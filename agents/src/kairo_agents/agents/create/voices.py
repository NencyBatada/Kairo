import os
from dataclasses import dataclass, replace


@dataclass(frozen=True)
class Voice:
    language: str
    language_code: str
    name: str
    script: str


VOICES: dict[str, Voice] = {
    "en": Voice("en", "en-IN", "en-IN-Chirp3-HD-Achird", "latin"),
}


def voice_for(language: str) -> Voice | None:
    voice = VOICES.get(language)
    if voice is None:
        return None
    override = os.environ.get(f"KAIRO_TTS_VOICE_{language.upper()}")
    return replace(voice, name=override) if override else voice
