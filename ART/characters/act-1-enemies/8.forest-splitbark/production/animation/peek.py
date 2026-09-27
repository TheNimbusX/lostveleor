"""Quick contact sheet for inspection: python peek.py <frames_dir> <out.jpg> f1 f2 ... (system Python + PIL)."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

src, out = Path(sys.argv[1]), Path(sys.argv[2])
frames = [int(a) for a in sys.argv[3:]]
tile = 240
sheet = Image.new("RGB", (tile * min(len(frames), 6), tile * ((len(frames) + 5) // 6)), "#222")
d = ImageDraw.Draw(sheet)
for i, f in enumerate(frames):
    im = Image.open(src / f"{f:04d}.png").convert("RGB").resize((tile, tile))
    x, y = (i % 6) * tile, (i // 6) * tile
    sheet.paste(im, (x, y))
    d.text((x + 6, y + 6), f"f{f}", fill="yellow")
sheet.save(out, quality=88)
