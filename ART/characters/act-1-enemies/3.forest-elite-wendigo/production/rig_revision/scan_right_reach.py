"""Read-only scan of right-arm reach in a Higgsfield-like planted crouch."""

import json
import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


rig = bpy.data.objects["ARM_ForestWendigo"]
for b in rig.pose.bones:
    b.rotation_mode = "QUATERNION"
    b.location = (0, 0, 0)
    b.rotation_quaternion = (1, 0, 0, 0)
    for constraint in b.constraints:
        if constraint.type == "IK":
            constraint.influence = 0


def move(name, delta):
    b = rig.pose.bones[name]
    b.location = b.bone.matrix_local.to_3x3().inverted() @ Vector(delta)


def rotate(name, axis, degrees):
    b = rig.pose.bones[name]
    basis = b.bone.matrix_local.to_3x3()
    rot = Quaternion(Vector(axis), math.radians(degrees))
    b.rotation_quaternion = b.rotation_quaternion @ (basis.inverted() @ rot.to_matrix() @ basis).to_quaternion()


move("pelvis", (0,.16,-.54))
rotate("pelvis", (1,0,0), -9)
rotate("spine_01", (1,0,0), -19)
rotate("spine_02", (1,0,0), -23)
move("CTRL_R_hand", (.04,1.20,-.79))
rig.pose.bones["R_arm_lower"].constraints["IK_R_hand_Plant"].influence = 1
base = rig.pose.bones["R_clavicle"].rotation_quaternion.copy()
rows = []
for yaw in range(-90, 136, 15):
    for pitch in range(-45, 46, 15):
        rig.pose.bones["R_clavicle"].rotation_quaternion = base
        rotate("R_clavicle", (0,0,1), yaw)
        rotate("R_clavicle", (1,0,0), pitch)
        bpy.context.view_layer.update()
        hand = rig.pose.bones["R_hand"].head.copy()
        target = rig.pose.bones["CTRL_R_hand"].head.copy()
        rows.append({"yaw":yaw,"pitch":pitch,"error":round((hand-target).length,4),
                     "hand":[round(v,3) for v in hand],"target":[round(v,3) for v in target]})
rows.sort(key=lambda row: row["error"])
out = Path(__file__).resolve().parent / "right_reach_scan.json"
out.write_text(json.dumps({"best":rows[:20],"worst":rows[-3:]},indent=2),encoding="utf-8")
print(json.dumps(rows[:20]),flush=True)
