import numpy as np
from collections import deque
from PIL import Image, ImageFilter
from common import *

def dilate(m, r):
    img = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * r + 1))
    return np.asarray(img) > 127

def flood(mask, seed):
    h, w = mask.shape; out = np.zeros_like(mask); q = deque([seed])
    if not mask[seed]: return out
    out[seed] = True
    while q:
        y, x = q.popleft()
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not out[ny, nx]:
                out[ny, nx] = True; q.append((ny, nx))
    return out

T = load()
res = {}

# HUNT: crimson -> magenta (hue -30), keep the red ribbon, grip, guard.
t = T['hunt']; h, s, v = hsv(t)
grip = band(h, 0, 30) & (s > .35) & (v > .25)
grip[:, :300] = False; grip[340:, :] = False            # only the hilt zone, not red droplets elsewhere
red = band(h, 330, 15) & (s > .25) & (v > .2)
box = np.zeros_like(red); box[112:345, 432:580] = True
seeds = [(y, x) for y in range(125, 175) for x in range(445, 475) if (red & box)[y, x]]
rib = np.zeros_like(red)
for sd in seeds[:1]: rib = flood(red & box, sd)
prot = dilate(grip | rib, 2)
water = band(h, 325, 15) & (s > .06) & ~prot
h2 = np.where(water, h - 30, h)
res['hunt'] = np.where(water[..., None], rgb(h2, s, v / 255 * 255 / 255 if False else v), t)
print('hunt ribbon px', rib.sum(), 'water px', water.sum())

# ELUSIVE: gold -> pearl glass with a lilac shimmer, keep the main hilt and ribbon.
t = T['elusive']; h, s, v = hsv(t)
hilt = band(h, 350, 22) & (s > .35) & (v > .2)
hilt[:, :300] = False; hilt[360:, :] = False
prot = dilate(hilt, 3)
water = band(h, 15, 75) & (s > .08) & (v > .08) & ~prot
hn = 235 + (np.clip(h, 15, 75) - 45) * 1.4
sn = .05 + .30 * s
vn = np.clip(v * .96, 0, 1)
res['elusive'] = np.where(water[..., None], rgb(hn, sn, vn), t)
print('elusive water px', water.sum())

res['base'] = T['base']; res['foam'] = T['foam']
import os
os.makedirs('../final', exist_ok=True)
names = {'base': 'Icon_Squall', 'hunt': 'Icon_Squall_Hunt', 'foam': 'Icon_Squall_FoamTrail', 'elusive': 'Icon_Squall_Elusive'}
for k, n in names.items():
    Image.fromarray(np.clip(res[k], 0, 255).astype(np.uint8)).resize((1254, 1254), Image.LANCZOS).save(f'../final/{n}.png')
row1 = np.concatenate([T[k] for k in ('base', 'hunt', 'foam', 'elusive')], 1)
row2 = np.concatenate([res[k] for k in ('base', 'hunt', 'foam', 'elusive')], 1)
Image.fromarray(np.clip(np.concatenate([row1, row2], 0), 0, 255).astype(np.uint8)).resize((1600, 800), Image.LANCZOS).save('../sheet-final.jpg', quality=90)
print('done')
