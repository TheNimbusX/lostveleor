"""Print FBX object, bind-pose and animated hip bases for rig QA."""

import bpy
import os
import sys


def rounded(values):
    return tuple(round(float(value), 6) for value in values)


for path in sys.argv[sys.argv.index("--") + 1 :]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path), use_anim=True)
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    hip = armature.data.bones["mixamorig:Hips"]
    print(f"FILE={path}")
    print(
        f"ARMATURE loc={rounded(armature.location)} "
        f"rot={rounded(armature.rotation_euler)} scale={rounded(armature.scale)}"
    )
    print(f"HIP_BIND_HEAD={rounded(hip.head_local)} TAIL={rounded(hip.tail_local)}")
    action = next(iter(bpy.data.actions), None)
    if action is not None:
        armature.animation_data_create()
        armature.animation_data.action = action
        for frame in (int(action.frame_range[0]), int(action.frame_range[1])):
            bpy.context.scene.frame_set(frame)
            pose = armature.pose.bones["mixamorig:Hips"]
            print(
                f"FRAME={frame} hip_loc={rounded(pose.location)} "
                f"hip_rot={rounded(pose.rotation_quaternion)}"
            )
