"""Tile selected rendered poses for quick animation direction review."""

import sys
from pathlib import Path

from PIL import Image, ImageDraw


root = Path(sys.argv[1]).resolve()
for clip in ("Idle", "Walk", "Hit", "Death"):
    for view in ("game", "side"):
        files = sorted((root / "review" / clip / view).glob("*.png"))
        if not files:
            continue
        tile_w, tile_h = 400, 420
        sheet = Image.new("RGB", (tile_w * len(files), tile_h), (27, 27, 27))
        draw = ImageDraw.Draw(sheet)
        for i, file in enumerate(files):
            frame = Image.open(file).convert("RGB")
            frame.thumbnail((tile_w, tile_h - 24))
            x = i * tile_w + (tile_w - frame.width) // 2
            sheet.paste(frame, (x, 24))
            draw.text((i * tile_w + 8, 5), f"{clip} / {view} / {file.stem}", fill=(240, 232, 213))
        sheet.save(root / f"{clip}_{view}_sheet.png")
