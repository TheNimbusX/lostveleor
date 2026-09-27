"""System-python: overlay a 0.1 m world grid on the ortho views, plus slice scatter plots.
Args: <views_dir> [points_json]
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

vd = Path(sys.argv[1])
data = json.loads((vd / "verts.json").read_text())
co = np.array(data["co"]).reshape(-1, 3)
res, ortho = data["res"], data["ortho"]
pts = json.loads(Path(sys.argv[2]).read_text()) if len(sys.argv) > 2 else {}


def project(M, p):
    M = np.array(M)
    inv = np.linalg.inv(M)
    q = inv @ np.array([p[0], p[1], p[2], 1.0])
    return ((q[0] / ortho + 0.5) * res, (0.5 - q[1] / ortho) * res)


for name, meta in data["meta"].items():
    img = Image.open(vd / f"{name}.png").convert("RGB")
    dr = ImageDraw.Draw(img)
    M = meta["mat"]
    # axis pairs per view
    if name in ("front", "back"):
        a, b = 0, 2
    elif name in ("left_side", "right_side"):
        a, b = 1, 2
    else:
        a, b = 0, 1
    for i in range(-10, 16):
        v = i * 0.1
        for axis in (a, b):
            p0 = [0, 0, 0]; p1 = [0, 0, 0]
            other = b if axis == a else a
            p0[axis] = v; p1[axis] = v
            p0[other] = -1.0; p1[other] = 1.5
            x0, y0 = project(M, p0); x1, y1 = project(M, p1)
            col = (255, 60, 60) if i == 0 else ((255, 255, 0) if i % 5 == 0 else (90, 90, 90))
            dr.line([(x0, y0), (x1, y1)], fill=col, width=1)
            lx, ly = project(M, [v if k == axis else 0 for k in range(3)])
            lab = "xyz"[axis] + f"{v:+.1f}"
            if axis == a:
                dr.text((lx + 2, 5), lab, fill=(255, 255, 255))
            else:
                dr.text((5, ly + 1), lab, fill=(255, 255, 255))
    for label, p in pts.items():
        x, y = project(M, p)
        dr.ellipse([x - 5, y - 5, x + 5, y + 5], outline=(0, 255, 255), width=2)
        dr.text((x + 6, y - 6), label, fill=(0, 255, 255))
    img.save(vd / f"{name}_grid.png")

# slice scatters: YZ slices at several x, XY slices at several z
out = vd / "slices"
out.mkdir(exist_ok=True)
S = 400  # px per metre
def scatter(sel, ax, bx, rng_a, rng_b, fname, title):
    w = int((rng_a[1] - rng_a[0]) * S); h = int((rng_b[1] - rng_b[0]) * S)
    img = Image.new("RGB", (w, h), (20, 20, 24))
    dr = ImageDraw.Draw(img)
    for gv in np.arange(-1.0, 1.6, 0.1):
        if rng_a[0] <= gv <= rng_a[1]:
            x = (gv - rng_a[0]) * S; dr.line([(x, 0), (x, h)], fill=(60, 60, 60)); dr.text((x + 2, 2), f"{gv:+.1f}", fill=(200, 200, 200))
        if rng_b[0] <= gv <= rng_b[1]:
            y = h - (gv - rng_b[0]) * S; dr.line([(0, y), (w, y)], fill=(60, 60, 60)); dr.text((2, y + 2), f"{gv:+.1f}", fill=(200, 200, 200))
    for p in sel:
        x = (p[ax] - rng_a[0]) * S; y = h - (p[bx] - rng_b[0]) * S
        dr.ellipse([x-2, y-2, x+2, y+2], fill=(230, 230, 120))
    dr.text((10, h - 15), title, fill=(255, 120, 120))
    img.save(out / fname)

for xc in (0.0, 0.2, 0.35, 0.5, 0.65, 0.75):
    sel = co[np.abs(co[:, 0] - xc) < 0.035]
    scatter(sel, 1, 2, (-0.95, 0.95), (-0.05, 1.4), f"yz_x{xc:+.2f}.png", f"YZ slice at x={xc:+.2f} (front=-Y left)")
for zc in (0.08, 0.2, 0.35, 0.5, 0.65, 0.8, 0.95, 1.1):
    sel = co[np.abs(co[:, 2] - zc) < 0.035]
    scatter(sel, 0, 1, (-0.95, 0.95), (-0.95, 0.95), f"xy_z{zc:.2f}.png", f"XY slice at z={zc:.2f} (-Y down=front)")
for yc in (-0.7, -0.55, -0.4, -0.25, -0.1, 0.1, 0.3, 0.5, 0.65):
    sel = co[np.abs(co[:, 1] - yc) < 0.035]
    scatter(sel, 0, 2, (-0.95, 0.95), (-0.05, 1.4), f"xz_y{yc:+.2f}.png", f"XZ slice at y={yc:+.2f}")
print("GRID_DONE")
