"""Draw planar cross-sections of the dumped mesh, coloured by base-colour texture samples.
usage: python slices.py mesh_dump.npz out.png axis(y|z|x) v0 v1 step [labels.npy]"""
import sys

import numpy as np
from PIL import Image, ImageDraw

d = np.load(sys.argv[1])
out = sys.argv[2]
axis = "xyz".index(sys.argv[3])
vals = np.arange(float(sys.argv[4]), float(sys.argv[5]) + 1e-6, float(sys.argv[6]))
labels = np.load(sys.argv[7]) if len(sys.argv) > 7 else None
co, tris, col = d["co"], d["tris"], d["tri_col"]
pal = [(230, 60, 60), (60, 120, 240), (60, 200, 80), (240, 200, 40), (200, 60, 220), (40, 220, 220), (255, 140, 0), (150, 150, 150), (255, 255, 255), (120, 60, 20), (0, 0, 0), (255, 120, 180), (90, 200, 160)]
plane = [a for a in range(3) if a != axis]
S = int(__import__("os").environ.get("SL_S", 300))
cell = S + 20
cols = 4
rows = (len(vals) + cols - 1) // cols
img = Image.new("RGB", (cols * cell, rows * cell), (40, 44, 48))
dr = ImageDraw.Draw(img)
lo = co[:, plane].min(0) - 0.02
hi = co[:, plane].max(0) + 0.02
span = (hi - lo).max()


def to_px(p, ox, oy):
    u = (p[0] - lo[0]) / span * S
    v = (p[1] - lo[1]) / span * S
    return ox + 10 + u, oy + 10 + S - v


P = co[tris]  # (T,3,3)
for k, val in enumerate(vals):
    ox, oy = (k % cols) * cell, (k // cols) * cell
    dr.text((ox + 12, oy + 4), f"{'xyz'[axis]}={val:+.2f}", fill=(255, 255, 0))
    # grid lines every 0.1 m
    for g in np.arange(-1.0, 1.4, 0.1):
        for a in range(2):
            if lo[a] <= g <= hi[a]:
                if a == 0:
                    x0, _ = to_px((g, lo[1]), ox, oy)
                    dr.line([(x0, oy + 10), (x0, oy + 10 + S)], fill=(70, 74, 78) if abs(g) > 1e-6 else (120, 120, 120))
                else:
                    _, y0 = to_px((lo[0], g), ox, oy)
                    dr.line([(ox + 10, y0), (ox + 10 + S, y0)], fill=(70, 74, 78) if abs(g) > 1e-6 else (120, 120, 120))
    s = P[:, :, axis] - val
    for t in np.nonzero((s.min(1) < 0) & (s.max(1) > 0))[0]:
        pts = []
        for i in range(3):
            j = (i + 1) % 3
            a, b = s[t, i], s[t, j]
            if (a < 0) != (b < 0):
                f = a / (a - b)
                p = P[t, i] + f * (P[t, j] - P[t, i])
                pts.append(to_px(p[plane], ox, oy))
        if len(pts) == 2:
            if labels is not None:
                c = pal[int(labels[t]) % len(pal)]
            else:
                c = tuple(int(255 * min(1, max(0, x)) ** (1 / 2.2)) for x in col[t])
            dr.line(pts, fill=c, width=2)
img.save(out)
print("saved", out, "plane axes", [ "xyz"[a] for a in plane])
