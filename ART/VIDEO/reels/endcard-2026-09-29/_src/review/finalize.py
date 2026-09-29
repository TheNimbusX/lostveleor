# Encode the final deliverables for both end cards from their lossless PNG sequences,
# write hold-frame PNGs, a safe-zone check image and the side-by-side review sheet.
import os, sys, subprocess, json, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
SP = os.path.dirname(HERE)
FF = open(os.path.join(SP, 'endcard-A', 'ffpath.txt')).read().strip()
OUT = r"C:\Users\d.grab\Desktop\the-game\ART\VIDEO\reels\endcard-2026-09-29"
CHK = os.path.join(OUT, 'checks')
os.makedirs(CHK, exist_ok=True)

V = {
    'A': dict(name='TWR-endcard-A-stone-embers', seq=os.path.join(SP, 'endcard-A', 'frames'), y45=250),
    'B': dict(name='TWR-endcard-B-gameplay-slam', seq=os.path.join(SP, 'endcard-B', 'frames_v4'), y45=221),
}
CRF = {'A': 17, 'B': 17}
what = sys.argv[1:] or ['encode', 'png', 'safe', 'review']

def encode(src_glob, out, crop=None, crf=17):
    vf = []
    if crop: vf.append('crop=%d:%d:%d:%d' % crop)
    vf.append('scale=out_color_matrix=bt709:out_range=tv:flags=lanczos+accurate_rnd+full_chroma_int')
    vf.append('setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709')
    cmd = [FF, '-v', 'error', '-y', '-framerate', '30', '-i', src_glob, '-vf', ','.join(vf),
           '-an', '-c:v', 'libx264', '-preset', 'slow', '-crf', str(crf), '-tune', 'grain',
           '-profile:v', 'high', '-level', '4.2', '-pix_fmt', 'yuv420p',
           '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv',
           '-movflags', '+faststart', out]
    subprocess.run(cmd, check=True)
    print(os.path.basename(out), '%.2f MB' % (os.path.getsize(out) / 1e6))

if 'encode' in what:
    for k, v in V.items():
        g = os.path.join(v['seq'], 'f%04d.png')
        encode(g, os.path.join(OUT, v['name'] + '-1080x1920.mp4'), crf=CRF[k])
        encode(g, os.path.join(OUT, v['name'] + '-1080x1350.mp4'), crop=(1080, 1350, 0, v['y45']), crf=CRF[k])

if 'png' in what:
    for k, v in V.items():
        last = Image.open(os.path.join(v['seq'], 'f0149.png')).convert('RGB')
        last.save(os.path.join(OUT, v['name'] + '-hold-1080x1920.png'), optimize=True)
        last.crop((0, v['y45'], 1080, v['y45'] + 1350)).save(os.path.join(OUT, v['name'] + '-hold-1080x1350.png'), optimize=True)
        print('png', k)

def font(sz):
    return ImageFont.truetype('arial.ttf', sz)

if 'safe' in what:
    # TikTok / Reels UI guide over the 9:16 hold frames (check image only, never in the deliverable)
    tiles = []
    for k, v in V.items():
        im = Image.open(os.path.join(v['seq'], 'f0149.png')).convert('RGBA')
        ov = Image.new('RGBA', im.size, (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
        red = (255, 40, 40, 70)
        d.rectangle([0, 0, 1080, 210], fill=red)                 # status bar + tabs
        d.rectangle([0, 1440, 1080, 1920], fill=red)             # caption / music / nav (bottom 25%)
        d.rectangle([940, 640, 1080, 1440], fill=red)            # like / comment / share column
        d.rectangle([90, 520, 940, 1300], outline=(60, 255, 90, 255), width=4)   # centre band
        # measured content bbox (bright pixels) for the record
        a = np.asarray(Image.open(os.path.join(v['seq'], 'f0149.png')).convert('L')).astype(np.float32)
        ys, xs = np.where(a > 150)
        bb = (xs.min(), ys.min(), xs.max(), ys.max())
        d.rectangle(bb, outline=(80, 200, 255, 255), width=2)
        f = font(30)
        d.text((100, 1310), 'content %d..%d x %d..%d' % (bb[0], bb[2], bb[1], bb[3]), fill=(80, 200, 255, 255), font=f)
        d.text((20, 1450), 'TikTok / Reels UI (caption, music, nav)', fill=(255, 255, 255, 255), font=f)
        d.text((950, 650), 'UI', fill=(255, 255, 255, 255), font=f)
        tiles.append(Image.alpha_composite(im, ov).convert('RGB'))
        print(k, 'content bbox', bb)
    s = Image.new('RGB', (2 * 1080 + 20, 1920), (30, 30, 30))
    for i, t in enumerate(tiles): s.paste(t, (i * 1100, 0))
    s.resize((s.width // 2, s.height // 2), Image.LANCZOS).save(os.path.join(CHK, 'TWR-endcard-safezones-check.jpg'), quality=90)

if 'review' in what:
    MOM = {
        'A': [(0.30, 'лого из темноты'), (1.20, 'свет бежит по трещине'), (2.30, 'пик свечения, текст'),
              (3.00, 'текст встал'), (4.967, 'финальный кадр (стоп)')],
        'B': [(0.00, 'бой: удар Вендиго'), (1.10, 'замедление, лава'), (1.37, 'удар логотипа'),
              (2.50, 'текст'), (4.967, 'финальный кадр (стоп)')],
    }
    TITLE = {'A': 'A — «Камень и угли»: тёмная карточка, свет по трещине, угли',
             'B': 'B — «Из боя в логотип»: секунда геймплея, стоп-кадр, удар логотипа'}
    TW, TH = 360, 640
    PAD, HDR, LAB = 16, 56, 40
    W = 5 * TW + 6 * PAD; H = 2 * (HDR + TH + LAB) + 3 * PAD
    s = Image.new('RGB', (W, H), (24, 22, 20)); d = ImageDraw.Draw(s)
    for r, k in enumerate(('A', 'B')):
        y0 = PAD + r * (HDR + TH + LAB + PAD)
        d.text((PAD, y0 + 12), TITLE[k], fill=(245, 225, 190), font=font(30))
        for c, (t, lab) in enumerate(MOM[k]):
            fi = min(int(round(t * 30)), 149)
            im = Image.open(os.path.join(V[k]['seq'], 'f%04d.png' % fi)).convert('RGB').resize((TW, TH), Image.LANCZOS)
            x = PAD + c * (TW + PAD)
            s.paste(im, (x, y0 + HDR))
            d.text((x, y0 + HDR + TH + 8), '%.1f с — %s' % (fi / 30.0, lab), fill=(210, 200, 185), font=font(20))
    s.save(os.path.join(OUT, 'review.jpg'), quality=92)
    print('review', s.size)
