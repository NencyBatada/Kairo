"""Reproduce the synthetic browser media with the project's bundled FFmpeg."""

import asyncio
from pathlib import Path

from kairo_agents.media.ffmpeg import run_ffmpeg


async def main():
    root = Path(__file__).resolve().parent
    await run_ffmpeg(
        ["-f", "lavfi", "-i", "color=c=red:s=64x64", "-frames:v", "1", "solid-red.png"],
        root,
    )
    await run_ffmpeg(
        ["-f", "lavfi", "-i", "color=c=red:s=64x64:d=3:r=10", "-c:v", "libx264",
         "-pix_fmt", "yuv420p", "-movflags", "+faststart", "silent-red.mp4"],
        root,
    )


if __name__ == "__main__":
    asyncio.run(main())
