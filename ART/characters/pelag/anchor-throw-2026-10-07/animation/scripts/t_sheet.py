"""Лист кадров папки обзора: python t_sheet.py <dir> <out.jpg> [cols] [every] — подпись = номер кадра обзора."""
import sys, glob, os
from PIL import Image, ImageDraw

d, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 10
every = int(sys.argv[4]) if len(sys.argv) > 4 else 1
fs = sorted(glob.glob(os.path.join(d, "f_*.png")))[::every]
im0 = Image.open(fs[0]); w, h = im0.size
rows = (len(fs) + cols - 1) // cols
sheet = Image.new("RGB", (w * cols, h * rows), (40, 40, 40))
dr = ImageDraw.Draw(sheet)
for i, f in enumerate(fs):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(Image.open(f).convert("RGB"), (x, y))
    dr.text((x + 4, y + 4), os.path.basename(f)[2:5], fill=(255, 255, 0))
sheet.save(out, quality=85)
print(out, len(fs))
