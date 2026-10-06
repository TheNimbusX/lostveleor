import os
from PIL import Image, ImageDraw, ImageFont

SRC = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\UI\Abilities"
OUT = r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\wreck-look-2026-10-06\icons-current-sheet.jpg"

rows = [
    ("Sabre / water skills", ["Autoattack", "ChainStep", "Dash", "Whirlwind", "Squall", "Cleave", "Skewer"]),
    ("ANCHOR skills (base)", ["Wreck", "AnchorThrow", "AnchorLeap", "AnchorSweep"]),
    ("Other", ["Backblast", "Blaze", "FireFlask"]),
    ("Forms: Whirlwind / Squall", ["Whirlwind_FoamWaves", "Whirlwind_Maelstrom", "Whirlwind_Storm",
                                   "Squall_Elusive", "Squall_FoamTrail", "Squall_Hunt"]),
    ("Forms: Wreck / Throw / Abordage", ["Wreck_Breakwater", "Wreck_NinthWave", "Wreck_Shell",
                                         "AnchorThrow_Fan", "AnchorThrow_Harpoon", "AnchorThrow_Net",
                                         "AnchorLeap_Breach", "AnchorLeap_Geyser", "AnchorLeap_Quake"]),
]

CELL = 200
PAD = 14
LABEL_H = 22
ROW_TITLE = 30
cols = max(len(r[1]) for r in rows)
W = PAD + cols * (CELL + PAD)
H = PAD + sum(ROW_TITLE + CELL + LABEL_H + PAD for _ in rows)

sheet = Image.new("RGB", (W, H), (34, 36, 44))
d = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arial.ttf", 15)
    tfont = ImageFont.truetype("arialbd.ttf", 18)
except Exception:
    font = tfont = ImageFont.load_default()

y = PAD
for title, names in rows:
    d.text((PAD, y + 4), title, fill=(240, 220, 160) if "ANCHOR" in title else (220, 220, 220), font=tfont)
    y += ROW_TITLE
    x = PAD
    for n in names:
        p = os.path.join(SRC, "Icon_%s.png" % n)
        im = Image.open(p).convert("RGBA")
        im.thumbnail((CELL, CELL), Image.LANCZOS)
        bg = Image.new("RGBA", (CELL, CELL), (24, 26, 32, 255))
        bg.alpha_composite(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
        sheet.paste(bg.convert("RGB"), (x, y))
        if "ANCHOR" in title or n.startswith(("Wreck", "Anchor")):
            d.rectangle([x - 2, y - 2, x + CELL + 1, y + CELL + 1], outline=(240, 200, 90), width=2)
        d.text((x + 2, y + CELL + 3), n, fill=(230, 230, 230), font=font)
        x += CELL + PAD
    y += CELL + LABEL_H + PAD

sheet.save(OUT, quality=90)
print(OUT, sheet.size)
