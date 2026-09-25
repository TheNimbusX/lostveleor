"""Create a compact sheet of sampled approved Higgsfield reference frames."""

from pathlib import Path

import av
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[3] / "review" / "higgsfield-refs"
OUT = Path(__file__).resolve().parent / "reference_contact_sheet.png"
NAMES = ["idle_walk", "death"]
SAMPLE_FRACTIONS = (0.05, 0.25, 0.45, 0.65, 0.85)
WIDTH, HEIGHT = 300, 340

sheet = Image.new("RGB", (WIDTH * len(SAMPLE_FRACTIONS), (HEIGHT + 32) * len(NAMES)), (25, 25, 25))
draw = ImageDraw.Draw(sheet)
for row, name in enumerate(NAMES):
    container = av.open(str(ROOT / f"{name}.mp4"))
    frames = [frame.to_image() for frame in container.decode(video=0)]
    container.close()
    for col, fraction in enumerate(SAMPLE_FRACTIONS):
        index = min(len(frames) - 1, round((len(frames) - 1) * fraction))
        frame = frames[index]
        frame.thumbnail((WIDTH, HEIGHT))
        x = col * WIDTH + (WIDTH - frame.width) // 2
        y = row * (HEIGHT + 32) + (HEIGHT - frame.height) // 2 + 32
        sheet.paste(frame, (x, y))
        draw.text((col * WIDTH + 8, row * (HEIGHT + 32) + 8), f"{name} {fraction:.0%} / frame {index}", fill=(245, 235, 216))
sheet.save(OUT)
print(OUT)
