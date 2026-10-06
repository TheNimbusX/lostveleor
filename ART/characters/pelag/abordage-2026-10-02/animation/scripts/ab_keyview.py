"""Абордаж v2: крупные ключевые кадры превью. python ab_keyview.py <preview_dir> <out.jpg> Clip:frame ..."""
import sys, os
from PIL import Image, ImageDraw, ImageFont
src, out = sys.argv[1], sys.argv[2]
picks = [a.split(":") for a in sys.argv[3:]]
views = ("side", "q34")
C = 300
F = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
sh = Image.new("RGB", (len(picks) * C, 2 * C + 24), (30, 32, 31))
d = ImageDraw.Draw(sh)
for i, (c, f) in enumerate(picks):
    d.text((i * C + 6, 4), "%s f%s" % (c, f), fill=(255, 225, 70), font=F)
    for r, v in enumerate(views):
        sh.paste(Image.open(os.path.join(src, "%s_%s_%03d.png" % (c, v, int(f)))).convert("RGB").resize((C, C)), (i * C, 24 + r * C))
sh.save(out, quality=88)
print(out, sh.size)
