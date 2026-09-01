"""Inspect root channels of one staged Pelag FBX after Blender round-trip."""

from pathlib import Path
import json

import bpy


PATH = Path(r"C:\Users\d.grab\Desktop\the-game\artifacts\pelag-animation-build\exports\Pelag_MX_AnchorLeap.fbx")
OUTPUT = Path(r"C:\Users\d.grab\Desktop\the-game\artifacts\pelag-animation-build\anchor-leap-root-probe.json")

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(PATH), use_anim=True)
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
action = armature.animation_data.action
samples = []
for frame in range(round(action.frame_range[0]), round(action.frame_range[1]) + 1):
    bpy.context.scene.frame_set(frame)
    root = armature.pose.bones["mixamorig:Hips"]
    samples.append(
        {
            "frame": frame,
            "location": list(root.location),
            "head": list(root.head),
            "matrix_translation": list(root.matrix.translation),
        }
    )
OUTPUT.write_text(json.dumps(samples, indent=2), encoding="utf-8")
print(OUTPUT)
