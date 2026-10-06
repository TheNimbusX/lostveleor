"""Крушение v4: видео для владельца из кадров v4_render (60 к/с симуляции) — без стоп-кадров.
python v4_media.py video <frames_glob> <seq.json> <out_stem> <title>          → <out_stem>-1x.mp4 и -0.5x.mp4
python v4_media.py pair <globA> <seqA> <titleA> <globB> <seqB> <titleB> <out_stem> → бок о бок, общий таймер
python v4_media.py sheet <out.jpg> <title> <png> <label> [<png> <label> ...]     → ключевые позы
"""
import sys, os, glob, json, subprocess, tempfile, shutil
from PIL import Image, ImageDraw, ImageFont

F = r"C:\Windows\Fonts\arial.ttf"


def ffmpeg():
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def font(n):
    try: return ImageFont.truetype(F, n)
    except OSError: return ImageFont.load_default()


def label(im, top, right):
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, im.width, 26), fill=(20, 22, 22))
    d.text((8, 4), top, fill=(235, 230, 220), font=font(17))
    w = d.textlength(right, font=font(15))
    d.text((im.width - w - 8, 5), right, fill=(200, 200, 190), font=font(15))
    return im


def tag_of(f):
    c = f.get("clip", "").replace("Pelag_AN_Wreck4_", "").replace("Pelag_AN_", "")
    return "%s  тик %.1f" % (c, f.get("frame", 0))


def encode(frames, out, fps=30):
    tmp = tempfile.mkdtemp()
    for i, im in enumerate(frames): im.save(os.path.join(tmp, "f_%04d.png" % i))
    subprocess.run([ffmpeg(), "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(tmp, "f_%04d.png"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", out], check=True)
    shutil.rmtree(tmp)
    print("video", out, len(frames))


def load(g, seq):
    fs = sorted(glob.glob(g)); meta = json.load(open(seq, encoding="utf-8"))["frames"]
    assert len(fs) == len(meta), (g, len(fs), len(meta))
    return [Image.open(f).convert("RGB") for f in fs], meta


def video(g, seq, stem, title):
    ims, meta = load(g, seq)
    for speed, step in (("1x", 2), ("0.5x", 1)):
        out = []
        for i in range(0, len(ims), step):
            out.append(label(ims[i].copy(), title, "%s   %s   t=%.3f с" % ("реальное время" if step == 2 else "0,5×", tag_of(meta[i]), meta[i]["t"])))
        encode(out, "%s-%s.mp4" % (stem, speed))


def pair(ga, sa, ta, gb, sb, tb, stem):
    A, ma = load(ga, sa); B, mb = load(gb, sb)
    n = max(len(A), len(B))
    for speed, step in (("1x", 2), ("0.5x", 1)):
        out = []
        for i in range(0, n, step):
            ia, ib = min(i, len(A) - 1), min(i, len(B) - 1)
            a = label(A[ia].copy(), ta, tag_of(ma[ia])); b = label(B[ib].copy(), tb, tag_of(mb[ib]))
            im = Image.new("RGB", (a.width + b.width + 6, max(a.height, b.height)), (10, 10, 10))
            im.paste(a, (0, 0)); im.paste(b, (a.width + 6, 0))
            d = ImageDraw.Draw(im); s = "реальное время" if step == 2 else "0,5×"
            d.text((im.width // 2 - 60, im.height - 24), "%s  t=%.3f с" % (s, i / 60.0), fill=(240, 240, 230), font=font(16))
            out.append(im)
        encode(out, "%s-%s.mp4" % (stem, speed))


def sheet(out, title, items):
    ims = [(Image.open(p).convert("RGB"), l) for p, l in items]
    w, h = ims[0][0].size; cols = min(4, len(ims)); rows = (len(ims) + cols - 1) // cols
    S = Image.new("RGB", (w * cols, h * rows + 34), (18, 20, 20))
    d = ImageDraw.Draw(S); d.text((10, 7), title, fill=(240, 235, 225), font=font(20))
    for i, (im, l) in enumerate(ims):
        im = label(im, l, "")
        S.paste(im, ((i % cols) * w, 34 + (i // cols) * h))
    S.save(out, quality=90); print("sheet", out)


if __name__ == "__main__":
    a = sys.argv[1:]
    if a[0] == "video": video(*a[1:5])
    elif a[0] == "pair": pair(*a[1:8])
    else: sheet(a[1], a[2], list(zip(a[3::2], a[4::2])))
