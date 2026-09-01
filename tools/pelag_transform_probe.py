"""Diagnostic for Mixamo root-local to armature/world translation axes."""

from pathlib import Path
import json

import bpy


SOURCE = Path(r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_AnchorAttack.fbx")
OUTPUT = Path(r"C:\Users\d.grab\Desktop\the-game\artifacts\pelag-animation-build\root-axis-probe.json")
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=False)
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
root = armature.pose.bones["mixamorig:Hips"]
payload = {
    "armature_world": [list(row) for row in armature.matrix_world],
    "bone_rest": [list(row) for row in root.bone.matrix_local],
    "baseline_location": list(root.location),
    "baseline_pose_translation": list(root.matrix.translation),
    "axes": {},
}
for component, label in enumerate("xyz"):
    baseline = root.location.copy()
    root.location[component] += 1.0
    payload["axes"][label] = {
        "no_update_pose_translation": list(root.matrix.translation),
    }
    root.location = baseline
OUTPUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(OUTPUT)
