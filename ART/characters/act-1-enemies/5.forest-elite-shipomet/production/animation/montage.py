"""Montage preview stills: python montage.py <dir> <Take> <out.jpg> [cols]  (system python, PIL)."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

d, take, out = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 6
files = sorted(d.glob(f"{take}_game_*.png")) + sorted(d.glob(f"{take}_side_*.png"))
ims = [Image.open(p).convert("RGB") for p in files]
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), (20, 20, 20))
dr = ImageDraw.Draw(sheet)
for i, (p, im) in enumerate(zip(files, ims)):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im, (x, y))
    dr.text((x + 6, y + 4), p.stem.replace(take + "_", ""), fill=(255, 230, 120))
sheet.save(out, quality=88)
print(out)
