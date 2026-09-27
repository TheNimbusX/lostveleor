"""Tile PNG stills into one labelled sheet (system python + PIL).
python sheet.py <out.jpg> <cols> <img> [<img> ...]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

out, cols, files = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = [Image.open(f).convert("RGB") for f in files]
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (w * cols, h * rows), (30, 30, 30))
for i, (f, im) in enumerate(zip(files, ims)):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im.resize((w, h)), (x, y))
    ImageDraw.Draw(sheet).text((x + 6, y + 4), Path(f).stem, fill=(255, 255, 90))
sheet.save(out, quality=88)
print("SHEET", out, sheet.size)
