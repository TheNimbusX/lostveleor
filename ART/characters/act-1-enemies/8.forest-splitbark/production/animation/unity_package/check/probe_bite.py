"""Probe bite-contact variants on the rig (nothing saved): head/shell stretch vs beak reach.

blender -b ../../../rig/ForestSplitter_Rig.blend -P probe_bite.py -- k_neck,body_extra,head_pitch ...
k_neck scales the neck translation track; body_extra (m, forward) and head_pitch (deg, - = nose up)
are added following the lunge envelope (0 at rest, 1 at contact).
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

ANIM = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ANIM))
import splitter_pose as sp  # noqa: E402
import takes_bite as tb  # noqa: E402

arm = bpy.data.objects[sp.ARM]
mesh = bpy.data.objects[sp.MESH]
names = [g.name for g in mesh.vertex_groups]
dom = []
for v in mesh.data.vertices:
    ws = [(g.weight, names[g.group]) for g in v.groups]
    dom.append(max(ws)[1] if ws else "")
dom = np.array(dom)
E = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
mesh.data.edges.foreach_get("vertices", E)
E = E.reshape(-1, 2)
head = np.isin(dom, ("head", "neck"))
shell = np.isin(dom, ("shell_L", "shell_R"))
hs = (head[E[:, 0]] & shell[E[:, 1]]) | (head[E[:, 1]] & shell[E[:, 0]])
hidx = np.where(dom == "head")[0]


def skin():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3) @ np.array(mesh.matrix_world)[:3, :3].T


sp.apply(arm, {})
rest = skin()
L0 = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
peak = max(-tb._neck_loc(t)[1] for t in np.arange(0, 30.01, 0.25))


def variant(k, body_extra, head_pitch):
    def pose(t):
        p = tb.bite(t)
        env = max(0.0, -tb._neck_loc(t)[1]) / peak
        x, y, z = p["neck"]["loc"]
        p["neck"]["loc"] = (x, y * k, z)
        bx, by, bz = p["body"]["loc"]
        p["body"]["loc"] = (bx, by - body_extra * env, bz)
        hr = p["head"]["rot"]
        p["head"]["rot"] = (hr[0] + head_pitch * env, hr[1], hr[2])
        return p
    return pose


out = {}
for arg in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["1,0,0"]):
    k, be, hp = (float(x) for x in arg.split(","))
    fn = variant(k, be, hp)
    rows = []
    for t in np.arange(12, 26.01, 1.0):
        pulled = sp.apply(arm, fn(t))
        S = skin()
        g = np.linalg.norm(S[E[:, 0]] - S[E[:, 1]], axis=1) - L0
        rows.append({"t": float(t), "hs_grow_mm": round(float(g[hs].max()) * 1000, 1),
                     "all_grow_mm": round(float(g.max()) * 1000, 1), "n_over_5cm": int((g > 0.05).sum()),
                     "beak_fwd": round(float(-S[hidx, 1].min()), 3), "pulled": {k2: round(v, 4) for k2, v in pulled.items()},
                     "reach": round(max(sp.reach(arm, leg) for leg in sp.LEGS), 3)})
    worst = max(rows, key=lambda r: r["hs_grow_mm"])
    fw = max(rows, key=lambda r: r["beak_fwd"])
    out[arg] = {"worst_hs_grow_mm": worst["hs_grow_mm"], "at": worst["t"],
                "max_all_grow_mm": max(r["all_grow_mm"] for r in rows),
                "max_over_5cm": max(r["n_over_5cm"] for r in rows),
                "beak_fwd_at_18": [r["beak_fwd"] for r in rows if r["t"] == 18][0], "beak_fwd_max_at": fw["t"],
                "pulled": [r["t"] for r in rows if r["pulled"]], "max_reach": max(r["reach"] for r in rows)}
    print("VARIANT", arg, json.dumps(out[arg]), flush=True)
print("PROBE_DONE", flush=True)
