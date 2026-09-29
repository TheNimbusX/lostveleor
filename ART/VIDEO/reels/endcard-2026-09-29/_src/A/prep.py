# Prepares logo layers + crack masks for the end card (variant A).
# The logo pixels come only from the master PNG (premultiplied Lanczos resize); nothing is redrawn.
import numpy as np, json, os
from PIL import Image
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = r'C:\Users\d.grab\Desktop\the-game\ART\UI\logo-final\TWR-logo-painted-stone-master.png'

LOGO_W = 830.0          # target width of the logo content (alpha>100 bbox) in the 1080 frame
SCALE_START = 1.06      # scale-in from 1.06 -> 1.0

a = np.array(Image.open(SRC)).astype(np.float32) / 255.0
H, W = a.shape[:2]
r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b)
sat = (mx - mn) / np.maximum(mx, 1e-3)

ys, xs = np.where(al > 100 / 255)
bx0, bx1, by0, by1 = xs.min(), xs.max(), ys.min(), ys.max()
print('content bbox', bx0, bx1, by0, by1)

# --- soft window: kill the near-invisible matting haze far from the artwork (canvas edges) so the
# layer never shows a rectangle. Letters/crack/shards are all inside bbox+margin -> untouched (window=1).
m = 110
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
dx = np.maximum(np.maximum(bx0 - m - xx, xx - (bx1 + m)), 0)
dy = np.maximum(np.maximum(by0 - m - yy, yy - (by1 + m)), 0)
d = np.sqrt(dx * dx + dy * dy)
win = np.clip(1 - d / 90.0, 0, 1)
win = win * win * (3 - 2 * win)
al_w = al * win
print('window touched alpha>0.2 pixels:', int(((win < 1) & (al > 0.2)).sum()))

# --- hot (amber/red) measure
hot = np.clip((r - 0.62) / 0.25, 0, 1) * np.clip((sat - 0.42) / 0.2, 0, 1) * np.clip((al - 0.2) / 0.3, 0, 1)
strong = np.clip((r - 0.75) / 0.2, 0, 1) * np.clip((sat - 0.6) / 0.2, 0, 1) * np.clip((0.62 - g) / 0.2, 0, 1)

# --- crack centreline by DP (seam) on half-res strong-hot map
hs = ndi.gaussian_filter(strong, 3)[::2, ::2]
h2, w2 = hs.shape
x_start, x_end = 110, 1665   # half-res columns covering the whole crack
cost = -hs
acc = np.full((h2, w2), np.inf, np.float32)
back = np.zeros((h2, w2), np.int8)
# restrict to a plausible band (crack is in the lower line, y 1000..1600 full res)
band = np.zeros(h2, bool); band[480:800] = True
acc[:, x_start] = np.where(band, cost[:, x_start], np.inf)
for x in range(x_start + 1, x_end + 1):
    prev = acc[:, x - 1]
    best = prev.copy(); arg = np.zeros(h2, np.int8)
    for s in (-2, -1, 1, 2):
        sh = np.roll(prev, s)
        if s > 0: sh[:s] = np.inf
        else: sh[s:] = np.inf
        pen = 0.02 * abs(s)
        better = sh + pen < best
        best = np.where(better, sh + pen, best); arg = np.where(better, -s, arg)
    acc[:, x] = np.where(band, best + cost[:, x], np.inf)
    back[:, x] = arg
yv = np.zeros(w2, np.float32)
y = int(np.argmin(acc[:, x_end]))
for x in range(x_end, x_start - 1, -1):
    yv[x] = y
    y = y + int(back[y, x])
cx = np.arange(x_start, x_end + 1) * 2.0
cy = ndi.uniform_filter1d(yv[x_start:x_end + 1] * 2.0, 9)
print('centreline samples', [(int(cx[i]), int(cy[i])) for i in range(0, len(cx), 150)])

# crack centre y for every full-res column (extrapolate flat at ends)
ycol = np.interp(np.arange(W), cx, cy)
dist = np.abs(yy - ycol[None, :])
x0c, x1c = cx[0], cx[-1]
u = (xx - x0c) / (x1c - x0c)   # along-crack coordinate 0..1

# --- masks
# core crack (tight): hot pixels close to the centreline
core = hot * np.exp(-(dist / 20.0) ** 2)
# cooling mask: hot pixels in a wider band (includes rim glow that belongs to the crack)
coolm = hot * np.exp(-(dist / 70.0) ** 2)
# shards: hot blobs sitting outside the stone letters (little flying ember diamonds)
stone = (al > 0.8) & (sat < 0.35)
stone_d = ndi.binary_dilation(stone, iterations=10)
lab, n = ndi.label((hot > 0.25) & ~stone_d)
sizes = ndi.sum(np.ones_like(hot), lab, range(1, n + 1))
keep = np.zeros(n + 1, bool); keep[1:] = (sizes > 30) & (sizes < 20000)
shard = keep[lab] & (dist < 380)
shard = ndi.binary_dilation(shard, iterations=4).astype(np.float32)
shard = ndi.gaussian_filter(shard, 2) * np.clip(hot * 1.5 + 0.2, 0, 1) * al
coolm = np.maximum(coolm, shard)
core = np.maximum(core, shard * 0.8)
print('shards', int(keep.sum()))

def premul_resize(arrs, size):
    out = []
    for ch in arrs:
        im = Image.fromarray(ch.astype(np.float32), 'F').resize(size, Image.LANCZOS)
        out.append(np.array(im))
    return out

s1 = LOGO_W / (bx1 - bx0)
cw = (bx0 + bx1) / 2.0; ch = (by0 + by1) / 2.0
meta = {'crack_x': cx[::4].tolist(), 'crack_y': cy[::4].tolist(), 's1': s1, 'content_center_master': [cw, ch], 'master_size': [W, H], 'scale_start': SCALE_START}
for tag, s in (('s1', s1), ('sbig', s1 * SCALE_START)):
    size = (int(round(W * s)), int(round(H * s)))
    pr, pg, pb, pa, pcore, pcool, pu = premul_resize(
        [r * al_w, g * al_w, b * al_w, al_w, core * al_w, coolm * al_w, u], size)
    pa = np.clip(pa, 0, 1)
    rgb = np.clip(np.stack([pr, pg, pb], -1), 0, 1)
    rgb = np.minimum(rgb, pa[..., None])
    np.savez_compressed(os.path.join(HERE, f'logo_{tag}.npz'), rgb=rgb.astype(np.float32), a=pa.astype(np.float32),
                        core=np.clip(pcore, 0, 1).astype(np.float32), cool=np.clip(pcool, 0, 1).astype(np.float32),
                        u=pu.astype(np.float32))
    meta[tag] = {'size': size, 'scale_x': size[0] / W, 'scale_y': size[1] / H}
    print(tag, size)
lit = (al > 0.95) & (sat < 0.3) & (hot < 0.05)
cols = np.stack([r[lit], g[lit], b[lit]], -1)
lum = cols @ np.array([0.3, 0.59, 0.11])
meta['stone_p50'] = cols[lum > np.percentile(lum, 40)].mean(0).tolist()
meta['stone_p85'] = cols[lum > np.percentile(lum, 85)].mean(0).tolist()
print('stone colours', meta['stone_p50'], meta['stone_p85'])
json.dump(meta, open(os.path.join(HERE, 'logo_meta.json'), 'w'), indent=1)

# previews
def save(arr, name):
    Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8)).resize((W // 2, H // 2)).save(os.path.join(HERE, name))
save(core, 'm_core.png'); save(coolm, 'm_cool.png')
vis = np.stack([r * al, g * al, b * al], -1) * 0.5
vis[..., 1] += np.clip(1 - dist / 3, 0, 1) * 0.8
save(vis, 'm_line.png')
