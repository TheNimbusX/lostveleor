"""Шквал v2: листы из кадров sq_render.py (обычный python + PIL).
python sq_sheet.py <frames_dir> <anim_dir> <ref_png>
  squall2-poses.jpg    — все клипы: строки «игровая камера» и «сбоку», столбцы — кадры (= тики)
  squall2-keyposes.jpg — 6 поз листа владельца: реф (верхний ряд листа, сбоку) / наша сбоку / наша в игровой камере
"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

fr, anim, ref = sys.argv[1], sys.argv[2], sys.argv[3]
F = lambda n, b=False: ImageFont.truetype("C:/Windows/Fonts/arial%s.ttf" % ("bd" if b else ""), n)
CELL = 220
BG, FG, YEL = (30, 32, 31), (235, 232, 220), (255, 225, 70)
tj = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))


def cell(name):
    return Image.open(os.path.join(fr, name)).convert("RGB").resize((CELL, CELL), Image.LANCZOS)


# ---- все клипы ---------------------------------------------------------------------------
clips = [c for c in tj["clips"]]
cols = max(tj["clips"][c]["frames"] for c in clips)
lw = 190
H = 60 + len(clips) * (2 * CELL + 34)
sheet = Image.new("RGB", (lw + cols * CELL, H), BG)
d = ImageDraw.Draw(sheet)
d.text((12, 14), "Шквал v2 — клипы Pelag_AN_Squall2_* (кадр = тик 30 к/с; полёт 0–6 растягивается видом на 2–6 тиков)", fill=FG, font=F(24, True))
y = 60
for c in clips:
    info = tj["clips"][c]; short = c.replace("Pelag_AN_Squall2_", "")
    d.text((10, y + 4), short, fill=YEL, font=F(22, True))
    marks = info.get("frame_marks", {})
    for t in range(info["frames"]):
        x = lw + t * CELL
        lab = "f%d %s" % (t, marks.get(str(t), ""))
        d.text((x + 6, y + 6), lab, fill=FG, font=F(16))
        for r, v in enumerate(("game", "side")):
            sheet.paste(cell(f"{short}_{v}_{t:03d}.png"), (x, y + 30 + r * CELL))
    d.text((10, y + 40), "игровая\nкамера", fill=(200, 200, 190), font=F(17))
    d.text((10, y + 40 + CELL), "сбоку", fill=(200, 200, 190), font=F(17))
    y += 2 * CELL + 34
sheet.save(os.path.join(anim, "squall2-poses.jpg"), quality=88)
print("poses", sheet.size)

# ---- 6 поз листа владельца ---------------------------------------------------------------
R = Image.open(ref).convert("RGB")
xs = [183, 445, 735, 990, 1263, 1513]
picks = tj["sheet_pose_map"]
K = 260
ks = Image.new("RGB", (150 + 6 * K, 70 + 3 * K + 40), BG)
d = ImageDraw.Draw(ks)
d.text((12, 14), "Шквал v2 — позы листа B-key-poses-chatgpt.png на нашем риге", fill=FG, font=F(24, True))
for i, (num, p) in enumerate(sorted(picks.items(), key=lambda kv: int(kv[0]))):
    x = 150 + i * K
    crop = R.crop((xs[i] - 135, 70, xs[i] + 135, 455)).resize((K * 270 // 385, K), Image.LANCZOS)
    ks.paste(crop, (x + (K - crop.width) // 2, 70))
    short = p["clip"].replace("Pelag_AN_Squall2_", "")
    for r, v in enumerate(("side", "game")):
        ks.paste(Image.open(os.path.join(fr, f"{short}_{v}_{p['frame']:03d}.png")).convert("RGB").resize((K, K)), (x, 70 + (r + 1) * K))
    d.text((x + 8, 70 + 3 * K + 6), "%s: %s f%d" % (num, short, p["frame"]), fill=YEL, font=F(17))
for r, lab in enumerate(("лист\n(сбоку)", "наша\nсбоку", "наша\nигровая")):
    d.text((10, 90 + r * K), lab, fill=(200, 200, 190), font=F(18))
ks.save(os.path.join(anim, "squall2-keyposes.jpg"), quality=90)
print("keyposes", ks.size)
