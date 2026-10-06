"""Крушение v2: GIF форм в камере игры, 1 кадр = 1 тик (30 к/с): Девятый вал (2 оборота заряда и отпускание),
Водяной панцирь (махи Braced + Slam), Волнорез (Slam_Drag + Stow).
python wk_gif_forms.py <gif_frames_dir> <out.gif>"""
import sys, os, json
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
seq = json.load(open(os.path.join(src, "gif_seq.json"), encoding="utf-8"))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 17)
P = "Pelag_AN_Wreck2_"
HITS = {(P + "Swing1", 7): "МАХ 1", (P + "Swing2", 6): "МАХ 2", (P + "ChargeRelease", 4): "УДАР ОЗЕМЬ (заряд)",
        (P + "Swing1_Braced", 7): "МАХ 1", (P + "Swing2_Braced", 6): "МАХ 2 (поза 10)", (P + "Slam", 9): "УДАР ОЗЕМЬ",
        (P + "Slam_Drag", 9): "УДАР ОЗЕМЬ", (P + "Slam_Drag", 16): "протяжка (поза 11)", (P + "Charge", 0): "заряд: оборот"}
frames, start = [], {}
for i, s in enumerate(seq):
    start.setdefault(s["form"], i)
    im = Image.open(os.path.join(src, "gif_%03d.png" % i)).convert("RGB")
    d = ImageDraw.Draw(im)
    d.text((10, 8), "Крушение v2, форма: %s (якорь — план оси хвата)" % s["form"],
           fill=(255, 225, 70), font=bold)
    mark = HITS.get((s["clip"], s["frame"]), "")
    d.text((10, 32), "тик %02d  %s  кадр %d  %s" % (i - start[s["form"]], s["clip"].replace(P, ""), s["frame"], mark),
           fill=(245, 240, 220), font=font)
    frames.append(im.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
frames[0].save(out, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)
print(out, len(frames), os.path.getsize(out))
