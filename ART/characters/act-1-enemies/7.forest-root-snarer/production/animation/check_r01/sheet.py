"""Tile rendered check frames into a labelled sheet (system python + PIL).
python sheet.py <frames_dir> <out.jpg> <glob> [cols]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

src, out, pat = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 4
files = sorted(src.glob(pat))
ims = [Image.open(f).convert("RGB") for f in files]
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (w * cols, h * rows), (40, 40, 40))
try:
    fnt = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 18)
except OSError:
    fnt = ImageFont.load_default()
for i, (f, im) in enumerate(zip(files, ims)):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im, (x, y))
    d = ImageDraw.Draw(sheet)
    d.rectangle([x, y, x + w - 1, y + h - 1], outline=(20, 20, 20))
    d.text((x + 6, y + 4), f.stem, fill=(255, 240, 120), font=fnt)
sheet.save(out, quality=88)
print(out, len(ims))
