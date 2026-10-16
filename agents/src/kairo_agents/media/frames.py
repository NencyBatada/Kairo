import tempfile
from dataclasses import dataclass
from pathlib import Path

from kairo_agents.media.ffmpeg import FfmpegError, run_ffmpeg

OPENING_SECONDS = 5
OPENING_RATE = 2
LATER_RATE = 1
MAX_LATER = 25
FRAME_WIDTH = 540


@dataclass(frozen=True)
class SampledFrame:
    at: float
    data: bytes


def opening_args() -> list[str]:
    return [
        "-i",
        "video.mp4",
        "-t",
        str(OPENING_SECONDS),
        "-vf",
        f"fps={OPENING_RATE},scale={FRAME_WIDTH}:-2",
        "-frames:v",
        str(OPENING_SECONDS * OPENING_RATE),
        "-q:v",
        "4",
        "opening%02d.jpg",
    ]


def later_args() -> list[str]:
    return [
        "-ss",
        str(OPENING_SECONDS),
        "-i",
        "video.mp4",
        "-vf",
        f"fps={LATER_RATE},scale={FRAME_WIDTH}:-2",
        "-frames:v",
        str(MAX_LATER),
        "-q:v",
        "4",
        "later%02d.jpg",
    ]


def opening_time(index: int) -> float:
    return round(index / OPENING_RATE, 2)


def later_time(index: int) -> float:
    return round(OPENING_SECONDS + index / LATER_RATE, 2)


async def sample_frames(video: bytes) -> list[SampledFrame]:
    with tempfile.TemporaryDirectory(prefix="kairo-frames-") as folder:
        work = Path(folder)
        (work / "video.mp4").write_bytes(video)
        await run_ffmpeg(opening_args(), work, timeout=60)
        try:
            await run_ffmpeg(later_args(), work, timeout=60)
        except FfmpegError:
            pass
        frames = [
            SampledFrame(opening_time(index), path.read_bytes())
            for index, path in enumerate(sorted(work.glob("opening*.jpg")))
        ]
        frames += [
            SampledFrame(later_time(index), path.read_bytes())
            for index, path in enumerate(sorted(work.glob("later*.jpg")))
        ]
        return frames
