"""Round-trip check of a Wendigo FBX in a clean Blender 5.2 scene.

Usage: blender --factory-startup --background --python verify_fbx.py -- --fbx file.fbx
"""

import argparse
import json
import sys
from pathlib import Path

import bpy


EXPECTED = ("Idle", "Walk", "Claw", "Leap", "Hit", "Death")
HERE = Path(__file__).resolve().parent


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--fbx", type=Path, required=True)
    parser.add_argument("--out", type=Path, default=HERE / "fbx_roundtrip_report.json")
    args = parser.parse_args(argv)
    fbx_path = args.fbx.resolve()
    if not fbx_path.is_file():
        raise FileNotFoundError(fbx_path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path), use_anim=True)
    rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    actions = {action.name: action for action in bpy.data.actions}
    contract = json.loads((HERE / "animation_contract.json").read_text(encoding="utf-8"))
    expected_last = {entry["name"].removeprefix("AN_ForestWendigo_"): entry["frames"][1]
                     for entry in contract["clips"]}
    triangles = sum(len(face.vertices) - 2 for mesh in meshes for face in mesh.data.polygons)
    found = {}
    for clip in EXPECTED:
        matches = [action for name, action in actions.items()
                   if f"AN_ForestWendigo_{clip}" in name]
        found[clip] = [{"name": action.name,
                        "frame_range": [float(n) for n in action.frame_range]}
                       for action in matches]
    report = {
        "fbx": str(fbx_path),
        "blender": bpy.app.version_string,
        "rigs": [{"name": rig.name, "bones": len(rig.data.bones)} for rig in rigs],
        "meshes": [{"name": mesh.name,
                    "triangles": sum(len(face.vertices) - 2 for face in mesh.data.polygons),
                    "rig": mesh.find_armature().name if mesh.find_armature() else None}
                   for mesh in meshes],
        "triangles": triangles,
        "actions": found,
        "all_imported_action_names": sorted(actions),
    }
    report["pass"] = (len(rigs) == 1 and len(meshes) == 1
                      and len(rigs[0].data.bones) == 22
                      and meshes[0].find_armature() == rigs[0]
                      and triangles == 24_636 and triangles <= 25_000
                      and all(len(found[clip]) == 1
                              and abs((found[clip][0]["frame_range"][1]
                                       - found[clip][0]["frame_range"][0])
                                      - expected_last[clip]) < 1e-4
                              for clip in EXPECTED))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("FBX_ROUNDTRIP", json.dumps({
        "pass": report["pass"], "triangles": triangles,
        "rigs": report["rigs"], "actions": found}))
    if not report["pass"]:
        raise RuntimeError("FBX round-trip validation failed")


if __name__ == "__main__":
    main()
