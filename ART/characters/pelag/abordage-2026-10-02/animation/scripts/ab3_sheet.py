"""Абордаж v3: листы из кадров ab3_render.py sheet (python + PIL).
python ab3_sheet.py <frames_dir> <anim_dir> <B2_png> <out_b2_compare.jpg>
  <anim_dir>/abordage3-poses.jpg — новые клипы: строки «игровая камера», «сбоку», «3/4 спереди», столбцы — кадры
  <out_b2_compare.jpg>          — позы 7–10 листа B2 (сбоку, 3/4) над нашими кадрами (сбоку, 3/4, игровая камера)
"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

fr, anim, ref, out2 = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
F = lambda n, b=False: ImageFont.truetype("C:/Windows/Fonts/arial%s.ttf" % ("bd" if b else ""), n)
CELL = 200
BG, FG, YEL = (30, 32, 31), (235, 232, 220), (255, 225, 70)
tj = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
VIEWS = (("game", "игровая\nкамера"), ("side", "сбоку"), ("q34", "3/4\nспереди"))
P = "Pelag_AN_Abordage2_"


def cell(name, size=CELL):
    return Image.open(os.path.join(fr, name)).convert("RGB").resize((size, size), Image.LANCZOS)


clips = [P + c for c in ("Throw", "PullShort", "Uppercut", "Slam")]
cols = max(tj["clips"][c]["frames"] for c in clips)
lw = 150
H = 60 + len(clips) * (len(VIEWS) * CELL + 34)
sheet = Image.new("RGB", (lw + cols * CELL, H), BG)
d = ImageDraw.Draw(sheet)
d.text((12, 14), "Абордаж v3 — новые клипы (кадр = тик: Throw при W = 3, A = 6; PullShort при P = 5; Uppercut/Slam 1:1)", fill=FG, font=F(24, True))
y = 60
for c in clips:
    info = tj["clips"][c]; short = c.replace(P, "")
    d.text((10, y + 4), short, fill=YEL, font=F(22, True))
    marks = info.get("frame_marks", {})
    for t in range(info["frames"]):
        x = lw + t * CELL
        d.text((x + 6, y + 6), "f%d %s" % (t, marks.get(str(t), "")), fill=FG, font=F(15))
        for r, (v, _) in enumerate(VIEWS):
            sheet.paste(cell(f"{short}_{v}_{t:03d}.png"), (x, y + 30 + r * CELL))
    for r, (_, lab) in enumerate(VIEWS):
        d.text((10, y + 40 + r * CELL), lab, fill=(200, 200, 190), font=F(17))
    y += len(VIEWS) * CELL + 34
sheet.save(os.path.join(anim, "abordage3-poses.jpg"), quality=86)
print("poses", sheet.size)

R = Image.open(ref).convert("RGB")
CROPS = {"7": (0, 620), "8": (640, 1300), "9": (1290, 2140), "10": (2120, 2688)}     # по X исходника 2688×1520
ROWS_Y = ((80, 710), (790, 1420))
picks = tj["sheet_pose_map_b2"]
K = 300
ks = Image.new("RGB", (150 + 4 * K, 70 + 5 * K + 80), BG)
d = ImageDraw.Draw(ks)
d.text((12, 14), "Абордаж v3 — позы 7–10 листа B2-key-poses-higgsfield.png (сверху) и наши клипы на риге v6 (из выгруженных FBX)",
       fill=FG, font=F(22, True))
for i, num in enumerate(("7", "8", "9", "10")):
    p = picks[num]; x = 150 + i * K
    for r, (y0, y1) in enumerate(ROWS_Y):
        x0, x1 = CROPS[num]
        im = R.crop((x0, y0, x1, y1)); sc = min(K / im.width, K / im.height)
        im = im.resize((int(im.width * sc), int(im.height * sc)), Image.LANCZOS)
        ks.paste(im, (x + (K - im.width) // 2, 70 + r * K + (K - im.height) // 2))
    short = p["clip"].replace(P, "")
    for r, v in enumerate(("side", "q34", "game")):
        ks.paste(cell(f"{short}_{v}_{p['frame']:03d}.png", K), (x, 70 + (r + 2) * K))
    lab = "%s: %s f%d" % (num, short, p["frame"]) + (" (контакт f%d)" % p["contact_frame"] if "contact_frame" in p else "")
    d.text((x + 8, 70 + 5 * K + 6), lab, fill=YEL, font=F(17))
    words, line, lines = p["what"].split(), "", []
    for wd in words:
        if len(line) + len(wd) > 36: lines.append(line); line = wd
        else: line = (line + " " + wd).strip()
    lines.append(line)
    d.text((x + 8, 70 + 5 * K + 28), "\n".join(lines[:3]), fill=FG, font=F(13))
for r, lab in enumerate(("лист B2\n(сбоку)", "лист B2\n(3/4)", "наша\nсбоку", "наша\n3/4", "наша\nигровая")):
    d.text((10, 90 + r * K), lab, fill=(200, 200, 190), font=F(18))
ks.save(out2, quality=90)
print("b2 compare", ks.size)
