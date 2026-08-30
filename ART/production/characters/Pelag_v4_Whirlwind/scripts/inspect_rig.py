import bpy
import json
import sys


def argument_after_separator(index=0):
    args = sys.argv
    marker = args.index("--")
    return args[marker + 1 + index]


source = argument_after_separator()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source, use_anim=True)

report = {"source": source, "armatures": [], "actions": []}
for obj in bpy.data.objects:
    if obj.type != "ARMATURE":
        continue
    armature = {
        "name": obj.name,
        "location": list(obj.location),
        "rotation": list(obj.rotation_euler),
        "scale": list(obj.scale),
        "bones": [],
    }
    for bone in obj.data.bones:
        armature["bones"].append({
            "name": bone.name,
            "parent": bone.parent.name if bone.parent else None,
            "head": list(bone.head_local),
            "tail": list(bone.tail_local),
        })
    report["armatures"].append(armature)

for action in bpy.data.actions:
    report["actions"].append({"name": action.name, "frame_range": list(action.frame_range)})

print("PELAG_RIG_REPORT=" + json.dumps(report, ensure_ascii=False))
