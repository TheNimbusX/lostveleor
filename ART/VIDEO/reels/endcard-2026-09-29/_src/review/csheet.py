import sys, os, glob
from PIL import Image, ImageDraw, ImageFont
# usage: csheet.py <dir> <out.jpg> <cols> <w> <step_seconds> [start_idx] [count]
d, out, cols, w, step = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), float(sys.argv[5])
start = int(sys.argv[6]) if len(sys.argv) > 6 else 0
count = int(sys.argv[7]) if len(sys.argv) > 7 else 999
files = sorted(glob.glob(os.path.join(d, '*.png')))[start:start + count]
im0 = Image.open(files[0])
h = int(round(w * im0.height / im0.width))
rows = (len(files) + cols - 1) // cols
lab = 22
sheet = Image.new('RGB', (cols * (w + 4) + 4, rows * (h + lab + 4) + 4), (40, 40, 40))
dr = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype('arial.ttf', 16)
except Exception:
    font = ImageFont.load_default()
for i, f in enumerate(files):
    im = Image.open(f).convert('RGB').resize((w, h), Image.LANCZOS)
    x = 4 + (i % cols) * (w + 4)
    y = 4 + (i // cols) * (h + lab + 4)
    sheet.paste(im, (x, y + lab))
    dr.text((x + 2, y + 2), '%.2fs' % ((start + i) * step), fill=(255, 255, 0), font=font)
sheet.save(out, quality=90)
print(out, sheet.size)
