"""Лист кадров обзора: python sheet.py <папка кадров> <выход.jpg> [колонки] [ширина клетки] [кадры через запятую]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
src, dst = Path(sys.argv[1]), sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 8
cell = int(sys.argv[4]) if len(sys.argv) > 4 else 240
files = sorted(src.glob('*.png'))
if len(sys.argv) > 5: files = [src / f'{int(f):04d}.png' for f in sys.argv[5].split(',')]
im0 = Image.open(files[0]); h = int(cell * im0.height / im0.width)
rows = (len(files) + cols - 1) // cols
out = Image.new('RGB', (cols * cell, rows * h), (20, 20, 20)); d = ImageDraw.Draw(out)
for i, f in enumerate(files):
    im = Image.open(f).convert('RGB').resize((cell, h))
    x, y = (i % cols) * cell, (i // cols) * h
    out.paste(im, (x, y)); d.rectangle([x, y, x + 34, y + 16], fill=(0, 0, 0)); d.text((x + 3, y + 2), f.stem.lstrip('0') or '0', fill=(255, 255, 0))
out.save(dst, quality=88)
