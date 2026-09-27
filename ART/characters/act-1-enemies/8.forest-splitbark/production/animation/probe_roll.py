"""Probe roll poses on the rig (nothing saved): metrics + a multi-view sheet per requested frame.

blender -b ../rig/ForestSplitter_Rig.blend -P probe_roll.py -- <out_dir> <Take|BALL> f1 f2 ... [--norender]
Metrics per frame: IK pull, skin min z, body-skin edges stretched > 2x, max edge growth (mm), ball bounds
and the lateral-axis spin radius about the bounds centre.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import render_setup as rs  # noqa: E402
import splitter_pose as sp  # noqa: E402
import takes_roll as tr  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
render = "--norender" not in args
args = [a for a in args if not a.startswith("--")]
out, take, frames = Path(args[0]), args[1], [float(a) for a in args[2:]] or [0.0]
out.mkdir(parents=True, exist_ok=True)
if take == "BALL":
    fn = tr.ball
else:
    from takes import TAKES
    fn = next(v for k, v in TAKES.items() if k.endswith(take))["fn"]

arm = bpy.data.objects[sp.ARM]
mesh = bpy.data.objects[sp.MESH]
me0 = mesh.data
E = np.empty(len(me0.edges) * 2, dtype=np.int64)
me0.edges.foreach_get("vertices", E)
E = E.reshape(-1, 2)
names = [g.name for g in mesh.vertex_groups]
dom = np.array([names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in me0.vertices])
shell = np.isin(dom, ["shell_L", "shell_R"])


def skin():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    m = ev.to_mesh()
    co = np.empty(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3)


sp.reset(arm)
for leg in sp.LEGS:
    for c in sp.constraints(arm, leg):
        c.influence = 0.0
bpy.context.view_layer.update()
rest = skin()
rest_len = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
body_edge = ~(shell[E[:, 0]] & shell[E[:, 1]])

if render:
    scene = rs.setup()
    rs.ground()
    scene.render.resolution_x = scene.render.resolution_y = 400
    cam = rs.camera("CAM_Probe")
    cam.data.lens = 50
    game = rs.game_camera(dist=3.0)
    views = {"front_low": ((0.0, -2.6, 0.35), (0.0, 0.0, 0.35)), "side": ((2.6, -0.2, 0.45), (0.0, 0.0, 0.35)),
             "back34_low": ((-1.7, 1.9, 0.5), (0.0, 0.0, 0.3)), "top": ((0.0, -0.4, 3.0), (0.0, -0.05, 0.3))}

rows = []
for f in frames:
    pose = fn(f)
    pulled = sp.apply(arm, pose)
    S = skin()
    L = np.linalg.norm(S[E[:, 0]] - S[E[:, 1]], axis=1)
    r = L / np.maximum(rest_len, 1e-6)
    grow = (L - rest_len)[body_edge]
    lo, hi = S.min(0), S.max(0)
    c = (lo + hi) / 2
    rad_yz = float(np.sqrt((S[:, 1] - c[1]) ** 2 + (S[:, 2] - c[2]) ** 2).max())
    lowest = {}
    for grp in ("head", "leg_front_L_foot", "leg_front_R_foot", "leg_hind_L_foot", "leg_hind_R_foot",
                "leg_front_L_lower", "leg_hind_L_lower"):
        m = dom == grp
        lowest[grp] = [round(float(x), 3) for x in S[m].min(0)] + [round(float(x), 3) for x in S[m].max(0)]
    gi = np.argsort(-(L - rest_len) * body_edge)[:5]
    worst = [[dom[E[i, 0]], dom[E[i, 1]], round(float(L[i] - rest_len[i]) * 1000)] for i in gi]
    row = {"frame": f, "pulled": {k: round(v, 4) for k, v in pulled.items()},
           "shell_min_z": round(float(S[shell, 2].min()), 4), "worst_edges": worst,
           "reach": {leg: round(sp.reach(arm, leg), 3) for leg in sp.LEGS},
           "skin_min_z": round(float(S[:, 2].min()), 4),
           "edges_over_2x": int((r[body_edge] > 2.0).sum()), "edges_under_half": int((r[body_edge] < 0.5).sum()),
           "edge_grow_max_mm": round(float(grow.max()) * 1000, 1),
           "shell_ratio": [round(float(r[~body_edge].min()), 4), round(float(r[~body_edge].max()), 4)],
           "bounds_min": [round(float(x), 3) for x in lo], "bounds_max": [round(float(x), 3) for x in hi],
           "spin_center_yz": [round(float(c[1]), 3), round(float(c[2]), 3)], "spin_radius_yz": round(rad_yz, 3),
           "parts_min_max": lowest}
    rows.append(row)
    print("PROBE", json.dumps(row), flush=True)
    if render:
        for name, (eye, tgt) in views.items():
            rs.look(cam, Vector(eye), Vector(tgt))
            scene.camera = cam
            scene.render.filepath = str(out / f"{take}_{f:05.2f}_{name}.png")
            bpy.ops.render.render(write_still=True)
        scene.camera = game
        scene.render.filepath = str(out / f"{take}_{f:05.2f}_game.png")
        bpy.ops.render.render(write_still=True)
(out / f"probe_{take}.json").write_text(json.dumps(rows, indent=1), encoding="utf-8")
print("PROBE_ROLL_DONE", flush=True)
