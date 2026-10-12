"""Labelled board of the Anchor Throw forms (07.10): approved base first, then every form variant."""
import glob
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\anchor-throw-2026-10-07"
RES = os.path.join(ROOT, "chatgpt-results")
GHOST = r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\wreck-look-2026-10-06\chatgpt-results\v4-form-ghost-anchor.png"

TILES = [
    ("base-hit-v1.png", "База — одобрена (для сравнения)", "кобальт, якорь бьёт всех на линии 7 м"),
    ("form-net-v1.png", "Невод v1 — сеть-ковёр", "морская зелень: плоская сеть из цепей гребёт полосу 3 м"),
    ("form-net-v2.png", "Невод v2 — сеть-черпак", "морская зелень: дуга-трал из цепей сгребает пачку"),
    ("form-fan-iron-v1.png", "Веер А v1 — железные, удар", "индиго: три настоящих якоря веером, звёзды на целях"),
    ("form-fan-iron-v2.png", "Веер А v2 — железные, рывок", "индиго: три цепи тянут, борозды сходятся к Пелагу"),
    ("form-fan-ghost-v1.png", "Веер Б — боковые призрачные", "индиго: по бокам светлые прозрачные якоря"),
    ("form-harpoon-v1.png", "Гарпун v1 — укус", "маджента: якорь вгрызся в элиту, дальше не летит"),
    ("form-harpoon-v2.png", "Гарпун v2 — подтяг", "маджента: элиту волочит к Пелагу, борозда, оглушение"),
]
tiles = [(os.path.join(RES, f), n, s_) for f, n, s_ in TILES if os.path.exists(os.path.join(RES, f))]
if os.path.exists(GHOST):
    tiles.append((GHOST, "Сравнить: Призрачный якорь Крушения", "уже принятая форма Крушения — сравнить с Веером Б над ней"))

TW, TH = 960, 540
LABEL = 64
GAP = 14
COLS = 3
rows = (len(tiles) + COLS - 1) // COLS
TITLE = 80
W = COLS * TW + (COLS + 1) * GAP
H = TITLE + rows * (TH + LABEL) + (rows + 1) * GAP

board = Image.new("RGB", (W, H), (18, 18, 22))
d = ImageDraw.Draw(board)
fb = ImageFont.truetype("arialbd.ttf", 30)
fs = ImageFont.truetype("arial.ttf", 22)
ft = ImageFont.truetype("arialbd.ttf", 40)
d.text((GAP, 18), "Бросок якоря — формы в железном стиле (07.10, ChatGPT)", fill=(240, 240, 240), font=ft)

for i, (path, name, sub) in enumerate(tiles):
    r, c = divmod(i, COLS)
    x = GAP + c * (TW + GAP)
    y = TITLE + GAP + r * (TH + LABEL + GAP)
    im = Image.open(path).convert("RGB")
    sc = max(TW / im.width, TH / im.height)
    im = im.resize((round(im.width * sc), round(im.height * sc)), Image.LANCZOS)
    ox, oy = (im.width - TW) // 2, (im.height - TH) // 2
    board.paste(im.crop((ox, oy, ox + TW, oy + TH)), (x, y))
    d.rectangle((x, y + TH, x + TW, y + TH + LABEL), fill=(30, 30, 36))
    d.text((x + 14, y + TH + 6), name, fill=(255, 255, 255), font=fb)
    d.text((x + 14, y + TH + 38), sub, fill=(185, 185, 195), font=fs)

out = os.path.join(ROOT, "board-throw-forms.jpg")
board.save(out, quality=90)
print(out, board.size, len(tiles))
