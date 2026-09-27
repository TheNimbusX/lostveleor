"""Animator-only IK helpers for the four legs (non-deform, left out of the deform-only FBX).

FK stays the default: IK (lower leg, chain 2) and FootRot (foot copies the control's rotation)
are created with influence 0 so bake scripts can key the deform bones directly, or key both
influences to 1 to plant a foot (Wendigo convention). CTRL_foot_* sits at the ankle in the foot's
own rest frame; CTRL_pole_* sits 0.45 m in front of the elbow/knee plane.
"""
import math

import bpy
from mathutils import Vector

LEGS = [(k, s) for k in ("front", "hind") for s in ("L", "R")]


def add(arm_obj):
    arm = arm_obj.data
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    made = []
    for kind, side in LEGS:
        pre = f"leg_{kind}_{side}_"
        up, lo, ft = (arm.edit_bones[pre + n] for n in ("upper", "lower", "foot"))
        tgt = arm.edit_bones.new(f"CTRL_foot_{kind}_{side}")
        tgt.head = lo.tail.copy()          # ankle = IK target
        tgt.tail = ft.tail.copy()          # same frame as the foot so FootRot is identity at rest
        tgt.roll = ft.roll
        tgt.parent = arm.edit_bones["root"]
        tgt.use_deform = False
        start, joint, end = up.head.copy(), lo.head.copy(), lo.tail.copy()
        axis = end - start
        proj = start + axis * ((joint - start).dot(axis) / axis.length_squared)
        normal = joint - proj
        if normal.length < 1e-3:
            normal = Vector((0, -1 if kind == "front" else 1, 0))
        normal.normalize()
        pole = arm.edit_bones.new(f"CTRL_pole_{kind}_{side}")
        pole.head = joint + normal * 0.45
        pole.tail = pole.head + Vector((0, 0, 0.1))
        pole.parent = arm.edit_bones["root"]
        pole.use_deform = False
        made.append((pre, tgt.name, pole.name))
    bpy.ops.object.mode_set(mode="OBJECT")

    deform = arm.collections.new("DEF")
    ctrl = arm.collections.new("CTRL")
    for b in arm.bones:
        (ctrl if b.name.startswith("CTRL_") else deform).assign(b)

    report = {}
    for pre, tgt, pole in made:
        lower = arm_obj.pose.bones[pre + "lower"]
        rest_joint = lower.head.copy()
        c = lower.constraints.new("IK")
        c.name = "IK_" + pre.rstrip("_")
        c.target, c.subtarget = arm_obj, tgt
        c.pole_target, c.pole_subtarget = arm_obj, pole
        c.chain_count = 2
        best = (float("inf"), 0.0)
        for step in range(72):
            c.pole_angle = math.radians(-180 + step * 5)
            bpy.context.view_layer.update()
            drift = (lower.head - rest_joint).length
            if drift < best[0]:
                best = (drift, c.pole_angle)
        c.pole_angle = best[1]
        c.influence = 0.0
        foot = arm_obj.pose.bones[pre + "foot"]
        fr = foot.constraints.new("COPY_ROTATION")
        fr.name = "FootRot_" + pre.rstrip("_")
        fr.target, fr.subtarget = arm_obj, tgt
        fr.influence = 0.0
        report[c.name] = {"pole_angle_deg": round(math.degrees(best[1]), 1),
                          "rest_joint_drift_m_if_enabled": round(best[0], 5)}
    bpy.context.view_layer.update()
    return report
