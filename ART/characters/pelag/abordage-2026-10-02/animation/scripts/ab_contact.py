"""Абордаж v2: лист превью (python + PIL). python ab_contact.py <preview_dir> <out.jpg> [views=side,q34]"""
import sys, os, re
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
views = (sys.argv[3] if len(sys.argv) > 3 else "side,q34").split(",")
F = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 14)
files = os.listdir(src)
clips = []
for c in ("Throw", "Pull", "Punch", "Recover"):
    n = len([f for f in files if re.match(r"%s_%s_\d+\.png" % (c, views[0]), f)])
    if n: clips.append((c, n))
C = 150
cols = max(n for _, n in clips)
H = sum(len(views) * C + 20 for _ in clips)
sheet = Image.new("RGB", (60 + cols * C, H), (30, 32, 31))
d = ImageDraw.Draw(sheet)
y = 0
for c, n in clips:
    d.text((4, y + 4), c, fill=(255, 225, 70), font=F)
    for f in range(n):
        d.text((60 + f * C + 4, y + 4), "f%d" % f, fill=(235, 232, 220), font=F)
        for r, v in enumerate(views):
            im = Image.open(os.path.join(src, "%s_%s_%03d.png" % (c, v, f))).convert("RGB").resize((C, C))
            sheet.paste(im, (60 + f * C, y + 20 + r * C))
    y += len(views) * C + 20
sheet.save(out, quality=85)
print(out, sheet.size)
