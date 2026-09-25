from pathlib import Path
from PIL import Image, ImageDraw
import sys

root = Path(sys.argv[1])
for clip in ("Walk", "Death"):
    for view in ("game", "side"):
        chosen = (0, 3, 6, 9, 12, 15, 18) if clip == "Walk" else (0, 7, 18, 24, 30, 40, 48, 60)
        frames = [(root / f"AN_ForestWendigo_{clip}" / view / "frames" / f"{f:04d}.png") for f in chosen]
        frames = [p for p in frames if p.exists()]
        if not frames:
            continue
        size = 500
        sheet = Image.new("RGB", (size * len(frames), size + 28), (17, 17, 17))
        draw = ImageDraw.Draw(sheet)
        for i, path in enumerate(frames):
            img = Image.open(path).convert("RGB").resize((size, size))
            sheet.paste(img, (i * size, 28))
            draw.text((i * size + 8, 6), f"{clip} {view} {path.stem}", fill=(240, 240, 230))
        sheet.save(root / f"{clip}_{view}_sheet.png")
