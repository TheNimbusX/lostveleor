# Cut the three icons out of each base sheet (512 px), build HUD-size comparisons next to Whirlwind / Squall / "Na vylet" (Skewer).
# Usage: python make_icons.py cut  -> icons/<sheet>-<Name>-512.png
#        python make_icons.py compare -> icons/compare-base.jpg (+ compare-forms.jpg when form sheets exist)
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(ROOT, "icons")
SRC = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\UI\Abilities"
NAMES = ["Wreck", "AnchorThrow", "Abordage"]
FORMS = ["Wreck", "Breakwater", "NinthWave", "WaterShell"]
# tile boxes measured on the sheets (the tile navy is too close to the sheet navy for auto-detection)
TILES = {
    "base-icons-v1": [(43, 118, 867, 1001), (931, 118, 1767, 1001), (1830, 118, 2641, 1001)],
    "base-icons-v2": [(40, 121, 853, 988), (927, 121, 1758, 988), (1838, 121, 2641, 988)],
}

try:
    FONT = ImageFont.truetype("arial.ttf", 13)
    TFONT = ImageFont.truetype("arialbd.ttf", 16)
except Exception:
    FONT = TFONT = ImageFont.load_default()


def runs(mask, min_len):
    out, start = [], None
    for i, v in enumerate(list(mask) + [False]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            if i - start >= min_len:
                out.append((start, i))
            start = None
    return out


def panels(path, n):
    """Square-ish tiles that differ from the sheet background (corner colour)."""
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.int32)
    bg = np.median(np.concatenate([a[:20, :20].reshape(-1, 3), a[-20:, -20:].reshape(-1, 3)]), axis=0)
    diff = np.abs(a - bg).sum(axis=2) > 8
    cols = diff.mean(axis=0) > 0.6
    xs = runs(cols, a.shape[1] // (n * 3))
    boxes = []
    for x0, x1 in xs[:n] if len(xs) >= n else xs:
        rows = diff[:, x0:x1].mean(axis=1) > 0.6
        ys = runs(rows, a.shape[0] // 4)
        y0, y1 = (ys[0][0], ys[-1][1]) if ys else (0, a.shape[0])
        boxes.append((x0, y0, x1, y1))
    return boxes


def square(im, bg):
    w, h = im.size
    s = max(w, h)
    out = Image.new("RGB", (s, s), bg)
    out.paste(im, ((s - w) // 2, (s - h) // 2))
    return out


def cut_row(sheet_name, names):
    p = os.path.join(ICONS, sheet_name + ".png")
    im = Image.open(p).convert("RGB")
    bg = im.getpixel((5, 5))
    boxes = TILES.get(sheet_name) or panels(p, len(names))
    assert len(boxes) == len(names), (sheet_name, boxes)
    res = []
    for name, b in zip(names, boxes):
        ic = square(im.crop(b), bg).resize((512, 512), Image.LANCZOS)
        out = os.path.join(ICONS, "%s-%s-512.png" % (sheet_name, name))
        ic.save(out)
        res.append(out)
        print(out, b)
    return res


def cut_grid(sheet_name):
    """2x2 form sheet: plain quadrants with a small inset (tiles fill the quadrants)."""
    p = os.path.join(ICONS, sheet_name + ".png")
    im = Image.open(p).convert("RGB")
    W, H = im.size
    inset = W // 64
    res = []
    for i, name in enumerate(FORMS):
        x0, y0 = (i % 2) * W // 2, (i // 2) * H // 2
        q = im.crop((x0 + inset, y0 + inset, x0 + W // 2 - inset, y0 + H // 2 - inset))
        out = os.path.join(ICONS, "%s-%s-512.png" % (sheet_name, name))
        q.resize((512, 512), Image.LANCZOS).save(out)
        res.append(out)
        print(out)
    return res


def old(n):
    im = Image.open(os.path.join(SRC, "Icon_%s.png" % n)).convert("RGBA")
    bg = Image.new("RGBA", im.size, (6, 14, 30, 255))
    bg.alpha_composite(im)
    return bg.convert("RGB")


def sheet(rows, cols, path):
    PAD, GAP, CELL = 16, 18, 128
    W = PAD * 2 + cols * (CELL + GAP)
    H = PAD + sum(24 + s + 30 for _, _, s, _ in rows) + PAD
    out = Image.new("RGB", (W, H), (34, 36, 44))
    d = ImageDraw.Draw(out)
    y = PAD
    for title, items, s, gray in rows:
        d.text((PAD, y), title, fill=(235, 225, 190), font=TFONT)
        y += 24
        x = PAD
        for name, im in items:
            t = im.resize((s, s), Image.LANCZOS)
            if gray:
                t = ImageOps.grayscale(t).convert("RGB")
            ox = x + (CELL - s) // 2
            out.paste(t, (ox, y))
            if name.endswith("*"):
                d.rectangle([ox - 2, y - 2, ox + s + 1, y + s + 1], outline=(156, 200, 240), width=1)
            d.text((x, y + s + 3), name.rstrip("*"), fill=(225, 225, 225), font=FONT)
            x += CELL + GAP
        y += s + 30
    out.save(path, quality=92)
    print(path, out.size)


def compare():
    ref = [("Whirlwind", old("Whirlwind")), ("Squall", old("Squall")), ("Na vylet", old("Skewer"))]
    rows = []
    for v in (1, 2):
        new = [(n + " v%d*" % v, Image.open(os.path.join(ICONS, "base-icons-v%d-%s-512.png" % (v, n)))) for n in NAMES]
        rows += [("v%d: 128 px" % v, ref + new, 128, False), ("v%d: 64 px, 1:1 like the HUD" % v, ref + new, 64, False),
                 ("v%d: 64 px grayscale (silhouette)" % v, ref + new, 64, True)]
    rows.append(("for contrast: current anchor icons, 64 px", ref + [("Wreck old", old("Wreck")), ("Throw old", old("AnchorThrow")), ("Leap old", old("AnchorLeap"))], 64, False))
    sheet(rows, 6, os.path.join(ICONS, "compare-base.jpg"))

    fs = [v for v in (1, 2) if os.path.exists(os.path.join(ICONS, "forms-icons-v%d-Wreck-512.png" % v))]
    if fs:
        wf = [("Whirlwind", old("Whirlwind")), ("Foam", old("Whirlwind_FoamWaves")), ("Maelstrom", old("Whirlwind_Maelstrom")), ("Storm", old("Whirlwind_Storm"))]
        rows = [("Whirlwind forms today (same silhouette, colour = form), 64 px", wf, 64, False)]
        for v in fs:
            new = [(n + " v%d*" % v, Image.open(os.path.join(ICONS, "forms-icons-v%d-%s-512.png" % (v, n)))) for n in FORMS]
            rows += [("Wreck forms v%d: 128 px" % v, new, 128, False), ("Wreck forms v%d: 64 px" % v, new, 64, False)]
        rows.append(("Wreck forms today, 64 px", [("Wreck", old("Wreck")), ("Breakwater", old("Wreck_Breakwater")), ("Ninth", old("Wreck_NinthWave")), ("Shell", old("Wreck_Shell"))], 64, False))
        sheet(rows, 4, os.path.join(ICONS, "compare-forms.jpg"))


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "cut":
        for v in (1, 2):
            if os.path.exists(os.path.join(ICONS, "base-icons-v%d.png" % v)):
                cut_row("base-icons-v%d" % v, NAMES)
            if os.path.exists(os.path.join(ICONS, "forms-icons-v%d.png" % v)):
                cut_grid("forms-icons-v%d" % v)
    elif cmd == "compare":
        compare()
