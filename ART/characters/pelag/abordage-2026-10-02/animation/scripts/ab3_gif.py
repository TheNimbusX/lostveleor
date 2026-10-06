"""Абордаж v3: GIF трёх кастов из кадров ab3_render.py gif (кадр = ½ тика, 33 мс — замедление ×2).
python ab3_gif.py <gif_frames_dir> <out.gif>"""
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
    d.text((10, 8), "%s — цель %.1f м: замах %d, A = %d, P = %d (%s), удар на тике %d" % (s["form"], s["dist"], s["W"], s["A"], s["P"], s["pull"], s["hit"]),
           fill=(255, 225, 70), font=bold)
    mark = ""
    if s["tick"] == s["hit"]: mark = "  КОНТАКТ"
    elif s["tick"] == s["W"]: mark = "  ВЫПУСК"
    elif s["clip"].endswith("Throw") and 0 < s["tick"] < s["W"]: mark = "  замах через плечо"
    d.text((10, 32), "тик %04.1f  %s f%.2f%s   (кадр = ½ тика, ×2 медленнее)" % (s["tick"], s["clip"].replace("Pelag_AN_Abordage2_", ""), s["frame"], mark),
           fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
