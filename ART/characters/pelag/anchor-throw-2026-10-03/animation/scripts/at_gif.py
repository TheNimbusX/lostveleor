"""Бросок якоря: GIF трёх бросков из кадров at_render.py gif (1 кадр = 1 тик, 30 к/с).
python at_gif.py <gif_frames_dir> <out.gif>"""
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
    head = "%.0f м%s: F = %d, R = %d, натяг на тике %d, ловля %d" % (s["reach"], " (стена)" if s["reach"] < 7 else "", s["F"], s["R"], s["T"], s["catch"])
    d.text((10, 8), head, fill=(255, 225, 70), font=bold)
    mark = " ВЫПУСК" if s["tick"] == 2 else " НАТЯГ" if s["tick"] == s["T"] else " ЛОВЛЯ" if s["tick"] == s["catch"] else " ПОПАДАНИЕ" if s["hit"] else ""
    d.text((10, 32), "тик %02d  %s f%.2f%s" % (s["tick"], s["clip"].replace("Pelag_AN_AnchorThrow_", ""), s["frame"], mark),
           fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
