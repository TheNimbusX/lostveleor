"""Contact sheet for a reels capture mp4: a tile every STEP seconds with the time in the mp4.

python sheet.py <shot-dir-or-mp4> [step=0.5] [start=0] [end=inf] [cols=10]
Writes <shot>_sheet.jpg next to the mp4.
"""
import glob, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFont

FF = r"C:/Users/d.grab/Desktop/the-game/artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
TW, TH = 216, 384


def sheet(path, step=0.5, start=0.0, end=None, cols=10, out=None):
    mp4 = path if path.endswith('.mp4') else sorted(glob.glob(os.path.join(path, '*.mp4')))[0]
    with tempfile.TemporaryDirectory() as tmp:
        args = [FF, '-v', 'error', '-ss', str(start)]
        if end is not None:
            args += ['-t', str(end - start)]
        args += ['-i', mp4, '-vf', 'fps=%g,scale=%d:%d' % (1.0 / step, TW, TH), os.path.join(tmp, 'f_%04d.jpg')]
        subprocess.run(args, check=True)
        files = sorted(glob.glob(os.path.join(tmp, 'f_*.jpg')))
        rows = (len(files) + cols - 1) // cols
        img = Image.new('RGB', (cols * TW, rows * TH), (0, 0, 0))
        d = ImageDraw.Draw(img)
        font = ImageFont.truetype('arial.ttf', 18)
        for i, f in enumerate(files):
            x, y = (i % cols) * TW, (i // cols) * TH
            img.paste(Image.open(f), (x, y))
            t = start + i * step
            d.rectangle([x, y, x + 62, y + 22], fill=(0, 0, 0))
            d.text((x + 4, y + 2), '%.2fs' % t, fill=(255, 230, 0), font=font)
        out = out or mp4[:-4] + '_sheet.jpg'
        img.save(out, quality=82)
        return out


if __name__ == '__main__':
    a = sys.argv[1:]
    p = a[0]
    step = float(a[1]) if len(a) > 1 else 0.5
    start = float(a[2]) if len(a) > 2 else 0.0
    end = float(a[3]) if len(a) > 3 and a[3] != 'inf' else None
    cols = int(a[4]) if len(a) > 4 else 10
    print(sheet(p, step, start, end, cols))
