"""Measure limb centre lines from Meshy leg/arm membership (to place joints).

usage: blender -b -P limb_slices.py -- meshy_autorig_r01.glb
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_common as rc  # noqa: E402

src = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
arm, mesh = rc.load_meshy(src)
mw = mesh.matrix_world
names = [g.name for g in mesh.vertex_groups]
SETS = {
    "LLeg": {"LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase"},
    "RLeg": {"RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"},
    "LArm": {"LeftArm", "LeftForeArm", "LeftHand"},
    "RArm": {"RightArm", "RightForeArm", "RightHand"},
}
pts = {k: [] for k in SETS}
for v in mesh.data.vertices:
    p = mw @ v.co
    for k, bones in SETS.items():
        m = sum(g.weight for g in v.groups if names[g.group] in bones)
        if m > 0.5:
            pts[k].append(p)

for k in ("LLeg", "RLeg"):
    print("==", k, len(pts[k]))
    for i in range(32):
        z0, z1 = i * 0.05, i * 0.05 + 0.05
        sl = [p for p in pts[k] if z0 <= p.z < z1]
        if not sl:
            continue
        c = sum(sl, Vector()) / len(sl)
        xs = [p.x for p in sl]
        ys = [p.y for p in sl]
        print(f"SLICE {k} z{z0:.2f} n{len(sl):4d} c({c.x:+.3f},{c.y:+.3f}) x[{min(xs):+.2f},{max(xs):+.2f}] y[{min(ys):+.2f},{max(ys):+.2f}]")

for k in ("LArm", "RArm"):
    sgn = 1 if k == "LArm" else -1
    print("==", k, len(pts[k]))
    # slice along |x| for the upper part, then along z for the hanging spike
    for i in range(20):
        x0, x1 = 0.10 + i * 0.04, 0.14 + i * 0.04
        sl = [p for p in pts[k] if x0 <= sgn * p.x < x1]
        if not sl:
            continue
        c = sum(sl, Vector()) / len(sl)
        zs = [p.z for p in sl]
        print(f"XSLICE {k} |x|{x0:.2f} n{len(sl):4d} c({c.x:+.3f},{c.y:+.3f},{c.z:+.3f}) z[{min(zs):.2f},{max(zs):.2f}]")
