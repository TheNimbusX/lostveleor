"""Board of all Anchor Throw variants (07.10): pose sheets on top, HIT row, YANK row, labelled."""
import glob
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, "chatgpt-results")
OUT = os.path.join(HERE, "board-throw-base.jpg")

NOTES = {
    "pose-sheet-v1": "pose sheet v1 - 7 poses, side + 3/4 row",
    "pose-sheet-v2": "pose sheet v2 - real 3/4 row, snappier",
    "base-hit-v1": "HIT v1 - blue streak + rocks (line too long)",
    "base-hit-v2": "HIT v2 - light rim, 2 stars, scale ok",
    "base-hit-v3": "HIT v3 - heavy blade (too long, a bit electric)",
    "base-yank-v1": "YANK v1 - one furrow, both on the anchor",
    "base-yank-v2": "YANK v2 - two furrows, chevrons to Pelag",
    "base-yank-v3": "YANK v3",
}


def font(size):
    for f in ("arialbd.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def row(names, cell_w, gap=16):
    ims = [Image.open(os.path.join(RES, n + ".png")).convert("RGB") for n in names]
    ims = [im.resize((cell_w, round(im.height * cell_w / im.width)), Image.LANCZOS) for im in ims]
    h = max(im.height for im in ims)
    lab = 46
    canvas = Image.new("RGB", (len(ims) * cell_w + (len(ims) - 1) * gap, h + lab), (18, 18, 20))
    d = ImageDraw.Draw(canvas)
    for i, (n, im) in enumerate(zip(names, ims)):
        x = i * (cell_w + gap)
        d.text((x + 6, 8), NOTES.get(n, n), fill=(235, 235, 235), font=font(26))
        canvas.paste(im, (x, lab))
    return canvas


def present(prefix):
    return sorted(os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(RES, prefix + "*.png")))


W = 3 * 1100 + 2 * 16
rows = []
poses = present("pose-sheet-v")
rows.append(row(poses, (W - (len(poses) - 1) * 16) // len(poses)))
rows.append(row(present("base-hit-v"), 1100))
yank = present("base-yank-v")
rows.append(row(yank, 1100))

title_h = 70
H = title_h + sum(r.height for r in rows) + 24 * len(rows)
board = Image.new("RGB", (W + 40, H + 20), (10, 10, 12))
d = ImageDraw.Draw(board)
d.text((20, 18), "Anchor Throw (Brosok yakorya) - refs 07.10, ChatGPT: pose sheet, HIT, YANK", fill=(255, 255, 255), font=font(36))
y = title_h
for r in rows:
    board.paste(r, (20, y))
    y += r.height + 24
board.save(OUT, quality=88)
print(OUT, board.size)
