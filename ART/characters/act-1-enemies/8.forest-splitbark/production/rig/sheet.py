"""Contact sheet: rows = poses, cols = views, each cell labelled.  (system python + PIL)
usage: python sheet.py <dir> <out.png> pose1,pose2 view1,view2 [cell]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
d, out = Path(sys.argv[1]), sys.argv[2]
poses, views = sys.argv[3].split(","), sys.argv[4].split(",")
S = int(sys.argv[5]) if len(sys.argv) > 5 else 360
W = Image.new("RGB", (S * len(views), S * len(poses)), (30, 30, 30))
dr = ImageDraw.Draw(W)
for r, p in enumerate(poses):
    for c, v in enumerate(views):
        f = d / f"{p}_{v}.png"
        if f.exists():
            im = Image.open(f).convert("RGB")
            im.thumbnail((S, S))
            W.paste(im, (c * S, r * S))
        dr.text((c * S + 6, r * S + 4), f"{p} / {v}", fill=(255, 230, 0))
W.save(out)
print(out)
