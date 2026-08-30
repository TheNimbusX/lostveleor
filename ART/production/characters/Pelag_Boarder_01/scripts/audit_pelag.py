import bpy
import json
import os
import sys
from mathutils import Vector


def argument(index, default):
    if "--" not in sys.argv:
        return default
    values = sys.argv[sys.argv.index("--") + 1 :]
    return values[index] if len(values) > index else default


def rounded(values):
    return [round(float(value), 6) for value in values]


def mesh_record(obj):
    mesh = obj.data
    triangle_count = sum(max(0, len(poly.vertices) - 2) for poly in mesh.polygons)
    world_corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = [min(corner[index] for corner in world_corners) for index in range(3)]
    maximum = [max(corner[index] for corner in world_corners) for index in range(3)]
    influences = 0
    for vertex in mesh.vertices:
        influences = max(influences, len([group for group in vertex.groups if group.weight > 0.0001]))
    return {
        "name": obj.name,
        "vertices": len(mesh.vertices),
        "triangles": triangle_count,
        "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
        "parent": obj.parent.name if obj.parent else None,
        "parent_type": obj.parent_type,
        "parent_bone": obj.parent_bone,
        "modifiers": [modifier.type for modifier in obj.modifiers],
        "shape_keys": list(mesh.shape_keys.key_blocks.keys()) if mesh.shape_keys else [],
        "max_influences": influences,
        "bounds_min": rounded(minimum),
        "bounds_max": rounded(maximum),
        "dimensions": rounded(obj.dimensions),
        "location": rounded(obj.location),
        "rotation_euler": rounded(obj.rotation_euler),
        "scale": rounded(obj.scale),
    }


def armature_record(obj):
    bones = []
    for bone in obj.data.bones:
        head = obj.matrix_world @ bone.head_local
        tail = obj.matrix_world @ bone.tail_local
        bones.append(
            {
                "name": bone.name,
                "parent": bone.parent.name if bone.parent else None,
                "deform": bool(bone.use_deform),
                "head": rounded(head),
                "tail": rounded(tail),
            }
        )
    return {"name": obj.name, "bones": bones}


output_path = os.path.abspath(argument(0, "pelag_audit.json"))
meshes = [mesh_record(obj) for obj in bpy.data.objects if obj.type == "MESH"]
armatures = [armature_record(obj) for obj in bpy.data.objects if obj.type == "ARMATURE"]

report = {
    "blender_version": bpy.app.version_string,
    "source_file": bpy.data.filepath,
    "scene_units": {
        "system": bpy.context.scene.unit_settings.system,
        "scale_length": bpy.context.scene.unit_settings.scale_length,
    },
    "mesh_count": len(meshes),
    "mesh_vertices": sum(record["vertices"] for record in meshes),
    "mesh_triangles": sum(record["triangles"] for record in meshes),
    "meshes": meshes,
    "armatures": armatures,
    "collections": {
        collection.name: sorted(obj.name for obj in collection.objects)
        for collection in bpy.data.collections
    },
}

os.makedirs(os.path.dirname(output_path), exist_ok=True)
with open(output_path, "w", encoding="utf-8") as stream:
    json.dump(report, stream, ensure_ascii=False, indent=2)

print(f"PELAG_AUDIT_WRITTEN={output_path}")
