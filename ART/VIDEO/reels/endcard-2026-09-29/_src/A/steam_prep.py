# Cuts the round Steam mark out of the owner's JPEG (white background) for the 3 s end card.
# Valve's pixels are kept as they are: no recolour. Only the circle edge is cleaned:
#   * circle fitted to the blue/white edge (sub-pixel), mask shrunk 1.5 px inside it, analytic AA;
#   * the last ~5.5 px inside the edge (JPEG ringing + chroma bleed of the white background) are replaced
#     by the colour sampled radially at R-5.5, and the same colour is extended under the transparent area,
#     so any later resampling never pulls white into the rim.
import numpy as np, os, sys
from PIL import Image
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'steam-logo-source.jpg')
OUT = os.path.join(os.path.dirname(HERE), 'steam-logo-clean.png')
SHRINK = 1.5      # px inside the fitted white edge
CLEAN = 5.5       # px band re-filled radially
CANVAS = 440

im = np.asarray(Image.open(SRC).convert('RGB')).astype(np.float32)
H, W, _ = im.shape
R = im[..., 0]

# --- sub-pixel edge points: last dark->white crossing of the red channel along each ray
cx0, cy0 = (W - 1) / 2.0, (H - 1) / 2.0
pts = []
for th in np.linspace(0, 2 * np.pi, 1440, endpoint=False):
    rr = np.arange(150, min(W, H) / 2.0 + 8, 0.25)
    x = cx0 + rr * np.cos(th); y = cy0 + rr * np.sin(th)
    ok = (x >= 0) & (x <= W - 1) & (y >= 0) & (y <= H - 1)
    v = ndi.map_coordinates(R, [y[ok], x[ok]], order=1)
    idx = np.where((v[:-1] < 128) & (v[1:] >= 128))[0]
    if len(idx):
        i = idx[-1]; r = rr[ok][i] + 0.25 * (128 - v[i]) / (v[i + 1] - v[i] + 1e-6)
        pts.append((cx0 + r * np.cos(th), cy0 + r * np.sin(th)))
pts = np.array(pts)

def fit(x, y):
    M = np.c_[2 * x, 2 * y, np.ones_like(x)]
    s = np.linalg.lstsq(M, x * x + y * y, rcond=None)[0]
    return s[0], s[1], np.sqrt(s[2] + s[0] ** 2 + s[1] ** 2)

x, y = pts[:, 0], pts[:, 1]
cx, cy, rad = fit(x, y)
for _ in range(2):   # drop rays that hit the white arm instead of the rim
    keep = np.abs(np.hypot(x - cx, y - cy) - rad) < 1.5
    cx, cy, rad = fit(x[keep], y[keep])
res = np.hypot(x[keep] - cx, y[keep] - cy) - rad
print('circle c=(%.2f, %.2f) r=%.2f  rays=%d  resid std=%.2f px' % (cx, cy, rad, keep.sum(), res.std()))

# --- canvas centred on the circle (native pixel grid, no resampling of the interior)
ox = int(round(cx - CANVAS / 2.0)); oy = int(round(cy - CANVAS / 2.0))
ccx, ccy = cx - ox, cy - oy
yy, xx = np.mgrid[0:CANVAS, 0:CANVAS].astype(np.float32)
d = np.hypot(xx - ccx, yy - ccy)
r_in = rad - CLEAN
sel = d > r_in
k = r_in / np.maximum(d[sel], 1e-3)
sx = cx + (xx[sel] - ccx) * k; sy = cy + (yy[sel] - ccy) * k
rgb = im[oy:oy + CANVAS, ox:ox + CANVAS].copy()
for c in range(3):
    rgb[..., c][sel] = ndi.map_coordinates(im[..., c], [sy, sx], order=1)
alpha = np.clip(rad - SHRINK - d + 0.5, 0, 1)

out = np.dstack([np.clip(rgb + 0.5, 0, 255), alpha * 255.0 + 0.5]).astype(np.uint8)
Image.fromarray(out, 'RGBA').save(OUT, optimize=True)
print('saved', OUT, out.shape, 'mask radius %.2f px, centre in canvas (%.2f, %.2f)' % (rad - SHRINK, ccx, ccy))
