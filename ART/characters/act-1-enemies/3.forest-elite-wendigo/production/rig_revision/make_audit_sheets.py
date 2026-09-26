"""Extract local video reference frames and compare with existing preview frames.

Read-only with respect to the approved sources. Writes only below rig_revision.
"""

from pathlib import Path
import subprocess

import imageio_ffmpeg
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
PROD = HERE.parent
ENEMY = PROD.parent
REFS = ENEMY / "review" / "higgsfield-refs"
REVIEW = PROD / "review" / "animation_stage"
OUT = HERE / "audit_sheets"
OUT.mkdir(parents=True, exist_ok=True)


def sheet(name, paths, width=440, cols=4):
    pictures = []
    for path in paths:
        with Image.open(path) as image:
            image = image.convert("RGB")
            h = round(image.height * width / image.width)
            image = image.resize((width, h), Image.Resampling.LANCZOS)
            pictures.append((path, image))
    if not pictures:
        return
    cell_h = max(image.height for _, image in pictures) + 28
    rows = (len(pictures) + cols - 1) // cols
    result = Image.new("RGB", (width * cols, cell_h * rows), "#24272a")
    draw = ImageDraw.Draw(result)
    for i, (path, image) in enumerate(pictures):
        x, y = (i % cols) * width, (i // cols) * cell_h
        result.paste(image, (x, y + 28))
        draw.text((x + 7, y + 5), path.stem, fill="white")
    result.save(OUT / f"{name}.jpg", quality=91)


ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
for name in ("idle_walk", "claw", "leap", "death"):
    frame_dir = OUT / f"ref_{name}"
    frame_dir.mkdir(exist_ok=True)
    subprocess.run(
        [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(REFS / f"{name}.mp4"),
         "-vf", "fps=3,scale=600:-1", "-frames:v", "20", str(frame_dir / "%03d.jpg")],
        check=True,
    )
    sheet(f"ref_{name}", sorted(frame_dir.glob("*.jpg")), cols=4)

for name in ("Walk", "Claw", "Leap", "Hit", "Death"):
    for camera in ("game", "side"):
        keyposes = REVIEW / f"AN_ForestWendigo_{name}" / camera / "keyposes"
        sheet(f"current_{name.lower()}_{camera}", sorted(keyposes.glob("*.png")), cols=4)
