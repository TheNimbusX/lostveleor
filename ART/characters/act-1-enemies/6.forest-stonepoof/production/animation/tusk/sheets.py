"""Листы кадров просмотра: python tusk/sheets.py <префикс> [кадры через запятую]."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
OUT = Path(__file__).resolve().parents[2] / 'review' / 'animation_tusk'
prefix = sys.argv[1] if len(sys.argv) > 1 else 'master'
frames = [int(x) for x in sys.argv[2].split(',')] if len(sys.argv) > 2 else [0, 4, 8, 10, 11, 12, 13, 14, 15, 16, 18, 20, 23, 26]
for view in ('side', 'game', 'front'):
    folder = OUT / f'{prefix}_{view}_frames'
    if not folder.exists():
        continue
    ims = [Image.open(folder / f'{f:04d}.png').convert('RGB') for f in frames]
    w, h = ims[0].size
    cols = 7
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (w * cols, h * rows), (40, 40, 40))
    for i, (f, im) in enumerate(zip(frames, ims)):
        d = ImageDraw.Draw(im)
        d.rectangle((0, 0, 44, 18), fill=(0, 0, 0))
        d.text((4, 3), f'f{f}' + (' HIT' if f == 14 else ''), fill=(255, 220, 0))
        sheet.paste(im, ((i % cols) * w, (i // cols) * h))
    sheet.save(OUT / f'sheet_{prefix}_{view}.jpg', quality=88)
    print('SHEET', view, sheet.size)
