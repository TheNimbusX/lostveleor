"""Абордаж v2: GIF трёх бросков из кадров ab_render.py gif (1 кадр = 1 тик, 30 к/с).
python ab_gif.py <gif_frames_dir> <out.gif>"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
seq = json.load(open(os.path.join(src, "gif_seq.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 17)
frames = []
for i, s in enumerate(seq):
    im = Image.open(os.path.join(src, f"gif_{i:03d}.png")).convert("RGB")
    d = ImageDraw.Draw(im)
    d.text((10, 8), "цель %.0f м: A = %d, P = %d, удар на тике %d" % (s["dist"], s["A"], s["P"], s["hit"]), fill=(255, 225, 70), font=bold)
    mark = " КОНТАКТ" if s["tick"] == s["hit"] else ""
    if any(abs(x["tick"] - round(x["tick"])) > 1e-6 for x in seq): mark += "   (кадр = ½ тика, замедление ×2)"
    d.text((10, 32), "тик %04.1f  %s f%.2f%s" % (s["tick"], s["clip"].replace("Pelag_AN_Abordage2_", ""), s["frame"], mark),
           fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
