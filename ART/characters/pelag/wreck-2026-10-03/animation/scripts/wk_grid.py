"""Склейка кадров превью в сетку: python wk_grid.py <папка> <шаблон glob> <out.png> [колонок] [кроп 0..1 по краям]."""
import sys, glob, os
from PIL import Image, ImageDraw
src, pat, out = sys.argv[1], sys.argv[2], sys.argv[3]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 6
crop = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0
fs = sorted(glob.glob(os.path.join(src, pat)))
ims = []
for f in fs:
    im = Image.open(f).convert("RGB")
    w, h = im.size
    if crop: im = im.crop((int(w * crop), int(h * crop), int(w * (1 - crop)), int(h * (1 - crop))))
    ImageDraw.Draw(im).text((4, 4), os.path.basename(f).rsplit(".", 1)[0], fill=(255, 255, 0))
    ims.append(im)
w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
g = Image.new("RGB", (w * cols, h * rows), (40, 40, 40))
for i, im in enumerate(ims): g.paste(im, ((i % cols) * w, (i // cols) * h))
g.save(out); print(out, len(ims))
