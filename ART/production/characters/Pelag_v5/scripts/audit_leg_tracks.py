"""Report leg/foot trajectories relative to the hips for a Mixamo FBX clip."""

import bpy
import json
import os
import sys


def world_head(armature, bone_name):
    return armature.matrix_world @ armature.pose.bones[bone_name].head


def axis_range(samples, axis):
    values = [sample[axis] for sample in samples]
    return [round(min(values), 5), round(max(values), 5)]


def analyze(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), use_anim=True)
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    armature.animation_data_create()
    armature.animation_data.action = action

    start = int(round(action.frame_range[0]))
    end = int(round(action.frame_range[1]))
    report = {
        "source": os.path.abspath(path),
        "frames": [start, end],
        "tracks_relative_to_hips": {},
    }

    for side in ("Left", "Right"):
        tracks = {name: [] for name in ("UpLeg", "Leg", "Foot", "ToeBase")}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            hips = world_head(armature, "mixamorig:Hips")
            for suffix in tracks:
                point = world_head(armature, f"mixamorig:{side}{suffix}") - hips
                tracks[suffix].append(point)

        report["tracks_relative_to_hips"][side] = {
            suffix: {
                "x_range": axis_range(points, 0),
                "y_range": axis_range(points, 1),
                "z_range": axis_range(points, 2),
            }
            for suffix, points in tracks.items()
        }

    print(json.dumps(report, indent=2))


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) != 1:
    raise RuntimeError("Expected one Mixamo FBX path")
analyze(arguments[0])
