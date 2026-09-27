"""Render deformation test poses (front + side) and measure edge stretch per pose.

usage: blender -b -P pose_test.py -- <rig.blend> <out_dir> [texture|weights]
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_common as rc  # noqa: E402
import rig_poses as rp  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
src, out = Path(args[0]).resolve(), Path(args[1]).resolve()
mode = args[2] if len(args) > 2 else "texture"
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
arm, mesh = rc.find_rig()
names = [g.name for g in mesh.vertex_groups]

me = mesh.data
ev = np.empty(len(me.edges) * 2, dtype=np.int64)
me.edges.foreach_get("vertices", ev)
ev = ev.reshape(-1, 2)
rest = np.empty(len(me.vertices) * 3)
me.vertices.foreach_get("co", rest)
rest = rest.reshape(-1, 3)
rest_len = np.linalg.norm(rest[ev[:, 0]] - rest[ev[:, 1]], axis=1)
dom = np.zeros(len(me.vertices), dtype=np.int64)
for v in me.vertices:
    if v.groups:
        g = max(v.groups, key=lambda g: g.weight)
        dom[v.index] = g.group
spike = np.array([any(names[g.group].endswith("Hand") and g.weight > 0.999 for g in v.groups) for v in me.vertices])

scene, cam = rc.setup_scene(760)
if mode == "texture":
    scene.display.shading.color_type = "TEXTURE"
else:
    scene.display.shading.color_type = "VERTEX"
report = {}
for pose in rp.POSES:
    rp.apply(arm, pose)
    dg = bpy.context.evaluated_depsgraph_get()
    ev_mesh = mesh.evaluated_get(dg).to_mesh()
    pco = np.empty(len(ev_mesh.vertices) * 3)
    ev_mesh.vertices.foreach_get("co", pco)
    pco = (np.array(mesh.matrix_world)[:3, :3] @ pco.reshape(-1, 3).T).T + np.array(mesh.matrix_world)[:3, 3]
    mesh.evaluated_get(dg).to_mesh_clear()
    ln = np.linalg.norm(pco[ev[:, 0]] - pco[ev[:, 1]], axis=1)
    ratio = ln / np.maximum(rest_len, 1e-6)
    worst = np.argsort(-np.abs(np.log(np.maximum(ratio, 1e-6))))[:200]
    by_bone = {}
    for e in worst:
        b = names[dom[ev[e, 0]]]
        by_bone[b] = by_bone.get(b, 0) + 1
    sp_edges = spike[ev[:, 0]] & spike[ev[:, 1]]
    report[pose] = {
        "edge_ratio_p001": round(float(np.percentile(ratio, 0.1)), 3),
        "edge_ratio_p999": round(float(np.percentile(ratio, 99.9)), 3),
        "edge_ratio_max": round(float(ratio.max()), 3),
        "worst200_by_bone": dict(sorted(by_bone.items(), key=lambda kv: -kv[1])[:6]),
        "rigid_spike_edge_ratio_range": [round(float(ratio[sp_edges].min()), 4), round(float(ratio[sp_edges].max()), 4)],
        "min_z": round(float(pco[:, 2].min()), 3),
    }
    print("POSE", pose, json.dumps(report[pose]), flush=True)
    for view in ("front", "side"):
        rc.place_camera(cam, view, target=(0, 0, 1.45), ortho=3.4)
        scene.render.filepath = str(out / f"{pose}_{view}.png")
        bpy.ops.render.render(write_still=True)
rp.clear(arm)
(out / "pose_stretch.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
