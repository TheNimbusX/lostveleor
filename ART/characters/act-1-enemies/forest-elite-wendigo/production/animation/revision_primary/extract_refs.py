"""Sample the owner-approved motion references for pose study."""

from pathlib import Path
import subprocess

import imageio_ffmpeg


HERE = Path(__file__).resolve().parent
refs = HERE.parents[2] / "review" / "higgsfield-refs"
exe = imageio_ffmpeg.get_ffmpeg_exe()
for name in ("claw", "leap"):
    source = refs / f"{name}.mp4"
    output = HERE / "ref_frames" / name
    output.mkdir(parents=True, exist_ok=True)
    result = subprocess.run([
        exe, "-y", "-hide_banner", "-loglevel", "error", "-i", str(source),
        "-vf", "fps=5,scale=960:-2", str(output / "%03d.png")
    ], check=True)
    print(name, len(list(output.glob("*.png"))), output)
