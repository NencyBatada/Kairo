import wave
from dataclasses import dataclass
from io import BytesIO

SAMPLE_RATE = 24000


class BadAudio(Exception):
    pass


@dataclass(frozen=True)
class Pcm:
    frames: bytes
    rate: int
    width: int
    channels: int

    @property
    def seconds(self) -> float:
        return len(self.frames) / (self.rate * self.width * self.channels)


def read_wav(data: bytes) -> Pcm:
    try:
        with wave.open(BytesIO(data)) as reader:
            return Pcm(
                reader.readframes(reader.getnframes()),
                reader.getframerate(),
                reader.getsampwidth(),
                reader.getnchannels(),
            )
    except (wave.Error, EOFError) as error:
        raise BadAudio("unreadable") from error


def place(lines: list[tuple[float, Pcm]], seconds: float) -> bytes:
    if not lines:
        raise BadAudio("empty")
    first = lines[0][1]
    if any(
        (pcm.rate, pcm.width, pcm.channels) != (first.rate, first.width, first.channels)
        for _, pcm in lines
    ):
        raise BadAudio("mixed_formats")
    frame = first.width * first.channels
    buffer = bytearray(round(seconds * first.rate) * frame)
    for start, pcm in lines:
        offset = round(start * first.rate) * frame
        chunk = pcm.frames[: max(0, len(buffer) - offset)]
        buffer[offset : offset + len(chunk)] = chunk
    output = BytesIO()
    with wave.open(output, "wb") as writer:
        writer.setnchannels(first.channels)
        writer.setsampwidth(first.width)
        writer.setframerate(first.rate)
        writer.writeframes(bytes(buffer))
    return output.getvalue()
