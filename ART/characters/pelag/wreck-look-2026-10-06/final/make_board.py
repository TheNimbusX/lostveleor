# Review helpers for the final frames:
#  frames/zones/<name>-zones.jpg  - the frame with the game damage zone (from its guide) outlined, to check the scale;
#  board-frames.jpg               - all frames on one sheet with labels.
import json, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
jobs = [j for j in json.load(open(os.path.join(ROOT, "prompts.json")))["jobs"] if j["file"].startswith("frames/")]
os.makedirs(os.path.join(ROOT, "frames", "zones"), exist_ok=True)
try:
    FONT = ImageFont.truetype("arialbd.ttf", 18)
except Exception:
    FONT = ImageFont.load_default()


def zone_edge(guide_path, size):
    g = np.asarray(Image.open(guide_path).convert("RGB")).astype(int)
    m = ((g[..., 0] - g[..., 1] > 90) & (g[..., 2] - g[..., 1] > 90)).astype(np.uint8) * 255
    m = Image.fromarray(m).resize(size, Image.NEAREST).filter(ImageFilter.MaxFilter(3))
    return m.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v else 0).filter(ImageFilter.MaxFilter(3))


tiles = []
for j in jobs:
    f = os.path.join(ROOT, j["file"])
    if not os.path.exists(f):
        continue
    im = Image.open(f).convert("RGB")
    edge = zone_edge(os.path.join(ROOT, "guides", j["refs"][0]), im.size)
    ov = im.copy()
    ov.paste(Image.new("RGB", im.size, (255, 0, 255)), (0, 0), edge)
    ov.save(os.path.join(ROOT, "frames", "zones", j["name"] + "-zones.jpg"), quality=90)
    tiles.append((j["name"], im))

W = 672
H = int(W * 752 / 1344)
cols = 2
rows = (len(tiles) + 1) // 2
board = Image.new("RGB", (cols * W + 3 * 12, rows * (H + 30) + 12), (28, 30, 38))
d = ImageDraw.Draw(board)
for i, (name, im) in enumerate(tiles):
    x = 12 + (i % cols) * (W + 12)
    y = 12 + (i // cols) * (H + 30)
    board.paste(im.resize((W, H), Image.LANCZOS), (x, y))
    d.text((x, y + H + 4), name, fill=(235, 225, 190), font=FONT)
board.save(os.path.join(ROOT, "board-frames.jpg"), quality=90)
print(len(tiles), board.size)
