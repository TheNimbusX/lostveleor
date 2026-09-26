"""Create real MP4 reviews from the fixed-camera PNG sequences."""

from pathlib import Path
import subprocess
import sys
import imageio_ffmpeg

root = Path(sys.argv[1]).resolve()
out = root.parent / "videos"
out.mkdir(exist_ok=True)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
for clip in ("Walk", "Death"):
    for view in ("game", "side"):
        frames = root / f"AN_ForestWendigo_{clip}" / view / "frames"
        path = out / f"ForestWendigo_{clip}_{view}.mp4"
        args = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error"]
        if clip == "Walk":
            args += ["-stream_loop", "3"]
        args += ["-framerate", "30", "-start_number", "0", "-i", str(frames / "%04d.png")]
        if clip == "Walk":
            args += ["-frames:v", "72"]
        args += ["-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "17", str(path)]
        subprocess.run(args, check=True)
        print(path)
