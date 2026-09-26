"""Read-only summary of Forest Wendigo IK controls and constraints."""

import json
import bpy


rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
out = []
for pb in rig.pose.bones:
    if not pb.name.startswith("CTRL_") and not pb.constraints:
        continue
    bone = rig.data.bones[pb.name]
    out.append({
        "name": pb.name,
        "head": [round(value, 3) for value in bone.head_local],
        "tail": [round(value, 3) for value in bone.tail_local],
        "constraints": [{
            "name": c.name,
            "type": c.type,
            "influence": round(c.influence, 4),
            "target": c.target.name if hasattr(c, "target") and c.target else None,
            "subtarget": c.subtarget if hasattr(c, "subtarget") else None,
            "chain_count": c.chain_count if hasattr(c, "chain_count") else None,
        } for c in pb.constraints],
    })
print(json.dumps(out, ensure_ascii=False))
