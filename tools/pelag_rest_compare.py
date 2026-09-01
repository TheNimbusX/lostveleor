"""Compare original and staged Mixamo armature bind matrices in world space."""

from pathlib import Path
import json

import bpy


ORIGINAL = Path(r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_AnchorAttack.fbx")
STAGED = Path(r"C:\Users\d.grab\Desktop\the-game\artifacts\pelag-animation-build\exports\Pelag_MX_AnchorLeap.fbx")
OUTPUT = Path(r"C:\Users\d.grab\Desktop\the-game\artifacts\pelag-animation-build\rest-compare.json")
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(ORIGINAL), use_anim=False)
original = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
bpy.ops.import_scene.fbx(filepath=str(STAGED), use_anim=False)
armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
staged = next(obj for obj in armatures if obj is not original)
rows = []
for source_bone in original.data.bones:
    target_bone = staged.data.bones.get(source_bone.name)
    source_head = original.matrix_world @ source_bone.head_local
    source_tail = original.matrix_world @ source_bone.tail_local
    target_head = staged.matrix_world @ target_bone.head_local
    target_tail = staged.matrix_world @ target_bone.tail_local
    rows.append(
        {
            "bone": source_bone.name,
            "head_error_m": (source_head - target_head).length,
            "tail_error_m": (source_tail - target_tail).length,
            "matrix_error": max(
                abs(source_bone.matrix_local[row][column] - target_bone.matrix_local[row][column])
                for row in range(4)
                for column in range(4)
            ),
        }
    )
payload = {
    "original_object_matrix": [list(row) for row in original.matrix_world],
    "staged_object_matrix": [list(row) for row in staged.matrix_world],
    "max_head_error_m": max(row["head_error_m"] for row in rows),
    "max_tail_error_m": max(row["tail_error_m"] for row in rows),
    "max_local_matrix_error": max(row["matrix_error"] for row in rows),
    "worst": sorted(rows, key=lambda row: row["head_error_m"] + row["tail_error_m"], reverse=True)[:8],
}
OUTPUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(OUTPUT)
