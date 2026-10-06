"""Крушение v3, формы (06.10): последовательности кадров (шаг полкадра = 60 к/с, без стоп-кадров) и видео/листы.

python v3s5_media.py braced <anim_dir> <out seq.json>  — «Якорная броня» при самых быстрых нажатиях: Swing1_Braced 0→7,5 →
                                                        Swing2_Braced 0→14,5 → Slam_Braced 0→24 (головы — запечки *_Braced_w*)
python v3s5_media.py drag <anim_dir> <out seq.json>    — «Волнорез»: Slam_Drag 0→25 (голова — запечка Slam_Drag_w13)
python v3s5_media.py cmp <anim_dir> <out seq.json>     — пары «база | броня» ключевых кадров (для листа сравнения)
python v3s5_media.py make <frames_dir> <media_dir> <tag> <views> <seq.json>  — MP4 1× (×3 подряд) и 0,5× (×2), GIF
python v3s5_media.py sheet <frames_dir> <media_dir> <tag> <views> <seq.json> <cols> <title> — лист поз (подписи из seq)
"""
import json, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw, ImageFont
import v3s2_media as M

P = "Pelag_AN_Wreck2_"
VIEW = {"game": "камера игры 48°", "frw": "3/4 спереди-справа", "rside": "сбоку (справа)", "fl": "3/4 спереди-слева"}


def bake(anim, name):
    return json.load(open(os.path.join(anim, "bake", name + ".anchorbake.json"), encoding="utf-8"))["samples"]


def near(s, t):
    return min(s, key=lambda x: abs(x["t"] - t))


def fr(clip, t, s, lab):
    return dict(clip=P + clip, frame=t, p=s["p"], q=s["q"], label=lab)


def braced(anim, out):
    s1, s2, sl = bake(anim, P + "Swing1_Braced_w7"), bake(anim, P + "Swing2_Braced_w14"), bake(anim, P + "Slam_Braced_w13")
    F = []
    for k in range(16): t = k / 2; F.append(fr("Swing1_Braced", t, near(s1, t), "Броня · Swing1 к%04.1f · тик %04.1f%s" % (t, t, "  КОНТАКТ 1" if abs(t - 7) < .26 else "")))
    for k in range(30): t = k / 2; F.append(fr("Swing2_Braced", t, near(s2, t), "Броня · Swing2 к%04.1f · тик %04.1f%s" % (t, 8 + t, "  КОНТАКТ 2" if abs(t - 14) < .26 else "")))
    for k in range(49):
        t = k / 2; lab = "  УДАР ОЗЕМЬ" if abs(t - 13) < .26 else "  над головой" if abs(t - 7) < .26 else ""
        F.append(fr("Slam_Braced", t, near(sl, t), "Броня · Slam к%04.1f · тик %04.1f%s" % (t, 23 + t, lab)))
    json.dump(dict(frames=F, seamchecks=[[P + "Swing1_Braced", 8, P + "Swing2_Braced", 0], [P + "Swing2_Braced", 15, P + "Slam_Braced", 0]]),
              open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(F))


def drag(anim, out):
    sd = bake(anim, P + "Slam_Drag_w13")
    F = []
    for k in range(51):
        t = k / 2
        lab = "  УДАР ОЗЕМЬ" if abs(t - 13) < .26 else "  над головой" if abs(t - 7) < .26 else "  протяжка (поза 11)" if 17 <= t <= 21 else \
              "  отпуск Sim (ходьба)" if abs(t - 23) < .26 else ""
        F.append(fr("Slam_Drag", t, near(sd, t), "Волнорез · Slam_Drag к%04.1f · тик %04.1f%s" % (t, 23 + t, lab)))
    json.dump(dict(frames=F), open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(F))


def cmp(anim, out):
    F = []
    for clip, w, frames in (("Swing1", "w7", (0, 4, 7, 12)), ("Swing2", "w14", (5, 14, 17)), ("Slam", "w13", (5, 13, 16))):
        for f in frames:
            for suf, name in (("", "база"), ("_Braced", "броня")):
                F.append(fr(clip + suf, f, near(bake(anim, P + clip + suf + "_" + w), f), "%s к%d — %s" % (clip, f, name)))
    json.dump(dict(frames=F), open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(F))


def make(src, dst, tag, views, seqf):
    seq = json.load(open(seqf, encoding="utf-8"))["frames"]
    os.makedirs(dst, exist_ok=True)
    tmp = os.path.join(src, "_lab"); os.makedirs(tmp, exist_ok=True)
    for v in views:
        names = sorted(n for n in os.listdir(src) if n.startswith("%s_%s_" % (tag, v)) and n.endswith(".png"))
        for speed, step, note in (("1x", 2, "реальное время, 30 к/с"), ("0.5x", 1, "0,5× (30 к/с, полкадра на кадр)")):
            frames = []
            for i, n in enumerate(names):
                if i % step: continue
                im = M.label(Image.open(os.path.join(src, n)).convert("RGB"), seq[i]["label"], VIEW.get(v, v) + " · " + note)
                frames.append(im)
            reps = 3 if speed == "1x" else 2
            for i, im in enumerate(frames * reps): im.save(os.path.join(tmp, "%s_%s_%s_%03d.png" % (tag, v, speed, i)))
            base = os.path.join(dst, "%s-%s-%s" % (tag, v, speed))
            subprocess.run([M.ffmpeg(), "-y", "-loglevel", "error", "-framerate", "30", "-i", os.path.join(tmp, "%s_%s_%s_%%03d.png" % (tag, v, speed)),
                            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", base + ".mp4"], check=True)
            small = [f.resize((f.width * 2 // 3, f.height * 2 // 3), Image.LANCZOS) for f in frames]
            small[0].save(base + ".gif", save_all=True, append_images=small[1:], duration=33, loop=0, optimize=True)
            for n_ in os.listdir(tmp):
                if n_.startswith("%s_%s_%s_" % (tag, v, speed)): os.remove(os.path.join(tmp, n_))
            print("media", base, len(frames), "frames")


def sheet(src, dst, tag, views, seqf, cols, title):
    seq = json.load(open(seqf, encoding="utf-8"))["frames"]
    cell, lab_h = 330, 42
    f1, f2 = ImageFont.truetype(M.F, 14), ImageFont.truetype(M.F, 12)
    tiles = []
    for i, s in enumerate(seq):
        for v in views:
            im = Image.open(os.path.join(src, "%s_%s_%03d.png" % (tag, v, i))).convert("RGB")
            im = im.crop((int(im.width * .18), int(im.height * .08), int(im.width * .82), int(im.height * .9)))
            im.thumbnail((cell, cell))
            lab = s["label"].split(" · ", 1)[-1] if " · " in s["label"] else s["label"]
            tiles.append((im, lab, VIEW.get(v, v) if len(views) > 1 else ""))
    rows = (len(tiles) + cols - 1) // cols
    out = Image.new("RGB", (cols * cell, 56 + rows * (cell + lab_h)), (24, 26, 26)); d = ImageDraw.Draw(out)
    d.text((10, 12), title, font=ImageFont.truetype(M.FB, 20), fill=(240, 240, 240))
    for k, (im, lab, view) in enumerate(tiles):
        x, y = (k % cols) * cell, 56 + (k // cols) * (cell + lab_h)
        while d.textlength(lab, font=f1) > cell - 12: lab = lab[:-2]
        d.text((x + 6, y + 2), lab, font=f1, fill=(255, 220, 120) if "броня" in lab or "УДАР" in lab or "поза" in lab else (225, 225, 225))
        if view: d.text((x + 6, y + 21), view, font=f2, fill=(160, 190, 190))
        out.paste(im, (x + (cell - im.width) // 2, y + lab_h))
    out.save(os.path.join(dst, "%s-keyposes.jpg" % tag), quality=90)
    print("sheet", os.path.join(dst, "%s-keyposes.jpg" % tag))


if __name__ == "__main__":
    m, a = sys.argv[1], sys.argv[2:]
    if m in ("braced", "drag", "cmp"): globals()[m](a[0], a[1])
    elif m == "make": make(a[0], a[1], a[2], a[3].split(","), a[4])
    elif m == "sheet": sheet(a[0], a[1], a[2], a[3].split(","), a[4], int(a[5]), a[6])
