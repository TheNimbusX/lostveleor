"""Contact sheet of pose renders: rows = poses, columns = views (system python + PIL).

usage: python montage.py <dir> <out.png> [scale]
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

d = Path(sys.argv[1])
out = Path(sys.argv[2])
scale = float(sys.argv[3]) if len(sys.argv) > 3 else 0.5
poses = ["rest", "arms_up", "crouch", "leg_lift"]
views = ["front", "side"]
ims = [[Image.open(d / f"{p}_{v}.png").convert("RGB") for v in views] for p in poses]
w, h = ims[0][0].size
tw, th = int(w * scale), int(h * scale)
sheet = Image.new("RGB", (tw * len(poses), th * len(views)), (20, 20, 20))
draw = ImageDraw.Draw(sheet)
for i, p in enumerate(poses):
    for j, v in enumerate(views):
        sheet.paste(ims[i][j].resize((tw, th)), (i * tw, j * th))
        draw.text((i * tw + 6, j * th + 6), f"{p} / {v}", fill=(255, 255, 0))
sheet.save(out)
print(out, sheet.size)
