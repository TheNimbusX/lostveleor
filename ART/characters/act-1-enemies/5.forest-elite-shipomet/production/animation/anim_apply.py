"""Blender-side glue: open the rig, list the takes, key them as actions on the deform bones."""
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from anim_pose import build, defaults  # noqa: E402
from anim_rig import ARM, MESH, Skeleton  # noqa: E402
from takes_attack import burst_take, linecast_take, shot_take  # noqa: E402
from takes_loco import idle_take, walk_take  # noqa: E402
from takes_react import death_take, hit_take  # noqa: E402

RIG_BLEND = HERE.parent / "rig" / "ForestThorncaster_Rig.blend"
PREFIX = "ForestThorncaster_"
ORDER = ["Idle", "Walk", "LineCast", "Burst", "Shot", "Hit", "Death"]


def open_rig(path=RIG_BLEND):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    bpy.context.preferences.filepaths.save_version = 0
    arm, mesh = bpy.data.objects[ARM], bpy.data.objects[MESH]
    sk = Skeleton(arm)
    sk.contact_front = front_contacts(mesh)
    return arm, mesh, sk


def front_contacts(mesh):
    """Front-most claw vertex on the ground per foot (rest pose), z snapped to 0."""
    names = [g.name for g in mesh.vertex_groups]
    out = {}
    for side in ("Left", "Right"):
        ids = {names.index(side + "Foot"), names.index(side + "ToeBase")}
        best = None
        for v in mesh.data.vertices:
            w = sum(g.weight for g in v.groups if g.group in ids)
            if w > 0.5 and v.co.z < 0.03 and (best is None or v.co.y < best.y):
                best = v.co.copy()
        out[side] = (best.x, best.y, 0.0)
    return out


def takes(sk):
    D = defaults(sk)
    return {"Idle": idle_take(D), "Walk": walk_take(sk, D), "LineCast": linecast_take(D), "Burst": burst_take(D),
            "Shot": shot_take(D), "Hit": hit_take(D), "Death": death_take(D)}


def solve(sk, take, f):
    return build(sk, take["params"](f))


def key_action(arm, sk, name, take, step=1.0):
    """Sample the take and key every deform bone (loc + quaternion) -> action PREFIX+name."""
    act = bpy.data.actions.get(PREFIX + name) or bpy.data.actions.new(PREFIX + name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    n = take["frames"]
    frames = list(np.arange(0, n + 1e-6, step))
    report = {"arm_short_max": 0.0, "leg_short_max": 0.0, "tips": {}}
    prev = {}
    for f in frames:
        f = float(f)
        # loops: the last frame reuses frame 0 exactly so the seam is perfect
        pose, info = solve(sk, take, 0.0 if (take.get("loop") and f >= n) else f)
        report["arm_short_max"] = max(report["arm_short_max"], info["L_arm_short"], info["R_arm_short"])
        report["leg_short_max"] = max(report["leg_short_max"], info["L_leg_short"], info["R_leg_short"])
        if float(f).is_integer():
            report["tips"][int(f)] = {"L": info["L_tip"], "R": info["R_tip"]}
            worst = max(info["L_arm_short"], info["R_arm_short"], info["L_leg_short"], info["R_leg_short"])
            if worst > 0.01:
                report.setdefault("short_frames", {})[int(f)] = round(worst, 3)
        basis = pose.basis()
        for bn, m in basis.items():
            pb = arm.pose.bones[bn]
            pb.rotation_mode = "QUATERNION"
            loc, q, _ = m.decompose()
            if bn in prev and q.dot(prev[bn]) < 0:
                q.negate()
            prev[bn] = q
            pb.location = loc
            pb.rotation_quaternion = q
            pb.scale = (1, 1, 1)
            pb.keyframe_insert("location", frame=f, group=bn)
            pb.keyframe_insert("rotation_quaternion", frame=f, group=bn)
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:
                        k.interpolation = "LINEAR" if step < 1 else "BEZIER"
                        k.handle_left_type = k.handle_right_type = "AUTO_CLAMPED"
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, n
    act["loop"] = bool(take.get("loop"))
    if take.get("contact") is not None:
        act["contact_frame"] = int(take["contact"])
    return act, report
