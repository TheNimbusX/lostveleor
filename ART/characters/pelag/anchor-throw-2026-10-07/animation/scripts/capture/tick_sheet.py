"""Покадровый лист броска из кадров съёмки (120 к/с): python tick_sheet.py <frames_dir> <out.jpg> <first> <last> <step> <cx,cy> <W,H> [cols] [label_t0_frame]
Подпись — номер кадра и тик показа от каста (кадр каста = label_t0_frame, 4 кадра = 1 тик)."""
import sys, os
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
first, last, step = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
cx, cy = (int(v) for v in sys.argv[6].split(","))
W, H = (int(v) for v in sys.argv[7].split(","))
cols = int(sys.argv[8]) if len(sys.argv) > 8 else 8
t0 = int(sys.argv[9]) if len(sys.argv) > 9 else None
files = sorted(f for f in os.listdir(src) if f.lower().endswith(".jpg"))
sel = list(range(first, min(last, len(files) - 1) + 1, step))
rows = (len(sel) + cols - 1) // cols
sheet = Image.new("RGB", (W * cols, H * rows), (20, 20, 20))
dr = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arial.ttf", 18)
except Exception:
    font = None
for k, i in enumerate(sel):
    im = Image.open(os.path.join(src, files[i])).convert("RGB")
    im = im.crop((cx - W // 2, cy - H // 2, cx + W // 2, cy + H // 2))
    x, y = (k % cols) * W, (k // cols) * H
    sheet.paste(im, (x, y))
    label = "f%d" % i + ("  C%+.2f" % ((i - t0) / 4.0) if t0 is not None else "")
    dr.rectangle((x, y, x + 150, y + 22), fill=(0, 0, 0))
    dr.text((x + 4, y + 2), label, fill=(255, 255, 0), font=font)
sheet.save(out, quality=88)
print(out, sheet.size, len(sel))
