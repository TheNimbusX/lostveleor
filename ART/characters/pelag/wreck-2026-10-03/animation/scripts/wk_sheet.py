"""Крушение v2: лист поз рядом с рефами — для каждой позы листа B (1–7): вырезка рефа + кадр клипа (3/4, сбоку, игра).
python wk_sheet.py <frames_dir> <anim_dir> <out.jpg>
Рефы: B-key-poses-fixed-higgsfield.png (позы 1, 2, 4–7), B-key-poses-fixed-higgsfield-v1.png (поза 3 — лучше в v1)."""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

fr, anim, out = sys.argv[1], sys.argv[2], sys.argv[3]
PACK = os.path.join(os.path.dirname(os.path.abspath(anim)), "motion-ref-pack")
REF = {k: "B-key-poses-fixed-higgsfield.png" for k in "124567"}
REF["3"] = "B-key-poses-fixed-higgsfield-v1.png"
BOX = {"1": (0, 95, 310, 400), "2": (290, 95, 690, 400), "3": (680, 95, 965, 400), "4": (945, 95, 1344, 400),
       "5": (0, 395, 420, 705), "6": (440, 430, 965, 705), "7": (945, 430, 1344, 705)}
T = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 15)
bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 18)
CW, CH = 300, 300
rows = []
for k in "1234567":
    clip, frame, what = T["sheet_pose_map"][k]
    ref = Image.open(os.path.join(PACK, REF[k])).convert("RGB").crop(BOX[k])
    ref.thumbnail((CW, CH))
    row = Image.new("RGB", (CW * 4, CH + 44), (30, 32, 34))
    row.paste(ref, ((CW - ref.width) // 2, 40 + (CH - ref.height) // 2))
    for i, vn in enumerate(("q34", "side", "game")):
        im = Image.open(os.path.join(fr, "pose%s_%s.png" % (k, vn))).convert("RGB")
        w, h = im.size; c = int(w * 0.12)
        im = im.crop((c, c, w - c, h - c)).resize((CW, CH))
        row.paste(im, (CW * (i + 1), 40))
    d = ImageDraw.Draw(row)
    d.text((8, 4), "поза %s — %s, кадр %d" % (k, clip.replace("Pelag_AN_", ""), frame), fill=(255, 225, 70), font=bold)
    d.text((8, 24), what, fill=(235, 235, 225), font=font)
    for i, lab in enumerate(("реф листа B", "клип: 3/4", "клип: сбоку", "клип: камера игры")):
        d.text((CW * i + 8, CH + 22), lab, fill=(170, 200, 190), font=font)
    rows.append(row)
head = Image.new("RGB", (CW * 4, 58), (20, 22, 24))
d = ImageDraw.Draw(head)
d.text((10, 6), "Крушение v2 — ключевые позы клипов рядом с листом B (перенос FBX как в Unity, риг v6)", fill=(255, 255, 255), font=bold)
d.text((10, 32), "якорь и цепь на кадрах клипа — ПЛАН оси хвата (прямая 1,6 м + голова), не путь запечки; сабля за кушаком",
       fill=(200, 200, 200), font=font)
sheet = Image.new("RGB", (CW * 4, head.height + sum(r.height for r in rows)), (20, 22, 24))
sheet.paste(head, (0, 0)); y = head.height
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.save(out, quality=88)
print(out, sheet.size)
