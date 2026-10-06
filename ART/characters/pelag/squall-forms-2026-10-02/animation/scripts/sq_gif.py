"""Шквал v2: GIF серии из кадров sq_render.py gif (1 кадр = 1 тик, 30 к/с). python sq_gif.py <gif_frames_dir> <out.gif>"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
seq = json.load(open(os.path.join(src, "gif_seq.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
frames = []
for i, s in enumerate(seq):
    im = Image.open(os.path.join(src, f"gif_{i:03d}.png")).convert("RGB")
    d = ImageDraw.Draw(im)
    d.text((10, 8), "тик %02d  %s f%.1f" % (i, s["clip"].replace("Pelag_AN_Squall2_", ""), s["frame"]), fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
