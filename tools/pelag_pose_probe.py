"""Small reproducible rig probe used by the Pelag animation build."""

from __future__ import annotations

import json
from pathlib import Path

import bpy


PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
SOURCE = PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_AnchorAttack.fbx"
OUTPUT = PROJECT / "artifacts/pelag-rig-probe.json"


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=True)
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
action = armature.animation_data.action

samples = {}
for frame in (2, 17, 32, 47, 62, 77, 92, 107, 122, 137, 152, 167):
    bpy.context.scene.frame_set(frame)
    samples[str(frame)] = {}
    for name in (
        "mixamorig:Hips",
        "mixamorig:Spine",
        "mixamorig:Spine1",
        "mixamorig:Spine2",
        "mixamorig:LeftUpLeg",
        "mixamorig:LeftLeg",
        "mixamorig:RightUpLeg",
        "mixamorig:RightLeg",
        "mixamorig:LeftArm",
        "mixamorig:LeftForeArm",
        "mixamorig:RightArm",
        "mixamorig:RightForeArm",
        "mixamorig:LeftHand",
        "mixamorig:RightHand",
    ):
        bone = armature.pose.bones.get(name)
        if bone is None:
            continue
        location, rotation, scale = bone.matrix_basis.decompose()
        samples[str(frame)][name] = {
            "location": [round(value, 6) for value in location],
            "rotation_quaternion": [round(value, 6) for value in rotation],
            "scale": [round(value, 6) for value in scale],
            "head_world": [
                round(value, 6)
                for value in (armature.matrix_world @ bone.head)
            ],
        }

payload = {
    "source": str(SOURCE),
    "fps": bpy.context.scene.render.fps,
    "frame_range": list(action.frame_range),
    "armature": armature.name,
    "bones": [bone.name for bone in armature.data.bones],
    "samples": samples,
}
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(f"Wrote {OUTPUT}")
