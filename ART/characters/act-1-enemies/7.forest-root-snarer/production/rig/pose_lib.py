"""FK posing helpers for the deformation test (armature-space deltas conjugated into bone space)."""
import math
import bpy
from mathutils import Euler, Quaternion, Vector


def reset(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion((1, 0, 0, 0))
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def _apply_world_delta(arm, name, qd):
    """Rotate bone `name` by armature-space quaternion qd about its head, on top of its current pose."""
    pb = arm.pose.bones[name]
    cur = pb.matrix.copy()  # armature space, includes parents
    rot = qd.to_matrix().to_4x4()
    head = cur.translation.copy()
    new = rot @ cur
    new.translation = head
    pb.matrix = new
    bpy.context.view_layer.update()


def rotate(arm, name, euler_deg, order="XYZ"):
    q = Euler(tuple(math.radians(a) for a in euler_deg), order).to_quaternion()
    _apply_world_delta(arm, name, q)


def aim(arm, name, direction):
    """Rotate so the bone's head->tail points along `direction` (armature space), minimal twist."""
    pb = arm.pose.bones[name]
    cur = (pb.tail - pb.head).normalized()
    tgt = Vector(direction).normalized()
    _apply_world_delta(arm, name, cur.rotation_difference(tgt))


def move(arm, name, offset):
    pb = arm.pose.bones[name]
    m = pb.matrix.copy()
    m.translation = m.translation + Vector(offset)
    pb.matrix = m
    bpy.context.view_layer.update()


# ---- test poses -------------------------------------------------------------
def pose_rest(arm):
    pass


def pose_arms_overhead(arm):
    """Slam wind-up like the ref (0.67-1.5 s): reared up on the hind legs, both slabs overhead."""
    move(arm, "pelvis", (0, 0.05, 0.12))
    rotate(arm, "pelvis", (-50, 0, 0))
    rotate(arm, "spine_01", (-12, 0, 0))
    rotate(arm, "spine_02", (-10, 0, 0))
    rotate(arm, "neck", (25, 0, 0))
    rotate(arm, "head", (20, 0, 0))
    for s, sg in (("L", 1), ("R", -1)):
        rotate(arm, f"{s}_clavicle", (0, sg * -20, 0))
        aim(arm, f"{s}_arm_upper", (sg * 0.55, 0.05, 0.85))
        aim(arm, f"{s}_arm_lower", (sg * 0.15, 0.10, 1.0))
        # legs stay under the body: thighs down, shins down, feet flat-ish
        aim(arm, f"{s}_leg_upper", (sg * 0.35, -0.25, -1.0))
        aim(arm, f"{s}_leg_lower", (sg * 0.15, 0.25, -1.0))
        aim(arm, f"{s}_foot", (sg * 0.2, -1.0, -0.25))


def pose_arms_slammed(arm):
    """Slam contact like the ref (1.67 s): body dropped, slabs driven into the ground out front."""
    move(arm, "pelvis", (0, 0, -0.10))
    rotate(arm, "pelvis", (12, 0, 0))
    rotate(arm, "spine_02", (8, 0, 0))
    rotate(arm, "head", (-12, 0, 0))
    for s, sg in (("L", 1), ("R", -1)):
        rotate(arm, f"{s}_clavicle", (0, sg * 10, 0))
        aim(arm, f"{s}_arm_upper", (sg * 0.95, -0.25, -0.2))
        aim(arm, f"{s}_arm_lower", (sg * 0.72, -0.55, -0.36))


def pose_hind_leg_lift(arm):
    """Left (+X) hind leg: knee swung forward/up (walk-cycle passing pose, exaggerated)."""
    rotate(arm, "L_leg_upper", (-60, 0, 0))
    rotate(arm, "L_leg_lower", (45, 0, 0))
    rotate(arm, "L_foot", (-20, 0, 0))


def pose_jaw_open(arm):
    """Roar: head up a bit, chin dropped 12 deg (jaw is a chin-drop only, closed mesh)."""
    rotate(arm, "head", (-10, 0, 0))
    rotate(arm, "jaw", (12, 0, 0))


def pose_ik_plant(arm):
    """IK check: all four IK helpers on at rest targets, body shoved 0.15 m forward and 0.06 m down."""
    for c in (pb.constraints.get(n) for pb in arm.pose.bones for n in ("IK_L_hand", "IK_R_hand", "IK_L_foot", "IK_R_foot")):
        if c:
            c.influence = 1.0
    move(arm, "pelvis", (0, -0.15, -0.06))
    rotate(arm, "pelvis", (6, 0, 0))


def ik_error(arm):
    out = {}
    for side in ("L", "R"):
        for kind, lower in (("hand", f"{side}_arm_lower"), ("foot", f"{side}_leg_lower")):
            t = arm.pose.bones[f"CTRL_{side}_{kind}"]
            out[f"{side}_{kind}"] = round((arm.pose.bones[lower].tail - t.head).length, 4)
    return out


def ik_off(arm):
    for pb in arm.pose.bones:
        for c in pb.constraints:
            if c.type == "IK":
                c.influence = 0.0
    bpy.context.view_layer.update()


POSES = {"rest": pose_rest, "jaw_open": pose_jaw_open, "ik_plant": pose_ik_plant, "arms_overhead": pose_arms_overhead,
         "arms_slammed": pose_arms_slammed, "hind_leg_lift": pose_hind_leg_lift}
