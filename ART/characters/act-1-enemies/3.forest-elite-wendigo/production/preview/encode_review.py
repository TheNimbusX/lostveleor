"""Encode unified side/game Wendigo review clips from non-destructive PNGs."""

from pathlib import Path
import subprocess
import sys

import imageio_ffmpeg


base = Path(sys.argv[1]).resolve()
encoder = imageio_ffmpeg.get_ffmpeg_exe()
for name in ("Idle", "Walk", "Claw", "Leap", "Hit", "Death"):
    folder = base / f"AN_ForestWendigo_{name}"
    side = folder / "side" / "frames" / "%04d.png"
    game = folder / "game" / "frames" / "%04d.png"
    output = base / f"ForestWendigo_{name}_review.mp4"
    if not side.parent.is_dir() or not game.parent.is_dir():
        raise RuntimeError(f"Missing review frames for {name}")
    cmd = [
        encoder, "-y", "-hide_banner", "-loglevel", "error",
        "-framerate", "30", "-i", str(side),
        "-framerate", "30", "-i", str(game),
        "-filter_complex", "[0:v][1:v]hstack=inputs=2[v]",
        "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p",
        "-crf", "18", "-preset", "medium", "-movflags", "+faststart",
        str(output),
    ]
    subprocess.run(cmd, check=True)
    print(name, output, output.stat().st_size)
