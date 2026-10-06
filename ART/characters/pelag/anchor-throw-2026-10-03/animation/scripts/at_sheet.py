"""Бросок якоря: листы из кадров at_render.py sheet (python + PIL).
python at_sheet.py <frames_dir> <anim_dir> <ref_png>
  anchorthrow-poses.jpg    — все клипы: строки «игровая камера», «сбоку», «3/4 спереди», столбцы — кадры
  anchorthrow-keyposes.jpg — 6 поз листа B-key-poses-higgsfield.png (две строки листа) рядом с нашими: сбоку / 3/4 / игровая камера
"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

fr, anim, ref = sys.argv[1], sys.argv[2], sys.argv[3]
F = lambda n, b=False: ImageFont.truetype("C:/Windows/Fonts/arial%s.ttf" % ("bd" if b else ""), n)
CELL = 200
BG, FG, YEL = (30, 32, 31), (235, 232, 220), (255, 225, 70)
tj = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
VIEWS = (("game", "игровая\nкамера"), ("side", "сбоку"), ("q34", "3/4\nспереди"))


def cell(name, size=CELL):
    return Image.open(os.path.join(fr, name)).convert("RGB").resize((size, size), Image.LANCZOS)


clips = list(tj["clips"])
cols = max(tj["clips"][c]["frames"] for c in clips)
lw = 150
H = 60 + len(clips) * (len(VIEWS) * CELL + 34)
sheet = Image.new("RGB", (lw + cols * CELL, H), BG)
d = ImageDraw.Draw(sheet)
d.text((12, 14), "Бросок якоря — клипы Pelag_AN_AnchorThrow_* (кадр = тик при W = 2, F = 10, R = 8; короче — растяжка timing.json)", fill=FG, font=F(24, True))
y = 60
for c in clips:
    info = tj["clips"][c]; short = c.replace("Pelag_AN_AnchorThrow_", "")
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
sheet.save(os.path.join(anim, "anchorthrow-poses.jpg"), quality=86)
print("poses", sheet.size)

R = Image.open(ref).convert("RGB")
xs = [175, 551, 960, 1440, 1925, 2433]
picks = tj["sheet_pose_map"]
K = 250
ks = Image.new("RGB", (150 + 6 * K, 70 + 5 * K + 70), BG)
d = ImageDraw.Draw(ks)
d.text((12, 14), "Бросок якоря — позы листа B-key-poses-higgsfield.png (сверху, две строки листа) и наши клипы на риге v6 (из выгруженных FBX)", fill=FG, font=F(22, True))
for i, (num, p) in enumerate(sorted(picks.items(), key=lambda kv: int(kv[0]))):
    x = 150 + i * K
    top = R.crop((xs[i] - 230, 150, xs[i] + 230, 700)).resize((K * 460 // 550, K), Image.LANCZOS)
    ks.paste(top, (x + (K - top.width) // 2, 70))
    bot = R.crop((xs[i] - 230, 830, xs[i] + 230, 1380)).resize((K * 460 // 550, K), Image.LANCZOS)
    ks.paste(bot, (x + (K - bot.width) // 2, 70 + K))
    short = p["clip"].replace("Pelag_AN_AnchorThrow_", "")
    for r, v in enumerate(("side", "q34", "game")):
        ks.paste(cell(f"{short}_{v}_{p['frame']:03d}.png", K), (x, 70 + (r + 2) * K))
    d.text((x + 8, 70 + 5 * K + 6), "%s: %s f%d" % (num, short, p["frame"]), fill=YEL, font=F(17))
    words, line, lines = p["what"].split(), "", []
    for wd in words:
        if len(line) + len(wd) > 30: lines.append(line); line = wd
        else: line = (line + " " + wd).strip()
    lines.append(line)
    d.text((x + 8, 70 + 5 * K + 28), "\n".join(lines[:2]), fill=FG, font=F(13))
for r, lab in enumerate(("лист\n(ряд 1)", "лист\n(ряд 2)", "наша\nсбоку", "наша\n3/4", "наша\nигровая")):
    d.text((10, 90 + r * K), lab, fill=(200, 200, 190), font=F(18))
ks.save(os.path.join(anim, "anchorthrow-keyposes.jpg"), quality=90)
print("keyposes", ks.size)
