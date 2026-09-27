"""Fix iteration probe: key selected takes on the rig (nothing saved) and print per-frame metrics.

blender -b -P probe_take.py -- Take[,Take] [f0-f1]
metrics per frame: wrist fold L/R (deg), spike tip z L/R, tip speed (m/s), body min z, spike min z, wrist edge growth (cm)
"""
import json
import math
import os
import sys
from pathlib import Path

import bpy
import numpy as np

VD = Path(__file__).resolve().parent
A = VD.parents[1]
sys.path.insert(0, str(A))
import anim_apply as aa  # noqa: E402
import anim_eval as ae  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
names = args[0].split(",")
rng = [int(x) for x in args[1].split("-")] if len(args) > 1 else None
arm, mesh, sk = aa.open_rig()
T = aa.takes(sk)
rest = np.array([v.co[:] for v in mesh.data.vertices])
mL, mR = ae.rigid(mesh, "LeftHand"), ae.rigid(mesh, "RightHand")
spike = mL | mR
names_g = [g.name for g in mesh.vertex_groups]
E = np.array([e.vertices[:] for e in mesh.data.edges])
L0 = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
W = np.zeros((len(rest), len(names_g)))
for v in mesh.data.vertices:
    for g in v.groups:
        W[v.index, g.group] = g.weight
arm_e = (W[E[:, 0]][:, [names_g.index(b) for b in ("LeftHand", "RightHand", "LeftForeArm", "RightForeArm")]].sum(1) > 0.5)
tip = {}
for s, m in (("Left", mL), ("Right", mR)):
    head = np.array(arm.data.bones[s + "Hand"].head_local)
    ids = np.nonzero(m)[0]
    tip[s] = int(ids[np.argmax(np.linalg.norm(rest[ids] - head, axis=1))])


if os.environ.get("WRIST_FIX"):
    sys.path.insert(0, str(A.parent / "rig"))
    import fix_wrist_weights as fww  # noqa: E402
    fs, rs_ = (float(x) for x in os.environ["WRIST_FIX"].split(","))
    print("WRIST_FIX", json.dumps(fww.apply(arm, mesh, fs, rs_)), flush=True)


def ang(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(a, b) / np.linalg.norm(a) / np.linalg.norm(b))))))


out = {}
for name in names:
    act, rep = aa.key_action(arm, sk, name, T[name])
    ae.use_action(arm, act)
    n = T[name]["frames"]
    f0, f1 = rng if rng else (0, n)
    prev = None
    rows = {}
    for f in range(f0, f1 + 1):
        ae.goto(f)
        co = ae.mesh_co(mesh)
        fold, fa = [], []
        for s in ("Left", "Right"):
            el = np.array(arm.matrix_world @ arm.pose.bones[s + "ForeArm"].head)
            wr = np.array(arm.matrix_world @ arm.pose.bones[s + "Hand"].head)
            fold.append(round(ang(wr - el, co[tip[s]] - wr), 0))
            fa.append([round(float(x), 2) for x in (wr - el) / np.linalg.norm(wr - el)] + [round(float(x), 2) for x in wr])
        tz = [round(float(co[tip["Left"], 2]), 3), round(float(co[tip["Right"], 2]), 3)]
        sp = 0.0 if prev is None else max(float(np.linalg.norm(co[tip[s]] - prev[tip[s]])) * 30 for s in tip)
        L = np.linalg.norm(co[E[:, 0]] - co[E[:, 1]], axis=1)
        grow = float(((L - L0)[arm_e]).max()) * 100
        rows[f] = {"fold": fold, "tipz": tz, "tipv": round(sp, 1), "body_minz": round(float(co[~spike, 2].min()), 3),
                   "spike_minz": round(float(co[spike, 2].min()), 3), "wrist_grow_cm": round(grow, 1), "R_forearm_dir_wrist": fa[1]}
        prev = co
        print(name, f, json.dumps(rows[f]), flush=True)
    out[name] = {"rows": rows, "arm_short_max": rep["arm_short_max"], "leg_short_max": rep["leg_short_max"],
                 "short_frames": rep.get("short_frames", {})}
    print(name, "IK short arm/leg", round(rep["arm_short_max"], 3), round(rep["leg_short_max"], 3), rep.get("short_frames", {}))
(VD / "out" / f"probe_{'_'.join(names)}.json").write_text(json.dumps(out, indent=1))
