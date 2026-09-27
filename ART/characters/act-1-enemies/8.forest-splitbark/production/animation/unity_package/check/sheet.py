"""Contact sheet of check renders: python sheet.py <prefix> <out.jpg> [cols] [cell]."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

R = Path(__file__).resolve().parent / "renders"
prefix, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 6
cell = int(sys.argv[4]) if len(sys.argv) > 4 else 280
files = sorted(p for p in R.glob(prefix + "*.png"))
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * cell), (30, 30, 30))
d = ImageDraw.Draw(sheet)
for i, p in enumerate(files):
    im = Image.open(p).convert("RGB").resize((cell, cell))
    x, y = (i % cols) * cell, (i // cols) * cell
    sheet.paste(im, (x, y))
    d.text((x + 4, y + 4), p.stem.replace(prefix, ""), fill=(255, 40, 40))
sheet.save(R / out, quality=90)
print(R / out, len(files))
