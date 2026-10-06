import os
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\wreck-look-2026-10-06"
SRC = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\UI\Abilities"
ICONS = os.path.join(ROOT, "icons")

# panel boxes in original pixels (x0, y0, x1, y1), measured on the 2688x1152 sheets
BOXES = {
    1: [(81, 110, 867, 1017), (957, 110, 1745, 1017), (1836, 110, 2623, 1017)],
    2: [(44, 141, 867, 995), (937, 141, 1757, 995), (1828, 141, 2645, 995)],
}
NAMES = ["Wreck", "AnchorThrow", "Abordage"]
BG = (2, 19, 37)

try:
    font = ImageFont.truetype("arial.ttf", 13)
    tfont = ImageFont.truetype("arialbd.ttf", 16)
except Exception:
    font = tfont = ImageFont.load_default()


def square(im):
    w, h = im.size
    s = max(w, h)
    out = Image.new("RGB", (s, s), BG)
    out.paste(im, ((s - w) // 2, (s - h) // 2))
    return out


def old(n):
    return Image.open(os.path.join(SRC, "Icon_%s.png" % n)).convert("RGB")


for v, boxes in BOXES.items():
    sheet = Image.open(os.path.join(ICONS, "silhouettes-v%d.png" % v)).convert("RGB")
    new = []
    for name, b in zip(NAMES, boxes):
        ic = square(sheet.crop(b)).resize((512, 512), Image.LANCZOS)
        ic.save(os.path.join(ICONS, "v%d-%s-512.png" % (v, name)))
        new.append((name + " NEW", ic))
    ref = [("Whirlwind", old("Whirlwind")), ("Squall", old("Squall"))]
    olds = [("Wreck old", old("Wreck")), ("AnchorThrow old", old("AnchorThrow")), ("AnchorLeap old", old("AnchorLeap"))]

    rows = [
        ("128 px: Whirlwind, Squall + new anchor base icons (v%d)" % v, ref + new, 128, False),
        ("64 px, 1:1 like the HUD", ref + new, 64, False),
        ("64 px grayscale: silhouette check", ref + new, 64, True),
        ("64 px, for contrast: current anchor icons", ref + olds, 64, False),
    ]
    PAD, GAP = 16, 18
    W = PAD * 2 + 5 * (128 + GAP)
    H = PAD
    for _, _, s, _ in rows:
        H += 24 + s + 20 + 10
    out = Image.new("RGB", (W, H + PAD), (34, 36, 44))
    d = ImageDraw.Draw(out)
    y = PAD
    for title, items, s, gray in rows:
        d.text((PAD, y), title, fill=(235, 225, 190), font=tfont)
        y += 24
        x = PAD
        for name, im in items:
            t = im.resize((s, s), Image.LANCZOS)
            if gray:
                t = ImageOps.grayscale(t).convert("RGB")
            out.paste(t, (x + (128 - s) // 2, y))
            if "NEW" in name:
                d.rectangle([x + (128 - s) // 2 - 2, y - 2, x + (128 - s) // 2 + s + 1, y + s + 1], outline=(240, 200, 90), width=1)
            d.text((x, y + s + 3), name, fill=(225, 225, 225), font=font)
            x += 128 + GAP
        y += s + 20 + 10
    p = os.path.join(ICONS, "compare-v%d.jpg" % v)
    out.save(p, quality=92)
    print(p, out.size)
