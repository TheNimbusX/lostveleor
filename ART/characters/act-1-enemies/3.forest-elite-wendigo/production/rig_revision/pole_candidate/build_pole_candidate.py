"""Isolated right-side IK pole correction on top of SkinCandidate.

Changes only two non-deforming control bones and their IK pole angles. All
deforming bone rest matrices, mesh positions, skin weights and triangles stay
identical to SkinCandidate. This is a candidate for measured comparison, not
an automatic replacement of the production rig.
"""

import json
import math
from pathlib import Path

import bpy


OUT = Path(__file__).resolve().parent
rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
mesh.data.calc_loop_triangles()
before = {b.name: b.matrix_local.copy() for b in rig.data.bones if b.use_deform}
before_vertices = [v.co.copy() for v in mesh.data.vertices]
triangles = len(mesh.data.loop_triangles)

bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")

targets = {
    "CTRL_R_elbow": (1.02, -.36, 1.23),
    "CTRL_R_knee": (1.13, -.82, .655),
}
for name, target in targets.items():
    bone = rig.data.edit_bones[name]
    length = bone.length
    bone.head = target
    bone.tail = (target[0], target[1], target[2] + length)

bpy.ops.object.mode_set(mode="POSE")
for b in rig.pose.bones:
    b.location = (0,0,0)
    b.rotation_mode = "QUATERNION"
    b.rotation_quaternion = (1,0,0,0)
    b.scale = (1,1,1)
    for c in b.constraints:
        if c.type == "IK":
            c.influence = 0
bpy.context.view_layer.update()

report = {"source":bpy.data.filepath,"output":None,"triangles":triangles,"poles":{},
          "deform_rest_change_max_m":None,"mesh_rest_change_max_m":None}
for lower_name, pole_name in (("R_arm_lower","CTRL_R_elbow"),
                              ("R_leg_lower","CTRL_R_knee")):
    pb = rig.pose.bones[lower_name]
    constraint = next(c for c in pb.constraints if c.type == "IK")
    rest_joint = rig.data.bones[lower_name].head_local.copy()
    constraint.influence = 1
    choice = None
    for degree in range(-180,180,5):
        constraint.pole_angle = math.radians(degree)
        bpy.context.view_layer.update()
        drift = (pb.head-rest_joint).length
        if choice is None or drift < choice[0]:
            choice = (drift,degree,tuple(pb.head))
    constraint.pole_angle = math.radians(choice[1])
    bpy.context.view_layer.update()
    report["poles"][pole_name] = {
        "new_head": targets[pole_name],
        "pole_angle_deg": choice[1],
        "neutral_joint_drift_m": round((pb.head-rest_joint).length,6),
        "neutral_joint_head": [round(v,6) for v in pb.head],
    }
    constraint.influence = 0

bpy.ops.object.mode_set(mode="OBJECT")
report["deform_rest_change_max_m"] = max(
    abs(rig.data.bones[name].matrix_local[row][col]-before[name][row][col])
    for name in before for row in range(4) for col in range(4))
report["mesh_rest_change_max_m"] = max((v.co-before_vertices[i]).length
                                    for i,v in enumerate(mesh.data.vertices))
if report["deform_rest_change_max_m"] > 1e-8 or report["mesh_rest_change_max_m"] > 1e-8:
    raise RuntimeError("A deform rest matrix or mesh vertex changed")
if triangles > 25000:
    raise RuntimeError("Triangle budget exceeded")

output = OUT / "ForestWendigo_Rig_PoleCandidate.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(output))
report["output"] = str(output)
(OUT / "build_report.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print("POLE_CANDIDATE",json.dumps(report),flush=True)
