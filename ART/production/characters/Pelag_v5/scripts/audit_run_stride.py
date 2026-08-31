"""Measure in-place run cadence and planted-foot travel from an FBX clip."""

import bpy
import json
import math
import os
import sys
from mathutils import Vector


def world_head(armature, bone_name):
    bone = armature.pose.bones[bone_name]
    return armature.matrix_world @ bone.head


def contiguous_groups(frames, first_frame, last_frame):
    groups = []
    for frame in frames:
        if not groups or frame != groups[-1][-1] + 1:
            groups.append([frame])
        else:
            groups[-1].append(frame)

    # A looping contact can cross the clip boundary. Merge those pieces.
    if len(groups) > 1 and groups[0][0] == first_frame and groups[-1][-1] == last_frame:
        groups[0] = groups[-1] + groups[0]
        groups.pop()
    return groups


def horizontal_distance(a, b):
    return math.hypot(a.x - b.x, a.y - b.y)


def analyze(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), use_anim=True)
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    armature.animation_data_create()
    armature.animation_data.action = action

    first_frame = int(round(action.frame_range[0]))
    last_frame = int(round(action.frame_range[1]))
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    duration = (last_frame - first_frame) / fps
    samples = {}

    for frame in range(first_frame, last_frame + 1):
        bpy.context.scene.frame_set(frame)
        samples[frame] = {
            "hips": world_head(armature, "mixamorig:Hips"),
            "left_foot": world_head(armature, "mixamorig:LeftFoot"),
            "left_toe": world_head(armature, "mixamorig:LeftToeBase"),
            "right_foot": world_head(armature, "mixamorig:RightFoot"),
            "right_toe": world_head(armature, "mixamorig:RightToeBase"),
        }

    report = {
        "source": os.path.abspath(path),
        "action": action.name,
        "frames": [first_frame, last_frame],
        "fps": fps,
        "duration_seconds": duration,
        "feet": {},
    }

    stride_estimates = []
    for side in ("left", "right"):
        foot_key = f"{side}_foot"
        toe_key = f"{side}_toe"
        sole_heights = {
            frame: min(samples[frame][foot_key].z, samples[frame][toe_key].z)
            for frame in samples
        }
        ground = min(sole_heights.values())
        grounded = [frame for frame, height in sole_heights.items() if height <= ground + 0.035]
        contacts = []
        for group in contiguous_groups(grounded, first_frame, last_frame):
            ordered = sorted(group)
            points = []
            for frame in ordered:
                # Foot travel relative to the animated hips is the authored
                # stride after horizontal root motion has been removed.
                points.append(samples[frame][foot_key] - samples[frame]["hips"])
            travel = max(
                (horizontal_distance(a, b) for a in points for b in points),
                default=0.0,
            )
            if len(points) >= 2:
                stride_estimates.append(travel)
            contacts.append({
                "frames": ordered,
                "ground_height_m": ground,
                "horizontal_travel_m": travel,
            })
        report["feet"][side] = {"contacts": contacts}

    authored_stride = sum(stride_estimates) / len(stride_estimates) if stride_estimates else 0.0
    report["authored_stride_m"] = authored_stride
    report["natural_speed_mps_at_1x"] = authored_stride / duration if duration > 0 else 0.0
    report["contacts_per_second_at_1x"] = 2.0 / duration if duration > 0 else 0.0
    print(json.dumps(report, indent=2))


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) != 1:
    raise RuntimeError("Expected one run FBX path")
analyze(arguments[0])
