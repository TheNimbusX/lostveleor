"""Deformation probes for ForestSplitter_Rig: render poses and measure edge stretch.

usage: blender -b ForestSplitter_Rig.blend -P deform_test.py -- <out_dir> [pose,pose...] [views] [scale]
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pose_lib  # noqa: E402
import render_util as ru  # noqa: E402
import rig_spec as spec  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
out = Path(args[0])
out.mkdir(parents=True, exist_ok=True)
poses = args[1].split(",") if len(args) > 1 else ["rest", "combined", "shells_15", "shells_25", "head_lunge", "hind_lift", "walk_twist"]
views = args[2].split(",") if len(args) > 2 else ["front", "game", "left", "back34", "top"]
scale = float(args[3]) if len(args) > 3 else 1.0

arm = bpy.data.objects[spec.ARM_NAME]
mesh = bpy.data.objects[spec.MESH_NAME]
arm.scale = (scale,) * 3
labels = np.zeros(len(mesh.data.vertices), np.int32)
mesh.data.attributes["rig_shell_label"].data.foreach_get("value", labels)
edges = np.array([e.vertices[:] for e in mesh.data.edges])


def deformed_coords():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3)


pose_lib.reset(arm)
bpy.context.view_layer.update()
rest = deformed_coords()
rest_len = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
shell_edge = (labels[edges[:, 0]] > 0) & (labels[edges[:, 1]] == labels[edges[:, 0]])

scene = ru.setup_scene("WORKBENCH", 900)
metrics = {}
for pose in poses:
    pose_lib.apply(arm, pose)
    co = deformed_coords()
    ln = np.linalg.norm(co[edges[:, 0]] - co[edges[:, 1]], axis=1)
    ratio = ln / np.maximum(rest_len, 1e-9)
    body = ~shell_edge
    metrics[pose] = {
        "shell_edge_ratio_range": [round(float(ratio[shell_edge].min()), 4), round(float(ratio[shell_edge].max()), 4)],
        "body_edge_ratio_p01_p99": [round(float(np.percentile(ratio[body], 1)), 3), round(float(np.percentile(ratio[body], 99)), 3)],
        "body_edge_ratio_min_max": [round(float(ratio[body].min()), 3), round(float(ratio[body].max()), 3)],
        "edges_stretched_over_2x": int((ratio > 2.0).sum()),
        "edges_squashed_under_0.5x": int((ratio < 0.5).sum()),
        "min_z_m": round(float(co[:, 2].min()), 4),
    }
    for v in views:
        eye, tgt = ru.VIEWS[v]
        eye = tuple(c * scale for c in eye)
        tgt = tuple(c * scale for c in tgt)
        ortho = 1.95 * scale if v in ("front", "back", "left", "right", "top", "below") else None
        ru.shot(out / f"{pose}_{v}.png", eye, tgt, ortho=ortho, lens=85 if "close" in v or "side" in v else 62)
pose_lib.reset(arm)
arm.scale = (1, 1, 1)
(out / "deform_metrics.json").write_text(json.dumps({"scale": scale, "poses": metrics}, indent=2), encoding="utf-8")
print("DEFORM_DONE", json.dumps(metrics), flush=True)
