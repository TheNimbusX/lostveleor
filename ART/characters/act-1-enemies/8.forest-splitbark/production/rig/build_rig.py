"""Build ARM_ForestSplitter on the approved 24k candidate and skin it.

usage: blender -b -P build_rig.py -- <candidate.blend> <out_dir>
Writes <out_dir>/ForestSplitter_Rig.blend and rig_build.json (weights/labels stats).
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_spec as spec  # noqa: E402
import weights as wts  # noqa: E402
import ik_controls  # noqa: E402
import proxy_heat  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
source, out_dir = Path(args[0]).resolve(), Path(args[1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
scene.render.fps = 30
mesh_obj = bpy.data.objects[spec.MESH_NAME]
mesh_obj.data.name = spec.MESH_NAME

arm_data = bpy.data.armatures.new(spec.ARM_NAME)
arm_obj = bpy.data.objects.new(spec.ARM_NAME, arm_data)
scene.collection.objects.link(arm_obj)
arm_obj.show_in_front = True
arm_data.display_type = "OCTAHEDRAL"
bpy.ops.object.select_all(action="DESELECT")
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
for name, head, tail, parent, connect, zaxis in spec.all_bones():
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.align_roll(Vector(zaxis))
    b.use_deform = True
    if parent:
        b.parent = arm_data.edit_bones[parent]
        b.use_connect = connect
bpy.ops.object.mode_set(mode="OBJECT")
names = [b.name for b in arm_data.bones]

# Heat weights among the body chains only; shells get explicit weights afterwards.
HEAT_EXCLUDED = ("root", "shell_L", "shell_R")
for n in HEAT_EXCLUDED:
    arm_data.bones[n].use_deform = False
heat, heat_info = proxy_heat.heat_matrix(mesh_obj, arm_obj, names)
for n in HEAT_EXCLUDED:
    arm_data.bones[n].use_deform = True
heat_unweighted = int((heat.sum(1) <= 1e-9).sum())
mesh_obj.parent = arm_obj
mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
mod.object = arm_obj
me = mesh_obj.data

co = np.array([v.co[:] for v in me.vertices], dtype=float)
me.calc_loop_triangles()
tris = np.array([t.vertices[:] for t in me.loop_triangles], dtype=np.int64)
W, labels, stats = wts.final_weights(co, tris, heat, names)

for g in list(mesh_obj.vertex_groups):
    mesh_obj.vertex_groups.remove(g)
groups = {n: mesh_obj.vertex_groups.new(name=n) for n in names if arm_data.bones[n].use_deform}
rows, cols_ = np.nonzero(W > 0)
for r, c in zip(rows.tolist(), cols_.tolist()):
    groups[names[c]].add([r], float(W[r, c]), "REPLACE")

attr = me.attributes.get("rig_shell_label") or me.attributes.new("rig_shell_label", "INT", "POINT")
attr.data.foreach_set("value", labels.astype(np.int32))

ik_report = ik_controls.add(arm_obj)

arm_obj["rig_version"] = "r01"
arm_obj["front_axis"] = "-Y"
blend = out_dir / "ForestSplitter_Rig.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
report = {
    "source": str(source),
    "blend": str(blend),
    "bones": names,
    "heat_unweighted_vertices": heat_unweighted,
    "heat_proxy": heat_info,
    "weights": stats,
    "ik": ik_report,
}
(out_dir / "rig_build.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("RIG_BUILT", json.dumps(stats), "heat_unweighted", heat_unweighted, flush=True)
