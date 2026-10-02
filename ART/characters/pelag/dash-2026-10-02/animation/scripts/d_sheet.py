"""Лист кадров: строки — ракурсы, столбцы — тики. python d_sheet.py <folder> <out.jpg> <views,через,запятую> <ticks> [title] [notes.json]"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont
folder, out, views, ticks = sys.argv[1], sys.argv[2], sys.argv[3].split(","), [int(t) for t in sys.argv[4].split(",")]
title = sys.argv[5] if len(sys.argv) > 5 else ""
notes = json.load(open(sys.argv[6], encoding="utf-8")) if len(sys.argv) > 6 else {}
w, h = Image.open(os.path.join(folder, f"{views[0]}_{ticks[0]:03d}.png")).size
label_w = 150
top = 56 if title else 0
cap = (44 if notes.get("ticks") else 0) + (30 if notes.get("footer") else 0)
sheet = Image.new("RGB", (label_w + len(ticks) * w, top + len(views) * h + cap), (30, 32, 31))
d = ImageDraw.Draw(sheet)
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 22)
small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 17)
big = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 26)
if title: d.text((12, 14), title, fill=(240, 236, 220), font=big)
for r, v in enumerate(views):
    y = top + r * h
    vl = notes.get("views", {}).get(v, v)
    for i, line in enumerate(vl.split("\n")):
        d.text((10, y + 12 + i * 22), line, fill=(230, 220, 160), font=small)
    for c, t in enumerate(ticks):
        x = label_w + c * w
        sheet.paste(Image.open(os.path.join(folder, f"{v}_{t:03d}.png")).convert("RGB"), (x, y))
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(30, 32, 31), width=2)
        if r == 0:
            d.text((x + 8, y + 6), f"f{t}", fill=(255, 230, 60), font=font)
for c, t in enumerate(ticks):
    txt = notes.get("ticks", {}).get(str(t), "")
    if txt:
        x = label_w + c * w
        for i, line in enumerate(txt.split("\n")):
            d.text((x + 8, top + len(views) * h + 4 + i * 19), line, fill=(220, 220, 220), font=small)
if notes.get("footer"):
    d.text((12, sheet.size[1] - 26), notes["footer"], fill=(170, 170, 160), font=small)
sheet.save(out, quality=90)
print(sheet.size, out)
