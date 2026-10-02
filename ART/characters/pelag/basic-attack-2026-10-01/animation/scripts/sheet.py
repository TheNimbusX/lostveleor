import sys, glob, os
from PIL import Image, ImageDraw, ImageFont
folder, prefix, out, cols = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
files = sorted(glob.glob(os.path.join(folder, prefix + "_*.png")))
w, h = Image.open(files[0]).size
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), (40, 40, 40))
d = ImageDraw.Draw(sheet)
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 22)
for i, f in enumerate(files):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(Image.open(f).convert("RGB"), (x, y))
    d.text((x + 6, y + 4), os.path.basename(f).split("_")[-1][:-4], fill=(255, 255, 0), font=font)
sheet.save(out, quality=88)
print(len(files), sheet.size)
