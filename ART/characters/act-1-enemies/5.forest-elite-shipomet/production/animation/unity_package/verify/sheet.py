"""Contact sheet (system python + PIL): python sheet.py <dir> <glob> <out.jpg> [cols] [cell_px]"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

d, pat, out = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 4
cell = int(sys.argv[5]) if len(sys.argv) > 5 else 400
files = sorted(d.glob(pat))
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * cell), (20, 20, 20))
dr = ImageDraw.Draw(sheet)
for i, p in enumerate(files):
    im = Image.open(p).convert("RGB").resize((cell, cell))
    x, y = (i % cols) * cell, (i // cols) * cell
    sheet.paste(im, (x, y))
    dr.text((x + 6, y + 4), p.stem, fill=(255, 230, 120))
sheet.save(out, quality=90)
print(out, len(files))
