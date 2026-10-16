import asyncio
import os
import subprocess
from pathlib import Path

import imageio_ffmpeg


class FfmpegError(Exception):
    pass


def ffmpeg_path() -> str:
    return os.environ.get("KAIRO_FFMPEG") or imageio_ffmpeg.get_ffmpeg_exe()


def _run(args: list[str], cwd: Path, timeout: float) -> None:
    try:
        result = subprocess.run(
            [ffmpeg_path(), "-hide_banner", "-loglevel", "error", "-nostdin", "-y", *args],
            cwd=cwd,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired as error:
        raise FfmpegError("timed_out") from error
    if result.returncode != 0:
        raise FfmpegError(result.stderr.decode("utf-8", errors="replace")[-2000:])


async def run_ffmpeg(args: list[str], cwd: Path, timeout: float = 150) -> None:
    await asyncio.to_thread(_run, args, cwd, timeout)
