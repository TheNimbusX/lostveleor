"""Pose helpers for rig probes: rotations given in armature space about each bone's head."""
import math

from mathutils import Quaternion, Vector

# (bone, world_axis, degrees) applied in order; offsets are armature-space metres.
POSES = {
    "rest": {"rot": [], "loc": []},
    "combined": {  # the requested deformation test: shells ~25 deg, head thrust, front-left paw lifted
        "rot": [("shell_L", (0, 1, 0), 25), ("shell_R", (0, -1, 0), 25),
                ("head", (1, 0, 0), -12), ("neck", (1, 0, 0), 6)],
        "loc": [("neck", (0, -0.12, 0.03)), ("CTRL_foot_front_L", (0, -0.10, 0.16))],
        "ik": ["leg_front_L"],
    },
    "shells_25": {"rot": [("shell_L", (0, 1, 0), 25), ("shell_R", (0, -1, 0), 25)], "loc": []},
    "shells_15": {"rot": [("shell_L", (0, 1, 0), 15), ("shell_R", (0, -1, 0), 15)], "loc": []},
    "shells_45": {"rot": [("shell_L", (0, 1, 0), 45), ("shell_R", (0, -1, 0), 45)], "loc": []},
    "shells_clap": {"rot": [("shell_L", (0, 1, 0), -6), ("shell_R", (0, -1, 0), -6)], "loc": []},
    "head_lunge": {"rot": [("head", (1, 0, 0), -15), ("neck", (1, 0, 0), 8), ("spine", (1, 0, 0), 6)],
                   "loc": [("neck", (0, -0.14, 0.03))]},
    "ik_steps": {"rot": [], "loc": [("CTRL_foot_front_L", (0, -0.12, 0.14)), ("CTRL_foot_hind_R", (0, 0.10, 0.12))],
                 "ik": ["leg_front_L", "leg_hind_R"]},
    "walk_twist": {"rot": [("body", (0, 0, 1), 8), ("spine", (0, 0, 1), -10), ("spine", (0, 1, 0), 5),
                           ("leg_front_R_upper", (1, 0, 0), 25), ("leg_hind_L_upper", (1, 0, 0), -25)], "loc": []},
    "hind_lift": {"rot": [("leg_hind_R_upper", (1, 0, 0), 35), ("leg_hind_R_lower", (1, 0, 0), -40),
                          ("body", (0, 1, 0), -6)], "loc": []},
}


def reset(arm_obj):
    for pb in arm_obj.pose.bones:
        for c in pb.constraints:
            c.influence = 0.0
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def apply(arm_obj, pose_name):
    import bpy
    reset(arm_obj)
    pose = POSES[pose_name]
    for name, axis, deg in pose["rot"]:
        pb = arm_obj.pose.bones[name]
        rest = pb.bone.matrix_local.to_quaternion()
        q = Quaternion(Vector(axis), math.radians(deg))
        pb.rotation_quaternion = pb.rotation_quaternion @ (rest.inverted() @ q @ rest)
    for name, off in pose["loc"]:
        pb = arm_obj.pose.bones[name]
        rest = pb.bone.matrix_local.to_quaternion()
        pb.location = rest.inverted() @ Vector(off)
    for leg in pose.get("ik", []):
        for pb in arm_obj.pose.bones:
            for c in pb.constraints:
                if c.name in ("IK_" + leg, "FootRot_" + leg):
                    c.influence = 1.0
    bpy.context.view_layer.update()
