"""System-python contact sheet: sheet.py <out.png> <cols> <img1> [img2 ...] [--legend legend.txt] [--scale 0.5]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

args = sys.argv[1:]
legend = None
scale = 0.5
if "--legend" in args:
    i = args.index("--legend"); legend = args[i + 1]; del args[i:i + 2]
if "--scale" in args:
    i = args.index("--scale"); scale = float(args[i + 1]); del args[i:i + 2]
out, cols, files = args[0], int(args[1]), args[2:]
ims = [Image.open(f).convert("RGB") for f in files]
ims = [im.resize((int(im.width * scale), int(im.height * scale))) for im in ims]
w = max(im.width for im in ims); h = max(im.height for im in ims)
rows = (len(ims) + cols - 1) // cols
lw = 230 if legend else 0
sheet = Image.new("RGB", (w * cols + lw, h * rows), (30, 30, 30))
dr = ImageDraw.Draw(sheet)
for k, (im, f) in enumerate(zip(ims, files)):
    x, y = (k % cols) * w, (k // cols) * h
    sheet.paste(im, (x, y))
    dr.text((x + 4, y + 4), Path(f).stem, fill=(255, 255, 255))
if legend:
    y = 6
    for line in Path(legend).read_text().splitlines():
        name, rest = line.split(" ", 1)
        c = tuple(int(float(v) * 255) for v in rest.strip("()").split(","))
        dr.rectangle([w * cols + 6, y, w * cols + 24, y + 14], fill=c)
        dr.text((w * cols + 30, y + 1), name, fill=(255, 255, 255))
        y += 20
sheet.save(out)
print("SHEET", out, sheet.size)
