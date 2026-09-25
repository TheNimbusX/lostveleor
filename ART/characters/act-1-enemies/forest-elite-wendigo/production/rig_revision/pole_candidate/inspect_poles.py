"""Read-only rest/control inspection for Wendigo pole-stability audit."""

import json
from pathlib import Path

import bpy


rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
mesh.data.calc_loop_triangles()
names = (
    "L_clavicle", "L_arm_upper", "L_arm_lower", "L_hand",
    "R_clavicle", "R_arm_upper", "R_arm_lower", "R_hand",
    "L_leg_upper", "L_leg_lower", "L_foot",
    "R_leg_upper", "R_leg_lower", "R_foot",
    "CTRL_L_elbow", "CTRL_L_knee", "CTRL_R_elbow", "CTRL_R_knee",
)
data = {
    "file": bpy.data.filepath,
    "tris": len(mesh.data.loop_triangles),
    "deform_bones": sum(b.use_deform for b in rig.data.bones),
    "bones": {
        name: {
            "head": [round(v, 4) for v in rig.data.bones[name].head_local],
            "tail": [round(v, 4) for v in rig.data.bones[name].tail_local],
            "parent": rig.data.bones[name].parent.name if rig.data.bones[name].parent else None,
            "deform": bool(rig.data.bones[name].use_deform),
        }
        for name in names
    },
    "ik": {
        name: {
            "pole": constraint.pole_subtarget,
            "target": constraint.subtarget,
            "angle_deg": round(constraint.pole_angle * 180 / 3.141592653589793, 2),
            "chain_count": constraint.chain_count,
        }
        for name in ("L_arm_lower", "R_arm_lower", "L_leg_lower", "R_leg_lower")
        for constraint in rig.pose.bones[name].constraints if constraint.type == "IK"
    },
}
out = Path(__file__).resolve().parent / "rest_source.json"
out.write_text(json.dumps(data, indent=2), encoding="utf-8")
print(json.dumps(data), flush=True)
