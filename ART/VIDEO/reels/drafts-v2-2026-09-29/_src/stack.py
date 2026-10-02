"""Stack several shots into one review image: one row per shot, a tile every STEP s (small tiles)."""
import glob, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFont
FF = r"C:/Users/d.grab/Desktop/the-game/artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
CAP = r"C:/Users/d.grab/Desktop/the-game/artifacts/capture/reels-v2"
def row(name, step, start, end, tw, th):
    mp4 = os.path.join(CAP, name, name + '_1080x1920_60fps.mp4')
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run([FF, '-v', 'error', '-ss', str(start), '-t', str(end - start), '-i', mp4, '-vf', 'fps=%g,scale=%d:%d' % (1 / step, tw, th), os.path.join(tmp, 'f_%04d.jpg')], check=True)
        return [Image.open(f).copy() for f in sorted(glob.glob(os.path.join(tmp, 'f_*.jpg')))]
def main(out, step, start, end, names, tw=144, th=256):
    rows = [row(n, step, start, end, tw, th) for n in names]
    cols = max(len(r) for r in rows)
    img = Image.new('RGB', (cols * tw, len(rows) * (th + 18)), (0, 0, 0))
    d = ImageDraw.Draw(img); font = ImageFont.truetype('arial.ttf', 13)
    for y, (n, r) in enumerate(zip(names, rows)):
        oy = y * (th + 18)
        d.text((4, oy + 2), n, fill=(0, 255, 255), font=font)
        for x, im in enumerate(r):
            img.paste(im, (x * tw, oy + 18))
            d.text((x * tw + 3, oy + 20), '%.1f' % (start + x * step), fill=(255, 230, 0), font=font)
    img.save(out, quality=80); print(out)
if __name__ == '__main__':
    a = sys.argv
    main(a[1], float(a[2]), float(a[3]), float(a[4]), a[5:])
