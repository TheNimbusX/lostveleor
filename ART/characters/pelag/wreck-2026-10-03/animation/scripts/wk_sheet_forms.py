"""Крушение v2: лист поз форм рядом с рефами B2 (позы 8–11): реф сбоку и 3/4 + кадр клипа (3/4, сбоку, камера игры).
python wk_sheet_forms.py <frames_dir> <anim_dir> <out.jpg>
Реф: motion-ref-pack/B2-key-poses-higgsfield.png (верхний ряд — сбоку, нижний — 3/4)."""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

fr, anim, out = sys.argv[1], sys.argv[2], sys.argv[3]
PACK = os.path.join(os.path.dirname(os.path.abspath(anim)), "motion-ref-pack")
REF = Image.open(os.path.join(PACK, "B2-key-poses-higgsfield.png")).convert("RGB")
SIDE = {"8": (0, 90, 640, 735), "9": (571, 90, 1360, 735), "10": (1384, 90, 2016, 735), "11": (1989, 90, 2688, 735)}
Q34 = {"8": (54, 750, 640, 1365), "9": (565, 750, 1371, 1365), "10": (1384, 750, 2003, 1365), "11": (1976, 750, 2688, 1365)}
T = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 15)
bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 18)
CW, CH = 300, 300


def fit(im):
    im = im.copy(); im.thumbnail((CW, CH))
    cell = Image.new("RGB", (CW, CH), (226, 226, 222))
    cell.paste(im, ((CW - im.width) // 2, (CH - im.height) // 2))
    return cell


rows = []
for k, (clip, frame, what) in T["sheet_pose_map_forms"].items():
    rk = k.rstrip("a")
    row = Image.new("RGB", (CW * 5, CH + 44), (30, 32, 34))
    row.paste(fit(REF.crop(SIDE[rk])), (0, 40))
    row.paste(fit(REF.crop(Q34[rk])), (CW, 40))
    for i, vn in enumerate(("q34", "side", "game")):
        im = Image.open(os.path.join(fr, "pose%s_%s.png" % (k, vn))).convert("RGB")
        w, h = im.size; c = int(w * 0.12)
        row.paste(im.crop((c, c, w - c, h - c)).resize((CW, CH)), (CW * (i + 2), 40))
    d = ImageDraw.Draw(row)
    d.text((8, 4), "поза %s — %s, кадр %d" % (rk, clip.replace("Pelag_AN_", ""), frame), fill=(255, 225, 70), font=bold)
    d.text((8, 24), what, fill=(235, 235, 225), font=font)
    for i, lab in enumerate(("реф B2: сбоку", "реф B2: 3/4", "клип: 3/4", "клип: сбоку", "клип: камера игры")):
        d.text((CW * i + 8, CH + 22), lab, fill=(170, 200, 190), font=font)
    rows.append(row)
head = Image.new("RGB", (CW * 5, 58), (20, 22, 24))
d = ImageDraw.Draw(head)
d.text((10, 6), "Крушение v2, формы — ключевые позы клипов рядом с листом B2 (перенос FBX как в Unity, риг v6)",
       fill=(255, 255, 255), font=bold)
d.text((10, 32), "якорь и цепь на кадрах клипа — ПЛАН оси хвата (прямая 1,6 м + голова), не путь запечки; сабля за кушаком; "
       "реф 10 — для обоих махов Панциря", fill=(200, 200, 200), font=font)
sheet = Image.new("RGB", (CW * 5, head.height + sum(r.height for r in rows)), (20, 22, 24))
sheet.paste(head, (0, 0)); y = head.height
for r in rows:
    sheet.paste(r, (0, y)); y += r.height
sheet.save(out, quality=88)
print(out, sheet.size)
