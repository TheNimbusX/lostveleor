"""Read-only FBX roundtrip audit for Forest Wendigo's six baked actions.

Run in a fresh Blender 5.2 process:
  blender --factory-startup -b --python validate_animation_fbx_roundtrip.py -- \
    --fbx ForestWendigo_Animated.fbx --out qa/combined_fbx_roundtrip_audit.json

The import exists only in memory; this script never saves a .blend or modifies FBX.
"""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


EXPECTED = {
    "AN_ForestWendigo_Idle": (0, 60),
    "AN_ForestWendigo_Walk": (0, 12),
    "AN_ForestWendigo_Claw": (0, 30),
    "AN_ForestWendigo_Leap": (0, 51),
    "AN_ForestWendigo_Hit": (0, 14),
    "AN_ForestWendigo_Death": (0, 45),
}


def arg(name, required=True):
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if name not in args or args.index(name) + 1 >= len(args):
        if required:
            raise SystemExit(f"Missing {name}")
        return None
    return args[args.index(name) + 1]


def issue(items, severity, code, **data):
    items.append({"severity": severity, "code": code, **data})


def frame_keys(action, slot):
    frames = set()
    scale_values = []
    paths = set()
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot)
            if bag is None:
                continue
            for curve in bag.fcurves:
                paths.add(curve.data_path)
                for key in curve.keyframe_points:
                    frames.add(round(key.co.x, 4))
                    if curve.data_path.endswith("scale"):
                        scale_values.append(key.co.y)
    return sorted(frames), paths, scale_values


def mesh_geometry(meshes):
    total_triangles = 0
    world_vertices = []
    per_mesh = []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        triangles = len(obj.data.loop_triangles)
        total_triangles += triangles
        world_vertices += [obj.matrix_world @ vert.co for vert in obj.data.vertices]
        per_mesh.append({
            "name": obj.name,
            "vertices": len(obj.data.vertices),
            "triangles": triangles,
            "object_scale": [round(float(v), 5) for v in obj.scale],
            "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
            "unweighted_vertices": sum(not vert.groups for vert in obj.data.vertices),
            "vertices_over_four_influences": sum(len(vert.groups) > 4 for vert in obj.data.vertices),
        })
    minimum = [min(point[i] for point in world_vertices) for i in range(3)] if world_vertices else [None] * 3
    maximum = [max(point[i] for point in world_vertices) for i in range(3)] if world_vertices else [None] * 3
    return {
        "triangles": total_triangles,
        "meshes": per_mesh,
        "bounds_min_m": [round(float(v), 4) for v in minimum],
        "bounds_max_m": [round(float(v), 4) for v in maximum],
        "height_m": round(float(maximum[2] - minimum[2]), 4) if world_vertices else None,
    }


def animation_samples(scene, rig, action, slot, event_relative_frame=None):
    rig.animation_data_create()
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    rig.animation_data.action = action
    rig.animation_data.action_slot = slot
    start, end = map(lambda value: int(round(value)), action.frame_range)
    root = rig.pose.bones.get("root")
    samples = []
    event_joints = None
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        root_pos = rig.matrix_world @ root.head if root else rig.matrix_world.translation
        samples.append([frame, round(root_pos.x, 5), round(root_pos.y, 5), round(root_pos.z, 5)])
        if event_relative_frame is not None and frame == start + event_relative_frame:
            event_joints = {
                name: [round(float(v), 4) for v in rig.matrix_world @ bone.head]
                for name in ("pelvis", "head", "L_hand", "R_hand", "L_foot", "R_foot")
                if (bone := rig.pose.bones.get(name)) is not None
            }
    origin = samples[0][1:3]
    maximum_xy_delta = max(math.hypot(item[1] - origin[0], item[2] - origin[1]) for item in samples)
    return {"root_max_xy_delta_m": round(maximum_xy_delta, 5),
            "root_start": samples[0], "root_end": samples[-1],
            "event_relative_frame": event_relative_frame,
            "event_imported_absolute_frame": start + event_relative_frame if event_relative_frame is not None else None,
            "event_joints_m": event_joints}


def main():
    path = Path(arg("--fbx")).resolve()
    out = Path(arg("--out")).resolve()
    source_report_path = arg("--source-report", required=False)
    source_actions = {}
    if source_report_path:
        source_data = json.loads(Path(source_report_path).read_text(encoding="utf-8"))
        source_actions = {item["name"]: item for item in source_data["actions"]}
    issues = []
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    scene = bpy.context.scene
    meshes = [obj for obj in scene.objects if obj.type == "MESH"]
    armatures = [obj for obj in scene.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        issue(issues, "fail", "armature_count", actual=len(armatures), expected=1)
    rig = armatures[0] if armatures else None
    default_connected_bones = [bone.name for bone in rig.data.bones if bone.use_connect] if rig else []
    if default_connected_bones:
        # Blender's FBX importer reconnects descendants, even though the FBX
        # contains keyed local translations for them. Connected PoseBones ignore
        # those translations in Blender's own evaluation. Normalize connection
        # flags in this disposable in-memory import, then compare baked poses.
        issue(issues, "warn", "blender_importer_connects_translated_bones",
              connected_bones=default_connected_bones,
              note="Local translation curves are present; in-memory disconnect is required to compare poses. Unity importer must be checked separately.")
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode="EDIT")
        for bone in rig.data.edit_bones:
            if bone.parent:
                bone.use_connect = False
        bpy.ops.object.mode_set(mode="OBJECT")
    geometry = mesh_geometry(meshes)
    if geometry["triangles"] > 25000:
        issue(issues, "fail", "triangle_budget", actual=geometry["triangles"], limit=25000)
    if geometry["height_m"] is None or abs(geometry["height_m"] - 3.1) > 0.06:
        issue(issues, "fail", "height_or_scale_mismatch", actual_m=geometry["height_m"], expected_m=3.1)
    if geometry["bounds_min_m"][2] is not None and abs(geometry["bounds_min_m"][2]) > 0.06:
        issue(issues, "fail", "feet_pivot_mismatch", min_z_m=geometry["bounds_min_m"][2])
    for mesh in geometry["meshes"]:
        if mesh["unweighted_vertices"] or mesh["vertices_over_four_influences"]:
            issue(issues, "fail", "skin_weights", mesh=mesh["name"],
                  unweighted=mesh["unweighted_vertices"],
                  over_four=mesh["vertices_over_four_influences"])
    bones = list(rig.data.bones) if rig else []
    bone_names = [bone.name for bone in bones]
    deform_names = [bone.name for bone in bones if bone.use_deform]
    controls = [name for name in bone_names if name.startswith("CTRL_")]
    if len(bones) != 22 or len(deform_names) != 22 or controls:
        issue(issues, "fail", "exported_bone_set", actual=len(bones), deform=len(deform_names),
              control_names=controls, expected=22)
    if rig and any(abs(v - 1.0) > 1e-4 for v in rig.scale):
        issue(issues, "warn", "armature_object_nonunit_scale", scale=[round(float(v), 5) for v in rig.scale])

    actions = []
    found = set()
    for action in sorted(bpy.data.actions, key=lambda item: item.name):
        expected = next((name for name in EXPECTED if name in action.name), None)
        if expected is None:
            continue
        found.add(expected)
        slot = next((slot for slot in action.slots if slot.target_id_type == "OBJECT"), None)
        if slot is None:
            issue(issues, "fail", "action_missing_slot", action=action.name)
            continue
        keys, paths, scale_values = frame_keys(action, slot)
        actual_range = [round(float(value), 4) for value in action.frame_range]
        intended_range = list(EXPECTED[expected])
        duration = round(actual_range[1] - actual_range[0], 4)
        expected_duration = intended_range[1] - intended_range[0]
        frame_origin_offset = round(actual_range[0] - intended_range[0], 4)
        if duration != expected_duration or frame_origin_offset not in (0, 1):
            issue(issues, "fail", "baked_action_range", action=action.name,
                  actual=actual_range, expected_duration=expected_duration,
                  allowed_frame_origin_offsets=[0, 1])
        expected_frames = set(range(int(actual_range[0]), int(actual_range[1]) + 1))
        missing_frames = sorted(expected_frames - set(keys))
        if missing_frames:
            issue(issues, "fail", "unbaked_frames", action=action.name, frames=missing_frames)
        max_scale_error = max((abs(value - 1.0) for value in scale_values), default=0.0)
        if max_scale_error > 0.005:
            issue(issues, "fail", "nonunit_scale_animation", action=action.name,
                  max_error=round(max_scale_error, 5))
        event_relative_frame = 18 if expected.endswith("Claw") else 33 if expected.endswith("Leap") else None
        samples = animation_samples(scene, rig, action, slot, event_relative_frame) if rig else None
        if samples and samples["root_max_xy_delta_m"] > 0.005:
            issue(issues, "fail", "horizontal_root_motion", action=action.name,
                  max_delta_m=samples["root_max_xy_delta_m"])
        event_pose_error = None
        source_action = source_actions.get(expected)
        if source_action and source_action.get("contact") and samples and samples["event_joints_m"]:
            source_joints = source_action["contact"]["joints_m"]
            errors = {
                name: math.sqrt(sum((value[i] - source_joints[name][i]) ** 2 for i in range(3)))
                for name, value in samples["event_joints_m"].items() if name in source_joints
            }
            event_pose_error = {"max_joint_error_m": round(max(errors.values(), default=0), 5),
                                "per_joint_error_m": {name: round(value, 5) for name, value in errors.items()}}
            if event_pose_error["max_joint_error_m"] > 0.03:
                issue(issues, "fail", "event_pose_changed_on_fbx_roundtrip", action=action.name,
                      imported_event_frame=samples["event_imported_absolute_frame"],
                      max_joint_error_m=event_pose_error["max_joint_error_m"])
        actions.append({
            "name": action.name,
            "contract_name": expected,
            "frame_range": actual_range,
            "frame_origin_offset": frame_origin_offset,
            "keyed_frame_count": len(keys),
            "fcurve_path_count": len(paths),
            "scale_key_count": len(scale_values),
            "max_scale_error": round(max_scale_error, 6),
            "event_pose_error": event_pose_error,
            **(samples or {}),
        })
    for name in EXPECTED:
        if name not in found:
            issue(issues, "fail", "missing_baked_clip", clip=name)
    counts = {level: sum(item["severity"] == level for item in issues) for level in ("fail", "warn", "note")}
    result = {
        "fbx": str(path),
        "file_bytes": path.stat().st_size,
        "geometry": geometry,
        "armature": {
            "name": rig.name if rig else None,
            "object_scale": [round(float(v), 5) for v in rig.scale] if rig else None,
            "bone_count": len(bones),
            "deform_bone_count": len(deform_names),
            "bone_names": bone_names,
            "control_bones": controls,
            "blender_default_import_connected_bones": default_connected_bones,
            "qa_disconnected_bones_in_memory_for_pose_comparison": bool(default_connected_bones),
        },
        "actions": actions,
        "issues": issues,
        "issue_counts": counts,
        "roundtrip_pass": counts["fail"] == 0,
        "limits": [
            "Blender roundtrip verifies FBX contents, not Unity's importer settings or game-frame contact timing.",
            "Material appearance and Animation Controller transitions require an in-engine stage-4 check.",
        ],
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print("FBX_ROUNDTRIP_JSON", out)
    print("FBX_ROUNDTRIP_SUMMARY", json.dumps({
        "geometry": geometry, "bone_count": len(bones), "deform": len(deform_names),
        "clips": [action["contract_name"] for action in actions],
        "issue_counts": counts, "issues": issues, "pass": result["roundtrip_pass"],
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
