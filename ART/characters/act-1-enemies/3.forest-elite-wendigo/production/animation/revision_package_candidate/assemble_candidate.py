"""Assemble an isolated six-action Wendigo candidate in Blender 5.2.

Run from the repository with Blender 5.2 using --factory-startup, for example:

  blender --factory-startup --background ForestWendigo_Rig_SkinCandidate.blend \
    --python assemble_candidate.py -- \
    --claw revision_claw_art/ForestWendigo_Claw_ArtRevision.blend

Paths passed after -- may be absolute or relative to production/animation.
The source blends are read only. Validation writes only JSON beside this script.
An explicit --export flag is required to write candidate .blend and .fbx files.
"""

import argparse
import hashlib
import json
import re
import struct
import sys
import traceback
from pathlib import Path

import bpy


HERE = Path(__file__).resolve().parent
ANIMATION = HERE.parent
PRODUCTION = ANIMATION.parent
DEFAULTS = {
    "skin": PRODUCTION / "rig_revision/final_candidate/ForestWendigo_Rig_SkinCandidate.blend",
    "idle": ANIMATION / "ForestWendigo_Animated.blend",
    "walkdeath": ANIMATION / "revision_secondary/output_final/ForestWendigo_WalkDeath_Revised.blend",
    "hit": ANIMATION / "revision_hit/ForestWendigo_Hit_v2.blend",
    "leap": ANIMATION / "revision_primary/Wendigo_Primary_v7b_CoilProbe.blend",
}
CLIPS = (
    ("Idle", "idle"),
    ("Walk", "walkdeath"),
    ("Claw", "claw"),
    ("Leap", "leap"),
    ("Hit", "hit"),
    ("Death", "walkdeath"),
)
RIG_NAME = "ARM_ForestWendigo"
MESH_NAME = "SM_ForestWendigo_LOD0"
MAX_TRIANGLES = 25_000
ROOT_XY_TOLERANCE_M = 1e-5
BONE_PATH = re.compile(r'^pose\.bones\["([^\"]+)"\]')


def resolve_source(raw):
    path = Path(raw)
    return (path if path.is_absolute() else ANIMATION / path).resolve()


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("skin", "idle", "walkdeath", "hit", "leap"):
        parser.add_argument("--" + key, default=str(DEFAULTS[key]))
    parser.add_argument("--claw", required=True, help="Final Claw .blend path")
    parser.add_argument("--export", action="store_true",
                        help="Explicitly write isolated candidate .blend and .fbx")
    args = parser.parse_args(argv)
    args.sources = {key: resolve_source(getattr(args, key))
                    for key in ("skin", "idle", "walkdeath", "hit", "leap", "claw")}
    return args


def digest_mesh(mesh):
    h = hashlib.sha256()
    for vertex in mesh.vertices:
        h.update(struct.pack("<3d", *vertex.co))
    for polygon in mesh.polygons:
        h.update(struct.pack("<I", len(polygon.vertices)))
        for index in polygon.vertices:
            h.update(struct.pack("<I", index))
    return h.hexdigest()


def digest_file(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def digest_bones(armature):
    h = hashlib.sha256()
    for bone in sorted(armature.bones, key=lambda item: item.name):
        h.update(bone.name.encode("utf-8") + b"\0")
        h.update((bone.parent.name if bone.parent else "").encode("utf-8") + b"\0")
        h.update(bytes([bone.use_deform]))
        for row in bone.matrix_local:
            h.update(struct.pack("<4d", *row))
        h.update(struct.pack("<d", bone.length))
    return h.hexdigest()


def iter_curves(action):
    for slot in action.slots:
        for layer in action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(slot)
                if bag is not None:
                    yield from bag.fcurves


def validate_action_paths(action, rig):
    unknown_bones = set()
    unsupported_paths = set()
    for curve in iter_curves(action):
        path = curve.data_path
        match = BONE_PATH.match(path)
        if match:
            if match.group(1) not in rig.data.bones:
                unknown_bones.add(match.group(1))
        elif path not in {"location", "rotation_euler", "rotation_quaternion", "scale"}:
            unsupported_paths.add(path)
    if unknown_bones or unsupported_paths:
        raise RuntimeError(f"{action.name}: unknown bones {sorted(unknown_bones)}, "
                           f"unsupported paths {sorted(unsupported_paths)}")


def append_one_source(source, clip_names, base_bone_digest, base_mesh_digest):
    expected_actions = [f"AN_ForestWendigo_{clip}" for clip in clip_names]
    with bpy.data.libraries.load(str(source), link=False) as (available, loaded):
        for category, expected in (("armatures", RIG_NAME), ("meshes", MESH_NAME)):
            if expected not in getattr(available, category):
                raise RuntimeError(f"{source}: missing {category} datablock {expected}")
        missing = sorted(set(expected_actions) - set(available.actions))
        if missing:
            raise RuntimeError(f"{source}: missing actions {missing}")
        loaded.armatures = [RIG_NAME]
        loaded.meshes = [MESH_NAME]
        loaded.actions = expected_actions

    imported_rig = loaded.armatures[0]
    imported_mesh = loaded.meshes[0]
    try:
        bone_digest = digest_bones(imported_rig)
        mesh_digest = digest_mesh(imported_mesh)
        if bone_digest != base_bone_digest:
            raise RuntimeError(f"{source}: rig rest pose/bones differ from SkinCandidate")
        if mesh_digest != base_mesh_digest:
            raise RuntimeError(f"{source}: mesh geometry differs from SkinCandidate")
    finally:
        bpy.data.armatures.remove(imported_rig)
        bpy.data.meshes.remove(imported_mesh)

    actions = {}
    for clip, action in zip(clip_names, loaded.actions):
        expected_name = f"AN_ForestWendigo_{clip}"
        if action is None or action.name != expected_name:
            raise RuntimeError(f"{source}: imported action name mismatch for {clip}")
        action.use_fake_user = True
        actions[clip] = action
    return actions


def activate_action(rig, action):
    rig.animation_data.action = action
    if len(action.slots) != 1:
        raise RuntimeError(f"{action.name}: expected one action slot, found {len(action.slots)}")
    rig.animation_data.action_slot = action.slots[0]
    if rig.animation_data.action != action or rig.animation_data.action_slot != action.slots[0]:
        raise RuntimeError(f"{action.name}: failed to bind its action slot to the rig")


def inspect_action(clip, action, rig, scene, expected_last):
    first, last = action.frame_range
    if abs(first) > 1e-6 or abs(last - expected_last) > 1e-6:
        raise RuntimeError(f"{clip}: frame range {first:g}-{last:g}; expected 0-{expected_last}")
    if not list(iter_curves(action)):
        raise RuntimeError(f"{clip}: action has no curves")
    validate_action_paths(action, rig)
    activate_action(rig, action)
    root = rig.pose.bones.get("root")
    head = rig.pose.bones.get("head")
    if root is None or head is None:
        raise RuntimeError("Expected root and head pose bones")

    max_root_xy = 0.0
    max_rig_xy = 0.0
    head_positions = []
    for frame in range(int(first), int(last) + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        max_root_xy = max(max_root_xy, abs(root.location.x), abs(root.location.y))
        max_rig_xy = max(max_rig_xy, abs(rig.matrix_world.translation.x),
                         abs(rig.matrix_world.translation.y))
        if frame in {int(first), int((first + last) / 2), int(last)}:
            head_positions.append((frame, list(rig.matrix_world @ head.head)))
    if max_root_xy > ROOT_XY_TOLERANCE_M or max_rig_xy > ROOT_XY_TOLERANCE_M:
        raise RuntimeError(f"{clip}: root XY drift {max_root_xy:.7f} m; "
                           f"armature XY drift {max_rig_xy:.7f} m")
    return {
        "source": None,
        "frame_range": [int(first), int(last)],
        "frames_inclusive": int(last - first + 1),
        "seconds": round((last - first) / scene.render.fps, 4),
        "fcurves": sum(1 for _ in iter_curves(action)),
        "slot_identifiers": [slot.identifier for slot in action.slots],
        "max_root_bone_xy_m": round(max_root_xy, 8),
        "max_armature_xy_m": round(max_rig_xy, 8),
        "head_samples": head_positions,
    }


def load_contract():
    path = HERE / "animation_contract.json"
    contract = json.loads(path.read_text(encoding="utf-8"))
    expected_names = {f"AN_ForestWendigo_{clip}" for clip, _ in CLIPS}
    by_name = {entry["name"]: entry for entry in contract["clips"]}
    if (len(by_name) != len(CLIPS) or set(by_name) != expected_names
            or contract["fps"] != 30
            or contract["geometry"]["maximum_exported_triangles"] != MAX_TRIANGLES):
        raise RuntimeError("Candidate animation contract has unexpected clips or limits")
    for name, entry in by_name.items():
        first, last = entry["frames"]
        if first != 0 or not isinstance(last, int) or last <= 0:
            raise RuntimeError(f"{name}: invalid contract frame range")
    walk = by_name["AN_ForestWendigo_Walk"]
    cycle_distance = walk["target_speed_mps"] * walk["frames"][1] / contract["fps"]
    if (walk["frames"] != [0, 18]
            or abs(cycle_distance - walk["cycle_ground_equivalent_m"]) > 1e-6):
        raise RuntimeError("Walk contract must specify 0-18 and 2.16 m at 3.6 m/s")
    claw = by_name["AN_ForestWendigo_Claw"]
    leap = by_name["AN_ForestWendigo_Leap"]
    death = by_name["AN_ForestWendigo_Death"]
    if (claw["contact_frame"] != 18 or "front/left" not in claw["beats"]
            or leap["takeoff_frame"] != 24 or leap["landing_contact_frame"] != 33
            or death["frames"] != [0, 60]):
        raise RuntimeError("Candidate contact, leap, or death contract values differ")
    return path, contract, by_name


def assemble(args, report):
    if bpy.app.version[:2] != (5, 2):
        raise RuntimeError(f"Blender 5.2 required; found {bpy.app.version_string}")
    for key, path in args.sources.items():
        if not path.is_file():
            raise FileNotFoundError(f"{key}: {path}")
    source_hashes = {key: digest_file(path) for key, path in args.sources.items()}
    report["source_sha256"] = source_hashes
    contract_path, contract, clips_by_name = load_contract()
    report["contract"] = str(contract_path)
    if Path(bpy.data.filepath).resolve() != args.sources["skin"]:
        raise RuntimeError("Open SkinCandidate .blend as the Blender startup file")

    scene = bpy.context.scene
    rig = bpy.data.objects.get(RIG_NAME)
    mesh = bpy.data.objects.get(MESH_NAME)
    if rig is None or rig.type != "ARMATURE" or mesh is None or mesh.type != "MESH":
        raise RuntimeError("SkinCandidate rig or mesh object missing")
    if mesh.find_armature() != rig:
        raise RuntimeError("SkinCandidate mesh is not bound to the expected rig")
    if len([obj for obj in bpy.data.objects if obj.type == "ARMATURE"]) != 1:
        raise RuntimeError("SkinCandidate has more than one armature")
    if len([obj for obj in bpy.data.objects if obj.type == "MESH"]) != 1:
        raise RuntimeError("SkinCandidate has more than one mesh")

    triangles = sum(len(face.vertices) - 2 for face in mesh.data.polygons)
    if triangles != 24_636 or triangles > MAX_TRIANGLES:
        raise RuntimeError(f"Unexpected SkinCandidate triangle count: {triangles}")
    material_count = sum(slot.material is not None for slot in mesh.material_slots)
    max_bone_influences = max((sum(group.weight > 0 for group in vertex.groups)
                               for vertex in mesh.data.vertices), default=0)
    if material_count > contract["geometry"]["materials_target"]:
        raise RuntimeError(f"SkinCandidate has {material_count} materials")
    if max_bone_influences > contract["geometry"]["max_bone_influences_per_vertex"]:
        raise RuntimeError(f"SkinCandidate has {max_bone_influences} influences per vertex")
    bone_digest = digest_bones(rig.data)
    mesh_digest = digest_mesh(mesh.data)
    report["skin"] = {
        "triangles": triangles,
        "max_triangles": MAX_TRIANGLES,
        "vertices": len(mesh.data.vertices),
        "material_count": material_count,
        "max_bone_influences_per_vertex": max_bone_influences,
        "bones": len(rig.data.bones),
        "deform_bones": sum(bone.use_deform for bone in rig.data.bones),
        "bone_digest": bone_digest,
        "mesh_digest": mesh_digest,
        "rig_object": rig.name,
        "mesh_object": mesh.name,
    }

    if bpy.data.actions:
        raise RuntimeError("SkinCandidate already contains actions; use the clean source blend")
    rig.animation_data_create()
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    scene.render.fps = 30
    scene.render.fps_base = 1.0

    by_source = {}
    for clip, key in CLIPS:
        by_source.setdefault(key, []).append(clip)
    actions = {}
    for key, clip_names in by_source.items():
        actions.update(append_one_source(args.sources[key], clip_names,
                                         bone_digest, mesh_digest))
    expected_names = {f"AN_ForestWendigo_{clip}" for clip, _ in CLIPS}
    if {action.name for action in bpy.data.actions} != expected_names:
        raise RuntimeError("Candidate contains missing or extraneous actions")

    for clip, key in CLIPS:
        expected_last = clips_by_name[f"AN_ForestWendigo_{clip}"]["frames"][1]
        data = inspect_action(clip, actions[clip], rig, scene, expected_last)
        data["source"] = str(args.sources[key])
        report["actions"][clip] = data
    for key, path in args.sources.items():
        if digest_file(path) != source_hashes[key]:
            raise RuntimeError(f"{key}: source changed while assembling candidate")
    activate_action(rig, actions["Idle"])
    scene.frame_set(0)
    bpy.context.view_layer.update()

    if not args.export:
        report["status"] = "validated_only"
        return

    blend_path = HERE / "ForestWendigo_SixAction_Candidate.blend"
    fbx_path = HERE / "ForestWendigo_SixAction_Candidate.fbx"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), check_existing=False)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        global_scale=1,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1,
        bake_anim_simplify_factor=0,
        path_mode="ABSOLUTE",
        embed_textures=False,
    )
    for key, path in args.sources.items():
        if digest_file(path) != source_hashes[key]:
            raise RuntimeError(f"{key}: source changed during FBX export")
    if not blend_path.is_file() or not fbx_path.is_file():
        raise RuntimeError("Candidate save or FBX export did not produce a file")
    report["outputs"] = {
        "blend": str(blend_path), "blend_bytes": blend_path.stat().st_size,
        "fbx": str(fbx_path), "fbx_bytes": fbx_path.stat().st_size,
    }
    report["status"] = "candidate_exported_pending_review"


def main():
    args = parse_args()
    report_path = HERE / "assembly_report.json"
    report = {
        "status": "validation_failed",
        "blender": bpy.app.version_string,
        "sources": {key: str(path) for key, path in args.sources.items()},
        "fps": 30,
        "root_xy_tolerance_m": ROOT_XY_TOLERANCE_M,
        "actions": {},
    }
    try:
        assemble(args, report)
    except Exception as exc:
        report["error"] = str(exc)
        report["traceback"] = traceback.format_exc()
        raise
    finally:
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print("WENDIGO_CANDIDATE_REPORT", report_path, report["status"])


if __name__ == "__main__":
    main()
