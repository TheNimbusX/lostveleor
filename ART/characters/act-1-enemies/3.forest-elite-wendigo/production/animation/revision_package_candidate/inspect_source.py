"""Read-only Blender 5.2 source audit for the isolated Wendigo candidate.

Usage: blender --background source.blend --python inspect_source.py -- --out report.json
"""

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import bpy


def digest_mesh(mesh):
    h = hashlib.sha256()
    for vertex in mesh.vertices:
        h.update(struct.pack("<3d", *vertex.co))
    for polygon in mesh.polygons:
        h.update(struct.pack("<I", len(polygon.vertices)))
        for index in polygon.vertices:
            h.update(struct.pack("<I", index))
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


def action_info(action):
    return {
        "name": action.name,
        "frame_range": [float(n) for n in action.frame_range],
        "slot_identifiers": [slot.identifier for slot in action.slots],
        "fcurves": sum(
            len(bag.fcurves)
            for slot in action.slots
            for layer in action.layers
            for strip in layer.strips
            if (bag := strip.channelbag(slot)) is not None
        ),
    }


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)
    rigs = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    report = {
        "source": bpy.data.filepath,
        "blender": bpy.app.version_string,
        "fps": bpy.context.scene.render.fps,
        "rigs": [
            {
                "name": obj.name,
                "data_name": obj.data.name,
                "bones": len(obj.data.bones),
                "deform_bones": sum(b.use_deform for b in obj.data.bones),
                "bone_digest": digest_bones(obj.data),
                "matrix_world": [list(row) for row in obj.matrix_world],
                "nla_tracks": len(obj.animation_data.nla_tracks) if obj.animation_data else 0,
            }
            for obj in rigs
        ],
        "meshes": [
            {
                "name": obj.name,
                "data_name": obj.data.name,
                "rig": obj.find_armature().name if obj.find_armature() else None,
                "vertices": len(obj.data.vertices),
                "polygons": len(obj.data.polygons),
                "triangles": sum(len(face.vertices) - 2 for face in obj.data.polygons),
                "mesh_digest": digest_mesh(obj.data),
                "material_names": [slot.material.name if slot.material else None for slot in obj.material_slots],
                "matrix_world": [list(row) for row in obj.matrix_world],
            }
            for obj in meshes
        ],
        "actions": [action_info(action) for action in bpy.data.actions],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("SOURCE_AUDIT", json.dumps({
        "source": report["source"],
        "rigs": [(r["name"], r["bones"], r["bone_digest"][:12]) for r in report["rigs"]],
        "meshes": [(m["name"], m["triangles"], m["mesh_digest"][:12]) for m in report["meshes"]],
        "actions": [(a["name"], a["frame_range"], a["fcurves"]) for a in report["actions"]],
    }))


if __name__ == "__main__":
    main()
