"""Estimate authored saber contact frames from right-hand peak velocity."""

import bpy
import json
import os
import sys


def world_head(armature, bone_name):
    return armature.matrix_world @ armature.pose.bones[bone_name].head


def analyze(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), use_anim=True)
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    armature.animation_data_create()
    armature.animation_data.action = action

    first = int(round(action.frame_range[0]))
    last = int(round(action.frame_range[1]))
    positions = {}
    for frame in range(first, last + 1):
        bpy.context.scene.frame_set(frame)
        positions[frame] = (
            world_head(armature, "mixamorig:RightHand")
            - world_head(armature, "mixamorig:Hips")
        )

    speeds = {
        frame: (positions[frame] - positions[frame - 1]).length
        for frame in range(first + 1, last + 1)
    }
    ranges = {"attack_a": (2, 50), "attack_b": (50, 100), "recovery": (100, 148)}
    report = {"source": os.path.abspath(path), "action": action.name, "ranges": {}}
    for name, (start, end) in ranges.items():
        candidates = [
            {"frame": frame, "hand_travel_per_frame_m": speeds[frame]}
            for frame in range(max(start + 1, first + 1), min(end, last) + 1)
        ]
        candidates.sort(key=lambda item: item["hand_travel_per_frame_m"], reverse=True)
        report["ranges"][name] = candidates[:5]

    print(json.dumps(report, indent=2))


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) != 1:
    raise RuntimeError("Expected one saber combo FBX path")
analyze(arguments[0])
