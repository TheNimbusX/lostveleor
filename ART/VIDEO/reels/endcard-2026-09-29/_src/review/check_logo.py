# Compare the logo in a rendered frame against an independent premultiplied Lanczos downscale of the master PNG.
# usage: check_logo.py frame.png bbox_width cx cy [out_prefix]
import sys, numpy as np
from PIL import Image
LOGO = r"C:\Users\d.grab\Desktop\the-game\ART\UI\logo-final\TWR-logo-painted-stone-master.png"
fr = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(np.float32)
BW = float(sys.argv[2]); CX = float(sys.argv[3]); CY = float(sys.argv[4])
pref = sys.argv[5] if len(sys.argv) > 5 else None
m = np.asarray(Image.open(LOGO)).astype(np.float32) / 255.0
a = m[..., 3]
ys, xs = np.where(a > 8 / 255)
bx0, bx1, by0, by1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
S = BW / (bx1 - bx0)
prem = [Image.fromarray((m[..., c] * a).astype(np.float32), 'F') for c in range(3)] + [Image.fromarray(a.astype(np.float32), 'F')]
# output window around the logo
W = int(BW) + 40; H = int((by1 - by0) * S) + 40
best = None
def render_at(fx, fy):
    # output pixel (i,j) in frame coords: X = x0 + i ; maps to master  mx = bx_c + (X + 0.5 - CX - fx)/S - 0.5 ...
    x0 = int(CX - W / 2); y0 = int(CY - H / 2)
    bcx = (bx0 + bx1) / 2; bcy = (by0 + by1) / 2
    mx0 = bcx + (x0 - CX - fx) / S; my0 = bcy + (y0 - CY - fy) / S
    box = (mx0, my0, mx0 + W / S, my0 + H / S)
    ch = [np.asarray(p.resize((W, H), Image.LANCZOS, box=box)) for p in prem]
    return x0, y0, np.dstack(ch)
for fy in np.arange(-6, 6.01, 1.0):
    for fx in np.arange(-6, 6.01, 1.0):
        x0, y0, L = render_at(fx, fy)
        al = L[..., 3]
        sel = al > 0.96
        ref = np.clip(L[..., :3] / np.maximum(al[..., None], 1e-4), 0, 1) * 255
        got = fr[y0:y0 + H, x0:x0 + W]
        d = np.abs(got - ref)[sel].mean()
        if best is None or d < best[0]:
            best = (d, fx, fy)
d, fx, fy = best
# refine
for fy2 in np.arange(fy - 0.4, fy + 0.41, 0.1):
    for fx2 in np.arange(fx - 0.4, fx + 0.41, 0.1):
        x0, y0, L = render_at(fx2, fy2)
        al = L[..., 3]; sel = al > 0.96
        ref = np.clip(L[..., :3] / np.maximum(al[..., None], 1e-4), 0, 1) * 255
        got = fr[y0:y0 + H, x0:x0 + W]
        dd = np.abs(got - ref)[sel].mean()
        if dd < best[0]: best = (dd, fx2, fy2)
d, fx, fy = best
x0, y0, L = render_at(fx, fy)
al = L[..., 3]; sel = al > 0.96
ref = np.clip(L[..., :3] / np.maximum(al[..., None], 1e-4), 0, 1) * 255
got = fr[y0:y0 + H, x0:x0 + W]
diff = np.abs(got - ref).max(-1)
dm = diff[sel]
# warm tint check: mean signed diff per channel on opaque stone
sd = (got - ref)[sel].mean(0)
print('shift', round(fx, 2), round(fy, 2), 'opaque px', sel.sum(), 'mean|d| %.2f p50 %.1f p95 %.1f p99 %.1f max %.0f' % (dm.mean(), np.percentile(dm, 50), np.percentile(dm, 95), np.percentile(dm, 99), dm.max()), 'signed rgb', np.round(sd, 2))
# rows (y) where large diffs cluster
if pref:
    vis = np.clip(diff * 8, 0, 255) * sel
    Image.fromarray(vis.astype(np.uint8)).save(pref + '_diff.png')
    both = np.concatenate([got, ref * al[..., None] + got * (1 - al[..., None])], 0)
    Image.fromarray(np.clip(both, 0, 255).astype(np.uint8)).save(pref + '_pair.png')
