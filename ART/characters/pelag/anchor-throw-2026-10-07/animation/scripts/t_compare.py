"""Лист «поза листа | наш клип в этот тик»: python t_compare.py <frames_dir> <out.png> <tick1,...,tick7> [title]
Верх — позы 1–7 pose-sheet-v1 (верхний ряд), низ — кадры t_render (f_*_tTT.TT.png) в эти тики."""
import sys, os, glob
from PIL import Image, ImageDraw, ImageFont

D, OUTP, TK = sys.argv[1], sys.argv[2], [float(x) for x in sys.argv[3].split(",")]
TITLE = sys.argv[4] if len(sys.argv) > 4 else ""
SHEET = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "chatgpt-results", "pose-sheet-v1.png")
BOX = [(0, 70, 260, 400), (205, 70, 435, 400), (385, 70, 665, 400), (690, 70, 1050, 400), (1010, 70, 1305, 400),
       (1290, 70, 1490, 400), (1470, 70, 1672, 400)]
NAMES = ["1 замах", "2 выпуск", "3 цепь разматывается", "4 держит", "5 рывок", "6 ловля", "7 готов"]
sheet = Image.open(SHEET).convert("RGB")
H = 380
cells_top, cells_bot = [], []
for i, b in enumerate(BOX):
    c = sheet.crop(b); c = c.resize((int(c.width * H / c.height), H)); cells_top.append(c)
    fs = [f for f in glob.glob(os.path.join(D, "f_*_t*.png")) if abs(float(f.rsplit("_t", 1)[1][:-4]) - TK[i]) < 1e-3]
    if fs:
        im = Image.open(fs[0]).convert("RGB"); im = im.resize((int(im.width * H / im.height), H))
    else:
        im = Image.new("RGB", (H, H), (60, 60, 60))
    cells_bot.append(im)
W = sum(max(a.width, b.width) for a, b in zip(cells_top, cells_bot)) + 10 * 8
out = Image.new("RGB", (W, 2 * H + 90), (30, 30, 30))
dr = ImageDraw.Draw(out)
try: font = ImageFont.truetype("arial.ttf", 18)
except Exception: font = None
x = 10
for i, (a, b) in enumerate(zip(cells_top, cells_bot)):
    cw = max(a.width, b.width)
    out.paste(a, (x + (cw - a.width) // 2, 30)); out.paste(b, (x + (cw - b.width) // 2, 40 + H))
    dr.text((x + 4, 6), NAMES[i], fill=(255, 230, 120), font=font)
    dr.text((x + 4, 48 + 2 * H), "тик C+%g" % TK[i], fill=(200, 220, 255), font=font)
    x += cw + 10
if TITLE: dr.text((10, 2 * H + 66), TITLE, fill=(220, 220, 220), font=font)
out.save(OUTP)
print(OUTP, out.size)
