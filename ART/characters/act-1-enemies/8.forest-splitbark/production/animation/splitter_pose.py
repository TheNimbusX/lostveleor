"""Apply a procedural pose dict to ARM_ForestSplitter (Blender space: front -Y, up +Z).

Pose dict (all optional):
  body/spine/neck/head: {"loc": (x, y, z) metres, "rot": (pitch, roll, yaw) degrees}
  shell_L/shell_R:      {"open": deg, "tilt": deg}   open = outward hinge, tilt = fore/aft rock
  legs: {"front_L": {"ik": 0..1, "foot": (dx, dy, dz), "pitch": deg}, ...}
Axes are armature axes carried by the parent (layered FK).
pitch +X = nose/toes down, roll +Y = top toward +X (character left), yaw +Z = nose toward +X.
IK feet: offsets from the rest ankle, CTRL_foot bones are parented to the (static) root.
"""
import math

import bpy
from mathutils import Quaternion, Vector

ARM = "ARM_ForestSplitter"
MESH = "SM_ForestSplitter_LOD0"
LEGS = ("front_L", "front_R", "hind_L", "hind_R")
FK = ("body", "spine", "neck", "head")
REACH_MAX = 0.975  # IK targets further than this share of the chain length get pulled in


def q(axis, deg):
    return Quaternion(Vector(axis), math.radians(deg))


def euler_q(pitch=0.0, roll=0.0, yaw=0.0):
    return q((0, 0, 1), yaw) @ q((0, 1, 0), roll) @ q((1, 0, 0), pitch)


def _rest(pb):
    return pb.bone.matrix_local.to_quaternion()


def set_rot(pb, qa):
    r = _rest(pb)
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = r.inverted() @ qa @ r


def set_loc(pb, v):
    pb.location = _rest(pb).inverted() @ Vector(v)


def constraints(arm_obj, leg):
    pre = "leg_" + leg
    lo = arm_obj.pose.bones[pre + "_lower"].constraints["IK_" + pre]
    ft = arm_obj.pose.bones[pre + "_foot"].constraints["FootRot_" + pre]
    return lo, ft


def chain_length(arm_obj, leg):
    b = arm_obj.data.bones
    return b["leg_%s_upper" % leg].length + b["leg_%s_lower" % leg].length


def reset(arm_obj):
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def apply(arm_obj, pose):
    """Set the pose, evaluate, pull unreachable IK targets in. Returns {leg: pulled metres}."""
    reset(arm_obj)
    pbs = arm_obj.pose.bones
    for name in FK:
        spec = pose.get(name)
        if not spec:
            continue
        if "rot" in spec:
            set_rot(pbs[name], euler_q(*spec["rot"]))
        if "loc" in spec:
            set_loc(pbs[name], spec["loc"])
    for side, s in (("L", 1), ("R", -1)):
        spec = pose.get("shell_" + side, {})
        qa = q((0, s, 0), spec.get("open", 0.0)) @ q((1, 0, 0), spec.get("tilt", 0.0))
        set_rot(pbs["shell_" + side], qa)
    for leg in LEGS:
        spec = pose.get("legs", {}).get(leg, {})
        ik = spec.get("ik", 1.0)
        for c in constraints(arm_obj, leg):
            c.influence = ik
        ctrl = pbs["CTRL_foot_" + leg]
        set_loc(ctrl, spec.get("foot", (0, 0, 0)))
        set_rot(ctrl, q((1, 0, 0), spec.get("pitch", 0.0)))
        for part in ("upper", "lower", "foot"):
            fk = spec.get("fk_" + part)
            if fk:
                set_rot(pbs["leg_%s_%s" % (leg, part)], euler_q(*fk))
    bpy.context.view_layer.update()
    pulled = {}
    for leg in LEGS:
        if pose.get("legs", {}).get(leg, {}).get("ik", 1.0) < 0.5:
            continue
        hip = pbs["leg_%s_upper" % leg].head.copy()
        ctrl = pbs["CTRL_foot_" + leg]
        tgt = ctrl.head.copy()
        lim = REACH_MAX * chain_length(arm_obj, leg)
        d = (tgt - hip).length
        if d > lim:
            new = hip + (tgt - hip) * (lim / d)
            rest_head = ctrl.bone.head_local
            set_loc(ctrl, new - rest_head)
            pulled[leg] = d - lim
    if pulled:
        bpy.context.view_layer.update()
    return pulled


def reach(arm_obj, leg):
    pbs = arm_obj.pose.bones
    hip = pbs["leg_%s_upper" % leg].head
    return (pbs["CTRL_foot_" + leg].head - hip).length / chain_length(arm_obj, leg)
