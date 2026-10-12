"""Видео из кадров съёмки 120 к/с: make_media.py <frames_dir> <out.mp4> <step> <W,H> <a-b@cx,cy> [<a-b@cx,cy> ...]
step 2 — реальное время при 60 к/с; step 1 — 0,5x при 60 к/с. Каждый кадр выхода — свой кадр съёмки (без стоп-кадров);
куски — броски серии без пауз между ними, у каждого своя рамка 1:1 (герой и линия броска)."""
import sys, os, subprocess, tempfile, shutil
from PIL import Image

FF = r"C:\Users\d.grab\Desktop\the-game\artifacts\tools\python\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe"
src, out, step = sys.argv[1], sys.argv[2], int(sys.argv[3])
W, H = (int(v) for v in sys.argv[4].split(","))
parts = sys.argv[5:]
files = sorted(f for f in os.listdir(src) if f.lower().endswith((".jpg", ".png")))
tmp = tempfile.mkdtemp(prefix="throwmedia_")
try:
    k = 0
    for part in parts:
        rng, c = part.split("@")
        first, last = (int(v) for v in rng.split("-"))
        cx, cy = (int(v) for v in c.split(","))
        for i in range(first, min(last, len(files) - 1) + 1, step):
            im = Image.open(os.path.join(src, files[i])).convert("RGB")
            x0 = max(0, min(im.width - W, cx - W // 2))
            y0 = max(0, min(im.height - H, cy - H // 2))
            im.crop((x0, y0, x0 + W, y0 + H)).save(os.path.join(tmp, "f%05d.png" % k))
            k += 1
    subprocess.run([FF, "-y", "-loglevel", "error", "-framerate", "60", "-i", os.path.join(tmp, "f%05d.png"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "16", "-preset", "slow", out], check=True)
    print(out, k, "frames")
finally:
    shutil.rmtree(tmp, ignore_errors=True)
