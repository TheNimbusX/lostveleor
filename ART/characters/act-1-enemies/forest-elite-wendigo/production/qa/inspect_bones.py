"""Print compact rest-bone coordinates for animation blocking."""

import json

import bpy


rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
data = []
for bone in rig.data.bones:
    data.append({
        "name": bone.name,
        "parent": bone.parent.name if bone.parent else None,
        "head": [round(value, 3) for value in bone.head_local],
        "tail": [round(value, 3) for value in bone.tail_local],
        "deform": bone.use_deform,
    })
print(json.dumps(data, ensure_ascii=False))
