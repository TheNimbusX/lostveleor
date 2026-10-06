"""Крушение v2: GIF серии в самом быстром темпе (Swing1 → Swing2 → Slam → Stow), камера игры, 1 кадр = 1 тик (30 к/с).
python wk_gif.py <gif_frames_dir> <out.gif>"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
seq = json.load(open(os.path.join(src, "gif_seq.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 17)
HITS = {("Pelag_AN_Wreck2_Swing1", 7): "МАХ 1", ("Pelag_AN_Wreck2_Swing2", 6): "МАХ 2 (восьмёрка)",
        ("Pelag_AN_Wreck2_Slam", 9): "УДАР ОЗЕМЬ"}
frames = []
for i, s in enumerate(seq):
    im = Image.open(os.path.join(src, "gif_%03d.png" % i)).convert("RGB")
    d = ImageDraw.Draw(im)
    d.text((10, 8), "Крушение v2: удары 7 / 14 / 24 тика, якорь — план оси хвата (путь головы даст запечка)", fill=(255, 225, 70), font=bold)
    mark = HITS.get((s["clip"], s["frame"]), "")
    d.text((10, 32), "тик %02d  %s  кадр %d  %s" % (i - 3, s["clip"].replace("Pelag_AN_Wreck2_", ""), s["frame"], mark),
           fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
