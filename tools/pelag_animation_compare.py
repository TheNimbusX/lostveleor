"""Compare pre/post mesh-ground-lift FBX actions without changing assets."""
from __future__ import annotations
import json
import math
from pathlib import Path
import bpy

PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
OLD = PROJECT / "artifacts/pelag-animation-build/pre-mesh-lift"
NEW = PROJECT / "artifacts/pelag-animation-build/exports"
OUT = PROJECT / "artifacts/pelag-animation-build/mesh-lift-compare.json"

def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

def load(path):
    clear()
    result = bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    if "FINISHED" not in result:
        raise RuntimeError(path)
    arm = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    return arm, arm.animation_data.action

def compare(old_path, new_path):
    old_arm, old_action = load(old_path)
    old_start, old_end = map(round, old_action.frame_range)
    old_bones = {bone.name for bone in old_arm.pose.bones}
    old_samples = []
    for offset in range(old_end - old_start + 1):
        bpy.context.scene.frame_set(old_start + offset)
        bpy.context.view_layer.update()
        old_samples.append({
            bone.name: (bone.matrix_basis.translation.copy(),
                        bone.matrix_basis.to_quaternion().normalized())
            for bone in old_arm.pose.bones
        })
    new_arm, new_action = load(new_path)
    new_start, new_end = map(round, new_action.frame_range)
    new_bones = {bone.name for bone in new_arm.pose.bones}
    if old_bones != new_bones:
        raise RuntimeError(f"bone mismatch {old_path.name}")
    if (old_end - old_start) != (new_end - new_start):
        raise RuntimeError(f"duration mismatch {old_path.name}")
    max_location = 0.0
    max_rotation = 0.0
    max_by_bone = {}
    hips_z_deltas = []
    for offset in range(old_end - old_start + 1):
        new_frame = new_start + offset
        bpy.context.scene.frame_set(new_frame)
        bpy.context.view_layer.update()
        old_pose = old_samples[offset]
        for bone in new_arm.pose.bones:
            location, rotation = bone.matrix_basis.translation.copy(), bone.matrix_basis.to_quaternion().normalized()
            old_location, old_rotation = old_pose[bone.name]
            location_delta = (location - old_location).length
            rotation_delta = math.degrees(old_rotation.rotation_difference(rotation).angle)
            max_location = max(max_location, location_delta)
            max_rotation = max(max_rotation, rotation_delta)
            max_by_bone[bone.name] = max(max_by_bone.get(bone.name, 0.0), location_delta)
            if bone.name == "mixamorig:Hips":
                hips_z_deltas.append(float(location.z - old_location.z))
    return {
        "file": old_path.name,
        "old_frame_range": [old_start, old_end],
        "new_frame_range": [new_start, new_end],
        "max_matrix_basis_location_delta_m": max_location,
        "max_matrix_basis_rotation_delta_deg": max_rotation,
        "max_location_delta_by_bone_m": max_by_bone,
        "hips_z_delta_min_m": min(hips_z_deltas),
        "hips_z_delta_max_m": max(hips_z_deltas),
        "hips_z_delta_mean_m": sum(hips_z_deltas) / len(hips_z_deltas),
    }

rows = []
for new_path in sorted(NEW.glob("Pelag_MX_*.fbx")):
    old_path = OLD / new_path.name
    if old_path.is_file():
        rows.append(compare(old_path, new_path))
OUT.write_text(json.dumps(rows, indent=2), encoding="utf-8")
for row in rows:
    print(row["file"], "loc", row["max_matrix_basis_location_delta_m"], "rot", row["max_matrix_basis_rotation_delta_deg"], "hipsZ", row["hips_z_delta_min_m"], row["hips_z_delta_max_m"])
