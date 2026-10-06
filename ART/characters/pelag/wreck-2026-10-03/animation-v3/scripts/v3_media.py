"""Крушение v3, Swing1: GIF и лист ключевых поз для владельца из кадров v3_render.py.

python v3_media.py <frames_dir> <out_dir>
  wreck3-swing1-game.gif   — масштаб игры (камера 48°, 87 px/м), голова и цепь по запечке, полкадра на кадр (60 к/с)
  wreck3-swing1-ingame.gif — то же, но голова как покажет риг при первом нажатии (со спины, сшивка AnchorBlend)
  wreck3-swing1-side.gif   — близко: сбоку справа и 3/4 спереди-слева рядом, по запечке
  wreck3-swing1-keyposes.jpg — позы листа B (рефы владельца) рядом с кадрами клипа
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

SRC, OUT = sys.argv[1], sys.argv[2]
REF = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "motion-ref-pack")
os.makedirs(OUT, exist_ok=True)
F = r"C:\Windows\Fonts\segoeui.ttf"; FB = r"C:\Windows\Fonts\segoeuib.ttf"
font, small, big = ImageFont.truetype(F, 18), ImageFont.truetype(F, 15), ImageFont.truetype(FB, 24)
MARK = {0: "снятие со спины", 1: "поза 1", 2.5: "мах", 7: "КОНТАКТ (тик 7)", 7.5: "проводка", 10: "поза 3", 12: "конец (= Wait1 0)"}


def label(im, f, text):
    d = ImageDraw.Draw(im)
    tick = "кадр %4.1f" % f
    phase = next((v for k, v in sorted(MARK.items(), reverse=True) if f >= k and k != 7), "")
    if abs(f - 7) < 0.26: phase = MARK[7]
    d.rectangle((0, 0, im.width, 30), fill=(20, 22, 22))
    d.text((10, 4), "%s  ·  %s" % (tick, phase), font=font, fill=(255, 210, 90) if abs(f - 7) < 0.26 else (235, 235, 235))
    d.text((im.width - d.textlength(text, font=small) - 10, 7), text, font=small, fill=(170, 200, 200))
    return im


def gif(names, path, text, scale=1.0):
    frames = []
    for k, n in enumerate(names):
        im = Image.open(os.path.join(SRC, n)).convert("RGB")
        if scale != 1.0: im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
        frames.append(label(im, k / 2, text))
    # 60 к/с полкадра → 20 мс (браузеры не держат меньше 20); пауза в начале и на контакте/в конце
    dur = [20] * len(frames); dur[0] = 500; dur[14] = 260; dur[-1] = 700
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=dur, loop=0, optimize=True)
    print("gif", path, len(frames))


seq = lambda tag, view: ["seq_%s_%s_%02d.png" % (tag, view, k) for k in range(25)]
gif(seq("bake", "game"), os.path.join(OUT, "wreck3-swing1-game.gif"), "камера игры 48°, 87 px/м · запечка")
gif(seq("ingame", "game"), os.path.join(OUT, "wreck3-swing1-ingame.gif"), "как в игре: 1-е нажатие, якорь со спины (шов рига)")
pairs = []
for k in range(25):
    a = Image.open(os.path.join(SRC, "seq_bake_rside_%02d.png" % k)).convert("RGB")
    b = Image.open(os.path.join(SRC, "seq_bake_fl_%02d.png" % k)).convert("RGB")
    im = Image.new("RGB", (a.width + b.width, a.height)); im.paste(a, (0, 0)); im.paste(b, (a.width, 0))
    im = im.resize((im.width * 3 // 4, im.height * 3 // 4), Image.LANCZOS)
    name = "pair_%02d.png" % k; im.save(os.path.join(SRC, name)); pairs.append(name)
gif(pairs, os.path.join(OUT, "wreck3-swing1-side.gif"), "сбоку справа | 3/4 спереди-слева · запечка")

# ---- лист ключевых поз: реф (лист B владельца) | сбоку | 3/4 | игра
chat = Image.open(os.path.join(REF, "B-key-poses-chatgpt.png")).convert("RGB")
fix1 = Image.open(os.path.join(REF, "B-key-poses-fixed-higgsfield-v1.png")).convert("RGB")
ROWS = [(0, None, "кадр 0 — снятие: рукоять со спины, якорь уже летит назад-вправо (сшивка рига со спины)"),
        (1, (fix1, (0, 92, 300, 400)), "кадр 1 ≈ поза 1: корпус скручен вправо, вес на задней правой, кисти у правого бедра"),
        (4, None, "кадр 4 — мах: таз ведёт, кисти тянут цепь поперёк тела, якорь обходит справа"),
        (7, (chat, (370, 110, 840, 500)), "кадр 7 = поза 2, КОНТАКТ: якорь впереди 2,21 м, 25,9 м/с, h 0,72 м, цепь натянута"),
        (10, (fix1, (690, 92, 960, 400)), "кадр 10 = поза 3: корпус влево, кисти у левого бедра, шаг правой вперёд"),
        (12, None, "кадр 12 — конец проводки (= Wait1 0); кадр 8 = Swing2 0")]
CW, CH, TW = 300, 300, 1200
sheet = Image.new("RGB", (CW * 4, 74 + len(ROWS) * (CH + 34)), (24, 26, 26))
d = ImageDraw.Draw(sheet)
d.text((12, 10), "Крушение v3 · Swing1: лист B и клип (FBX как в Unity, якорь и цепь по запечке)", font=big, fill=(240, 240, 240))
y = 74
for f, ref, text in ROWS:
    d.text((12, y + 6), text, font=font, fill=(255, 210, 90) if f == 7 else (225, 225, 225))
    y += 34
    if ref is not None:
        im, box = ref
        r = im.crop(box); r.thumbnail((CW, CH)); sheet.paste(r, ((CW - r.width) // 2, y + (CH - r.height) // 2))
    else:
        d.text((CW // 2 - 40, y + CH // 2 - 10), "(в листе нет)", font=small, fill=(120, 130, 130))
    for c, v in enumerate(("rside", "fl", "game")):
        im = Image.open(os.path.join(SRC, "f%04.1f_%s.png" % (f, v))).convert("RGB")
        im = im.crop((int(im.width * .08), int(im.height * .06), int(im.width * .92), int(im.height * .9))).resize((CW, CH), Image.LANCZOS)
        sheet.paste(im, (CW * (c + 1), y))
    y += CH
for c, t in enumerate(("лист B (реф)", "клип: сбоку справа", "клип: 3/4 спереди-слева", "клип: камера игры")):
    d.text((CW * c + 10, 48), t, font=small, fill=(160, 190, 190))
sheet.save(os.path.join(OUT, "wreck3-swing1-keyposes.jpg"), quality=90)
print("sheet done")
