import os
from dataclasses import dataclass, replace

from kairo_agents.agents.create.languages import LANGUAGES


@dataclass(frozen=True)
class Voice:
    language: str
    language_code: str
    name: str
    script: str


VOICES: dict[str, Voice] = {
    language.code: Voice(language.code, language.voice_code, language.voice, language.script)
    for language in LANGUAGES.values()
    if language.speaks and language.voice_code and language.voice
}


def voice_for(language: str) -> Voice | None:
    voice = VOICES.get(language)
    if voice is None:
        return None
    override = os.environ.get(f"KAIRO_TTS_VOICE_{language.upper()}")
    return replace(voice, name=override) if override else voice
