"""Animator-only IK helpers (non-deform, dropped by the deform-only FBX export), Wendigo style:
IK on the lower bone, chain 2, influence 0 by default; pole angle solved for zero rest drift."""
import math
import bpy
from mathutils import Vector
from rig_build_lib import activate

CHAINS = [  # (side, kind, upper, lower, pole name)
    (s, k, u.format(s=s), l.format(s=s), p.format(s=s))
    for s in ("L", "R")
    for k, u, l, p in (("hand", "{s}_arm_upper", "{s}_arm_lower", "CTRL_{s}_elbow"),
                       ("foot", "{s}_leg_upper", "{s}_leg_lower", "CTRL_{s}_knee"))
]


def add_ik_controls(arm_obj):
    arm = arm_obj.data
    activate(arm_obj)
    bpy.ops.object.mode_set(mode="EDIT")
    made = []
    for side, kind, upper, lower, pole_name in CHAINS:
        ub, lb = arm.edit_bones[upper], arm.edit_bones[lower]
        start, joint, finish = ub.head.copy(), lb.head.copy(), lb.tail.copy()
        axis = finish - start
        proj = start + axis * ((joint - start).dot(axis) / axis.length_squared)
        normal = joint - proj
        if normal.length < 0.01:
            normal = Vector((1 if side == "L" else -1, 0, 0))
        normal.normalize()
        tgt = arm.edit_bones.new(f"CTRL_{side}_{kind}")
        tgt.head, tgt.tail = finish, finish + Vector((0, 0, 0.12))
        tgt.parent = arm.edit_bones["root"]
        tgt.use_deform = False
        pole = arm.edit_bones.new(pole_name)
        pole.head = joint + normal * 0.5
        pole.tail = pole.head + Vector((0, 0, 0.12))
        pole.parent = arm.edit_bones["root"]
        pole.use_deform = False
        made.append((side, kind, upper, lower, tgt.name, pole.name))
    bpy.ops.object.mode_set(mode="OBJECT")

    report = {}
    for side, kind, upper, lower, tgt, pole in made:
        lp = arm_obj.pose.bones[lower]
        rest = lp.head.copy()
        rest_tail = lp.tail.copy()
        c = lp.constraints.new("IK")
        c.name = f"IK_{side}_{kind}"
        c.target, c.subtarget = arm_obj, tgt
        c.pole_target, c.pole_subtarget = arm_obj, pole
        c.chain_count = 2
        best = (float("inf"), 0.0)
        for step in range(72):
            c.pole_angle = math.radians(-180 + step * 5)
            bpy.context.view_layer.update()
            drift = (lp.head - rest).length + (lp.tail - rest_tail).length
            best = min(best, (drift, c.pole_angle))
        coarse = best[1]
        for step in range(-10, 11):  # refine to 0.5 deg around the coarse optimum
            c.pole_angle = coarse + math.radians(step * 0.5)
            bpy.context.view_layer.update()
            drift = (lp.head - rest).length + (lp.tail - rest_tail).length
            best = min(best, (drift, c.pole_angle))
        c.pole_angle = best[1]
        bpy.context.view_layer.update()
        report[c.name] = {"rest_drift_m_if_enabled": round(best[0], 5),
                          "pole_angle_deg": round(math.degrees(best[1]), 1)}
        c.influence = 0.0
    bpy.context.view_layer.update()
    return report
