"""Probe the Thorncaster rig for animation authoring: objects, bone lengths, foot contact points.

blender -b ../rig/ForestThorncaster_Rig.blend -P probe_rig.py
Writes probe_rig.json next to this script.
"""
import json
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
arm = bpy.data.objects["ARM_ForestThorncaster"]
mesh = bpy.data.objects["SM_ForestThorncaster_LOD0"]
out = {"objects": [(o.name, o.type, o.parent.name if o.parent else None) for o in bpy.data.objects],
       "actions": [a.name for a in bpy.data.actions], "scene_fps": bpy.context.scene.render.fps}
out["bones"] = {b.name: {"len": b.length, "head": list(b.head_local), "tail": list(b.tail_local),
                         "x_axis": list(b.matrix_local.col[0][:3]), "y_axis": list(b.matrix_local.col[1][:3]),
                         "parent": b.parent.name if b.parent else None, "connected": b.use_connect}
                for b in arm.data.bones}
names = [g.name for g in mesh.vertex_groups]
co = np.array([v.co[:] for v in mesh.data.vertices])
w = np.zeros((len(co), len(names)))
for v in mesh.data.vertices:
    for g in v.groups:
        w[v.index, g.group] = g.weight
out["mesh_min"] = co.min(0).tolist()
out["mesh_max"] = co.max(0).tolist()
feet = {}
for side in ("Left", "Right"):
    idx = [names.index(side + "Foot"), names.index(side + "ToeBase")]
    sel = w[:, idx].sum(1) > 0.5
    low = sel & (co[:, 2] < 0.03)
    pts = co[low]
    feet[side] = {"n_foot": int(sel.sum()), "n_contact": int(low.sum()),
                  "contact_min": pts.min(0).tolist(), "contact_max": pts.max(0).tolist(),
                  "contact_mean": pts.mean(0).tolist(),
                  "toe_contact_front": pts[pts[:, 1].argmin()].tolist(),
                  "foot_bbox_min": co[sel].min(0).tolist(), "foot_bbox_max": co[sel].max(0).tolist()}
out["feet"] = feet
spk = {}
for side in ("Left", "Right"):
    i = names.index(side + "Hand")
    sel = w[:, i] > 0.999
    pts = co[sel]
    tail = np.array(arm.data.bones[side + "Hand"].tail_local)
    spk[side] = {"n": int(sel.sum()), "farthest_from_wrist": pts[np.linalg.norm(pts - np.array(arm.data.bones[side + "Hand"].head_local), axis=1).argmax()].tolist(),
                 "tail": tail.tolist()}
out["spikes"] = spk
# body vertex extents per dominant bone (for ground checks in death)
dom = w.argmax(1)
out["extent_by_bone"] = {names[j]: [co[dom == j].min(0).tolist(), co[dom == j].max(0).tolist()] for j in range(len(names)) if (dom == j).any()}
out["materials"] = [m.name for m in mesh.data.materials]
out["images"] = [(i.name, i.filepath, list(i.size)) for i in bpy.data.images]
out["modifiers"] = [(m.name, m.type) for m in mesh.modifiers]
(HERE / "probe_rig.json").write_text(json.dumps(out, indent=1))
print("PROBE_OK")
