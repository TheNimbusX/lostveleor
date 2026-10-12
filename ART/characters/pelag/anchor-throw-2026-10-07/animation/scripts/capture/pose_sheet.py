"""clips-vs-poses.png: верх — позы 1–7 pose-sheet-v1 (верхний ряд листа), низ — наш клип в этот тик (Unity, без Play,
artifacts/throw-0710/poses из pose_sheet.cs). python pose_sheet.py [out.png] [title]"""
import sys, os, glob
from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\Users\d.grab\Desktop\the-game"
SHEET = os.path.join(ROOT, r"ART\characters\pelag\anchor-throw-2026-10-07\chatgpt-results\pose-sheet-v1.png")
POSES = os.path.join(ROOT, r"artifacts\throw-0710\poses")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, r"artifacts\throw-0710\clips-vs-poses.png")
TITLE = sys.argv[2] if len(sys.argv) > 2 else ""
BOX = [(0, 70, 260, 400), (205, 70, 435, 400), (385, 70, 665, 400), (690, 70, 1050, 400), (1010, 70, 1305, 400),
       (1290, 70, 1490, 400), (1470, 70, 1672, 400)]
NAMES = ["1 замах", "2 выпуск", "3 цепь разматывается", "4 держит", "5 рывок", "6 ловля", "7 готов"]
TICKS = ["C+1 · Throw 1", "C+2 (выпуск) · Throw 2", "C+5 · Throw 5", "C+10 HoldTick · Yank 0", "C+13 · Haul 1",
         "C+17 ловля · Catch 0", "C+23 · Catch 6"]
sheet = Image.open(SHEET).convert("RGB")
H = 400
tops, bots = [], []
files = sorted(glob.glob(os.path.join(POSES, "*.png")))
for i, b in enumerate(BOX):
    c = sheet.crop(b); tops.append(c.resize((int(c.width * H / c.height), H)))
    im = Image.open(files[i]).convert("RGB"); bots.append(im.resize((int(im.width * H / im.height), H)))
W = sum(max(a.width, b.width) for a, b in zip(tops, bots)) + 10 * 8
out = Image.new("RGB", (W, 2 * H + 110), (30, 30, 30))
dr = ImageDraw.Draw(out)
try:
    font = ImageFont.truetype("arial.ttf", 19)
except Exception:
    font = None
x = 10
for i, (a, b) in enumerate(zip(tops, bots)):
    cw = max(a.width, b.width)
    out.paste(a, (x + (cw - a.width) // 2, 32)); out.paste(b, (x + (cw - b.width) // 2, 44 + H))
    dr.text((x + 4, 6), "лист: " + NAMES[i], fill=(255, 230, 120), font=font)
    dr.text((x + 4, 52 + 2 * H), "наш клип: " + TICKS[i], fill=(200, 220, 255), font=font)
    x += cw + 10
dr.text((10, 2 * H + 82), TITLE or "Бросок якоря 07.10 — Pelag_AN_AnchorThrow2_* в Unity (без Play, BakeMesh), тики Sim при броске на 7 м",
        fill=(220, 220, 220), font=font)
out.save(OUT)
print(OUT, out.size)
