"""Quick pose preview for authoring (no actions written).

blender -b -P preview.py -- <Take> <out_dir> <f1,f2,...> [views=game,side] [res=360]
Prints IK shortfalls, spike tip positions and evaluated mesh min z per frame.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import anim_apply as aa  # noqa: E402
import review_scene as rs  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
take_name, out = args[0], Path(args[1])
frames = [float(x) for x in args[2].split(",")]
views = args[3].split(",") if len(args) > 3 else ["game", "side"]
res = int(args[4]) if len(args) > 4 else 360
out.mkdir(parents=True, exist_ok=True)
arm, mesh, sk = aa.open_rig()
take = aa.takes(sk)[take_name]
scene, cam, fl = rs.setup(res)
arm.animation_data_clear()
rows = []
gn = [g.name for g in mesh.vertex_groups]
dom = np.array([gn[max(v.groups, key=lambda g: g.weight).group] for v in mesh.data.vertices])
for f in frames:
    pose, info = aa.solve(sk, take, f)
    for bn, m in pose.basis().items():
        arm.pose.bones[bn].matrix_basis = m
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    row = {"f": f, "minz": round(float(co[:, 2].min()), 3), "maxz": round(float(co[:, 2].max()), 3),
           "ymin": round(float(co[:, 1].min()), 2), "ymax": round(float(co[:, 1].max()), 2),
           "arm_short": [round(info["L_arm_short"], 3), round(info["R_arm_short"], 3)],
           "leg_short": [round(info["L_leg_short"], 3), round(info["R_leg_short"], 3)],
           "tipL": [round(x, 3) for x in info["L_tip"]], "tipR": [round(x, 3) for x in info["R_tip"]]}
    below = co[:, 2] < -0.01
    if below.any():
        u, c = np.unique(dom[below], return_counts=True)
        row["below"] = {str(k): [int(n), round(float(co[below & (dom == k), 2].min()), 3)] for k, n in zip(u, c)}
    lows = {}
    for k in ("Hips", "Spine01", "Spine", "Head", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg"):
        sel = dom == k
        lows[k] = round(float(co[sel, 2].min()), 2)
    row["low_by_bone"] = lows
    rows.append(row)
    print("PREVIEW", json.dumps(row), flush=True)
    for v in views:
        rs.place(cam, v)
        scene.render.filepath = str(out / f"{take_name}_{v}_{int(f):03d}.png")
        bpy.ops.render.render(write_still=True)
(out / f"{take_name}_preview.json").write_text(json.dumps(rows, indent=1))
