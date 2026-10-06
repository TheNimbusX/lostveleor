"""Крушение v3: видео для владельца без стоп-кадров — реальное время 30 к/с и то же в 0,5×, камера игры и 3/4 сбоку.

python v3s2_media.py seq <anim_dir> <out seq.json>        — последовательность Swing1 0→12 → Wait1 ×2 (шаг полкадра):
     голова — запечка Swing1 (bake/…_w7.anchorbake.json), потом живой маятник под Wait1 (bake/…Wait1.live.json)
python v3s2_media.py make <frames_dir> <media_dir> <tag> <views> <seq.json> — MP4 (H.264; 1× трижды, 0,5× дважды подряд) и GIF
python v3s2_media.py sheet <frames_dir> <media_dir> <tag> <views> <idx,...> <labels|...>   — лист ключевых поз
"""
import json, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

F = r"C:\Windows\Fonts\segoeui.ttf"; FB = r"C:\Windows\Fonts\segoeuib.ttf"


def sample(samples, t, key="t"):
    """Сэмпл с ближайшим t (запечка — 4 на кадр, живой прогон — 4 на кадр)."""
    return min(samples, key=lambda s: abs(s[key] - t))


def build_seq(anim, out):
    b = os.path.join(anim, "bake")
    s1 = json.load(open(os.path.join(b, "Pelag_AN_Wreck2_Swing1_w7.anchorbake.json")))["samples"]
    live = json.load(open(os.path.join(b, "Pelag_AN_Wreck2_Wait1.live.json")))["samples"]
    frames = []
    for k in range(0, 2 * (12 + 24) + 1):
        t = k / 2
        if t <= 12:
            s = sample(s1, t); frames.append(dict(clip="Pelag_AN_Wreck2_Swing1", frame=t, p=s["p"], q=s["q"], label="Swing1 к%04.1f" % t))
        else:
            w = t - 12; s = sample(live, w)
            frames.append(dict(clip="Pelag_AN_Wreck2_Wait1", frame=w % 12 if w % 12 or w == 0 else 12, p=s["p"], q=s["q"],
                               label="Wait1 к%04.1f (петля %d), якорь живой" % (w % 12 if w % 12 else 12, int((w - 1e-6) // 12) + 1)))
    seq = dict(frames=frames, seamchecks=[["Pelag_AN_Wreck2_Swing1", 12, "Pelag_AN_Wreck2_Wait1", 0],
                                         ["Pelag_AN_Wreck2_Wait1", 12, "Pelag_AN_Wreck2_Wait1", 0]])
    json.dump(seq, open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames))


def ffmpeg():
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def label(im, top, right):
    d = ImageDraw.Draw(im)
    f, s = ImageFont.truetype(F, 17), ImageFont.truetype(F, 14)
    d.rectangle((0, 0, im.width, 26), fill=(20, 22, 22))
    d.text((8, 3), top, font=f, fill=(255, 210, 90) if "контакт" in top else (235, 235, 235))
    d.text((im.width - d.textlength(right, font=s) - 8, 5), right, font=s, fill=(170, 200, 200))
    return im


def make(src, dst, tag, views, seqf):
    seq = json.load(open(seqf, encoding="utf-8"))["frames"]
    os.makedirs(dst, exist_ok=True)
    tmp = os.path.join(src, "_lab"); os.makedirs(tmp, exist_ok=True)
    for v in views:
        names = sorted(n for n in os.listdir(src) if n.startswith("%s_%s_" % (tag, v)) and n.endswith(".png"))
        for speed, step, note in (("1x", 2, "реальное время, 30 к/с"), ("0.5x", 1, "0,5× (30 к/с, полкадра на кадр)")):
            pick = [n for i, n in enumerate(names) if i % step == 0]
            frames = []
            for i, n in enumerate(pick):
                im = Image.open(os.path.join(src, n)).convert("RGB")
                k = names.index(n); top = seq[k]["label"]
                if seq[k]["clip"].endswith("Swing1") and abs(seq[k]["frame"] - 7) < 0.26: top += "  КОНТАКТ"
                im = label(im, top, ("камера игры 48°" if v == "game" else "3/4 спереди-справа") + " · " + note)
                frames.append(im)
            reps = 3 if speed == "1x" else 2          # MP4 — тот же прогон подряд (плееры не зацикливают), GIF зацикливается сам
            for i, im in enumerate(frames * reps): im.save(os.path.join(tmp, "%s_%s_%s_%03d.png" % (tag, v, speed, i)))
            base = os.path.join(dst, "%s-%s-%s" % (tag, v, speed))
            subprocess.run([ffmpeg(), "-y", "-loglevel", "error", "-framerate", "30", "-i", os.path.join(tmp, "%s_%s_%s_%%03d.png" % (tag, v, speed)),
                            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", base + ".mp4"], check=True)
            small = [f.resize((f.width * 2 // 3, f.height * 2 // 3), Image.LANCZOS) for f in frames]
            small[0].save(base + ".gif", save_all=True, append_images=small[1:], duration=33, loop=0, optimize=True)
            print("media", base, len(frames), "frames")


def sheet(src, dst, tag, views, idx, labels):
    seq_h = 300
    cols = len(views)
    ims = []
    for i in idx:
        row = []
        for v in views:
            im = Image.open(os.path.join(src, "%s_%s_%03d.png" % (tag, v, i))).convert("RGB")
            im = im.crop((int(im.width * .1), int(im.height * .04), int(im.width * .9), int(im.height * .92)))
            im.thumbnail((420, seq_h)); row.append(im)
        ims.append(row)
    W = 420 * cols; Hh = 60 + len(idx) * (seq_h + 30)
    out = Image.new("RGB", (W, Hh), (24, 26, 26)); d = ImageDraw.Draw(out)
    d.text((10, 12), globals().get("ImageDraw_title") or "Крушение v3 · Wait1 (петля окна): ключевые позы, якорь — живой маятник", font=ImageFont.truetype(FB, 22), fill=(240, 240, 240))
    y = 60
    for row, lab in zip(ims, labels):
        d.text((10, y + 4), lab, font=ImageFont.truetype(F, 17), fill=(225, 225, 225)); y += 30
        for c, im in enumerate(row): out.paste(im, (420 * c + (420 - im.width) // 2, y))
        y += seq_h
    out.save(os.path.join(dst, "%s-keyposes.jpg" % tag), quality=90)
    print("sheet done")


if __name__ == "__main__":
    mode = sys.argv[1]
    if mode == "seq": build_seq(sys.argv[2], sys.argv[3])
    elif mode == "make": make(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), sys.argv[6])
    elif mode == "sheet": sheet(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), [int(x) for x in sys.argv[6].split(",")], sys.argv[7].split("|"))
