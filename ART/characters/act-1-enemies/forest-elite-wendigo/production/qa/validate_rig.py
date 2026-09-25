"""Read-only geometry/skin audit for the Forest Wendigo Blender deliverable."""

import json
import sys
from pathlib import Path

import bmesh
import bpy


def arg_after(flag, default=None):
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return args[args.index(flag) + 1] if flag in args and args.index(flag) + 1 < len(args) else default


def mesh_report(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundaries = sum(1 for edge in bm.edges if edge.is_boundary)
    overfull = sum(1 for edge in bm.edges if len(edge.link_faces) > 2)
    bm.free()

    armatures = [modifier.object for modifier in obj.modifiers
                 if modifier.type == "ARMATURE" and modifier.object is not None]
    if obj.parent is not None and obj.parent.type == "ARMATURE" and obj.parent not in armatures:
        armatures.append(obj.parent)
    deform_bones = {
        bone.name
        for armature in armatures
        for bone in armature.data.bones
        if bone.use_deform
    }
    unweighted = 0
    excessive_influences = 0
    unnormalized = 0
    worst_deviation = 0.0
    for vertex in mesh.vertices:
        weights = [
            group.weight for group in vertex.groups
            if obj.vertex_groups[group.group].name in deform_bones and group.weight > 1e-6
        ]
        if deform_bones and not weights:
            unweighted += 1
        if len(weights) > 4:
            excessive_influences += 1
        if weights:
            deviation = abs(sum(weights) - 1.0)
            worst_deviation = max(worst_deviation, deviation)
            if deviation > 0.005:
                unnormalized += 1
    return {
        "name": obj.name,
        "triangles": len(mesh.loop_triangles),
        "vertices": len(mesh.vertices),
        "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
        "armatures": [armature.name for armature in armatures],
        "boundary_edges": boundaries,
        "nonmanifold_three_or_more_face_edges": overfull,
        "unweighted_vertices": unweighted,
        "vertices_with_more_than_four_bone_influences": excessive_influences,
        "vertices_with_unnormalized_weights": unnormalized,
        "worst_weight_sum_deviation": round(worst_deviation, 6),
    }


objects = list(bpy.context.scene.objects)
meshes = [obj for obj in objects if obj.type == "MESH" and not obj.hide_render]
armatures = [obj for obj in objects if obj.type == "ARMATURE"]
reports = [mesh_report(obj) for obj in meshes]
actions = [
    {
        "name": action.name,
        "frame_range": [round(value, 3) for value in action.frame_range],
        "slot_count": len(action.slots),
    }
    for action in bpy.data.actions
]
report = {
    "blend": bpy.data.filepath,
    "scene_fps": bpy.context.scene.render.fps,
    "visible_meshes": reports,
    "visible_triangle_total": sum(item["triangles"] for item in reports),
    "triangle_budget": 25000,
    "within_triangle_budget": sum(item["triangles"] for item in reports) <= 25000,
    "armatures": [
        {
            "name": armature.name,
            "deform_bones": [bone.name for bone in armature.data.bones if bone.use_deform],
            "all_bones": len(armature.data.bones),
        }
        for armature in armatures
    ],
    "actions": actions,
}
target = arg_after("--out")
if target:
    path = Path(target).resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))
