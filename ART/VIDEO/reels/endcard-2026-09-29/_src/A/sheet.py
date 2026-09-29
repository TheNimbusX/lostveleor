import sys, subprocess, numpy as np, os
from PIL import Image, ImageDraw, ImageFont
ff = open('ffpath.txt').read().strip()
src = sys.argv[1]; out = sys.argv[2]
W, H = 1080, 1920
raw = subprocess.run([ff, '-v', 'error', '-i', src, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True).stdout
n = len(raw) // (W * H * 3)
fr = np.frombuffer(raw, np.uint8)[:n * W * H * 3].reshape(n, H, W, 3)
times = [i * 0.25 for i in range(20)] + [(n - 1) / 30]
tw, th = 270, 480
cols = 7; rows = (len(times) + cols - 1) // cols
sheet = Image.new('RGB', (cols * (tw + 8) + 8, rows * (th + 30) + 8), (34, 34, 38))
dr = ImageDraw.Draw(sheet)
font = ImageFont.truetype('Nunito-Bold.ttf', 18)
for k, t in enumerate(times):
    i = min(int(round(t * 30)), n - 1)
    im = Image.fromarray(fr[i]).resize((tw, th), Image.LANCZOS)
    x = 8 + (k % cols) * (tw + 8); y = 8 + (k // cols) * (th + 30)
    sheet.paste(im, (x, y))
    dr.text((x + 4, y + th + 4), f'{t:.2f}s  f{i}', fill=(220, 220, 220), font=font)
sheet.save(out, quality=90)
print(n, 'frames', sheet.size)
