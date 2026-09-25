"""Read-only round-trip check of exported Wendy rig formats in Blender."""

import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

out_dir = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
report = {}

for suffix, importer in (("fbx", bpy.ops.import_scene.fbx),
                         ("glb", bpy.ops.import_scene.gltf)):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    path = out_dir / f"ForestWendigo_Rig.{suffix}"
    importer(filepath=str(path))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    arms = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    count = 0
    boundaries = 0
    overfull = 0
    unweighted = 0
    excessive = 0
    bounds = []
    mats = []
    per_mesh = []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        count += len(obj.data.loop_triangles)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        boundaries += sum(edge.is_boundary for edge in bm.edges)
        overfull += sum(len(edge.link_faces) > 2 for edge in bm.edges)
        bm.free()
        unweighted += sum(not v.groups for v in obj.data.vertices)
        excessive += sum(len(v.groups) > 4 for v in obj.data.vertices)
        bounds += [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        mats += [slot.material.name for slot in obj.material_slots if slot.material]
        per_mesh.append({"name": obj.name, "triangles": len(obj.data.loop_triangles),
                         "vertices": len(obj.data.vertices),
                         "materials": [slot.material.name if slot.material else None
                                       for slot in obj.material_slots]})
    minimum = [min(point[axis] for point in bounds) for axis in range(3)]
    maximum = [max(point[axis] for point in bounds) for axis in range(3)]
    report[suffix] = {
        "file_bytes": path.stat().st_size,
        "mesh_count": len(meshes),
        "armature_count": len(arms),
        "bone_count": sum(len(arm.data.bones) for arm in arms),
        "triangles": count,
        "boundary_edges": boundaries,
        "overfull_edges": overfull,
        "unweighted_vertices": unweighted,
        "vertices_over_four_influences": excessive,
        "materials": mats,
        "per_mesh": per_mesh,
        "world_bounds_min": [round(value, 4) for value in minimum],
        "world_bounds_max": [round(value, 4) for value in maximum],
    }

(out_dir / "roundtrip_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
