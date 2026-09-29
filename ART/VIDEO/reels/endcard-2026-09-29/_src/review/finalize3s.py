# 3 s cut of end card A (+ Steam mark): encode 9:16 and 4:5, hold-frame PNGs, contact sheet every 0.2 s
# (decoded back from the encoded 9:16 file) and a TikTok/Reels safe-zone check.
import os, sys, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
SP = os.path.dirname(HERE)
FF = open(os.path.join(SP, 'endcard-A', 'ffpath.txt')).read().strip()
SEQ = os.path.join(SP, 'endcard-A', 'frames3s')
OUT = r"C:\Users\d.grab\Desktop\the-game\ART\VIDEO\reels\endcard-2026-09-29"
CHK = os.path.join(OUT, 'checks')
NAME = 'TWR-endcard-A-3s'
NF = 90
os.makedirs(CHK, exist_ok=True)
what = sys.argv[1:] or ['encode', 'png', 'sheet', 'safe']

last = Image.open(os.path.join(SEQ, 'f%04d.png' % (NF - 1))).convert('RGB')
L = np.asarray(last.convert('L')).astype(np.float32)
L[1262:] = 0   # embers drifting below the title block are not content (Steam circle ends at row 1248)
ys, xs = np.where(L > 150)
# the Steam circle is darker than 150 at the top; include it via its blue channel
B = np.asarray(last).astype(np.float32)
blue = (B[..., 2] > 120) & (B[..., 2] > B[..., 0] + 40)
blue[:, :400] = False; blue[:, 680:] = False; blue[1400:] = False   # only the mark under the text
ys2, xs2 = np.where(blue)
top, bot = min(ys.min(), ys2.min()), max(ys.max(), ys2.max())
Y45 = int(round((top + bot) / 2.0 - 675))
Y45 = max(0, min(Y45, 1920 - 1350))
print('content rows %d..%d -> 4:5 crop y=%d..%d' % (top, bot, Y45, Y45 + 1350))

def encode(out, crop=None, crf=17):
    vf = []
    if crop: vf.append('crop=%d:%d:%d:%d' % crop)
    vf.append('scale=out_color_matrix=bt709:out_range=tv:flags=lanczos+accurate_rnd+full_chroma_int')
    vf.append('setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709')
    cmd = [FF, '-v', 'error', '-y', '-framerate', '30', '-i', os.path.join(SEQ, 'f%04d.png'), '-vf', ','.join(vf),
           '-an', '-c:v', 'libx264', '-preset', 'slow', '-crf', str(crf), '-tune', 'grain',
           '-profile:v', 'high', '-level', '4.2', '-pix_fmt', 'yuv420p',
           '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv',
           '-movflags', '+faststart', out]
    subprocess.run(cmd, check=True)
    print(os.path.basename(out), '%.2f MB' % (os.path.getsize(out) / 1e6))

if 'encode' in what:
    encode(os.path.join(OUT, NAME + '-1080x1920.mp4'))
    encode(os.path.join(OUT, NAME + '-1080x1350.mp4'), crop=(1080, 1350, 0, Y45))

if 'png' in what:
    last.save(os.path.join(OUT, NAME + '-hold-1080x1920.png'), optimize=True)
    last.crop((0, Y45, 1080, Y45 + 1350)).save(os.path.join(OUT, NAME + '-hold-1080x1350.png'), optimize=True)
    print('hold pngs')

def font(sz):
    return ImageFont.truetype('arial.ttf', sz)

if 'sheet' in what:
    W, H = 1080, 1920
    raw = subprocess.run([FF, '-v', 'error', '-i', os.path.join(OUT, NAME + '-1080x1920.mp4'), '-f', 'rawvideo',
                          '-pix_fmt', 'rgb24', '-'], capture_output=True).stdout
    n = len(raw) // (W * H * 3)
    fr = np.frombuffer(raw, np.uint8)[:n * W * H * 3].reshape(n, H, W, 3)
    idx = [int(round(k * 0.2 * 30)) for k in range(15)] + [n - 1]
    tw, th, cols = 240, 427, 8
    rows = (len(idx) + cols - 1) // cols
    s = Image.new('RGB', (cols * (tw + 8) + 8, rows * (th + 30) + 8), (34, 34, 38)); d = ImageDraw.Draw(s)
    for k, i in enumerate(idx):
        im = Image.fromarray(fr[i]).resize((tw, th), Image.LANCZOS)
        x = 8 + (k % cols) * (tw + 8); y = 8 + (k // cols) * (th + 30)
        s.paste(im, (x, y))
        d.text((x + 4, y + th + 5), '%.2f s  f%d%s' % (i / 30.0, i, '  (stop)' if i == n - 1 else ''), fill=(220, 220, 220), font=font(17))
    s.save(os.path.join(CHK, NAME + '-sheet.jpg'), quality=90)
    print('sheet', n, 'frames decoded', s.size)

if 'safe' in what:
    im = last.convert('RGBA')
    ov = Image.new('RGBA', im.size, (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
    red = (255, 40, 40, 70)
    d.rectangle([0, 0, 1080, 210], fill=red)
    d.rectangle([0, 1440, 1080, 1920], fill=red)
    d.rectangle([940, 640, 1080, 1440], fill=red)
    d.rectangle([0, Y45, 1079, Y45 + 1349], outline=(255, 210, 60, 255), width=3)
    d.rectangle((xs.min(), top, xs.max(), bot), outline=(80, 200, 255, 255), width=2)
    d.text((20, 1450), 'TikTok / Reels UI (caption, music, nav)', fill=(255, 255, 255, 255), font=font(30))
    d.text((20, Y45 + 8), '4:5 crop', fill=(255, 210, 60, 255), font=font(30))
    Image.alpha_composite(im, ov).convert('RGB').resize((540, 960), Image.LANCZOS).save(
        os.path.join(CHK, NAME + '-safezones-check.jpg'), quality=90)
    print('safe check')
