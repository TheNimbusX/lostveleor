"""Audit deformed Pelag FBX meshes against the z=0 ground plane.

This is deliberately a read-only content check apart from its JSON report.
The FBX is imported exactly as Blender/Unity-facing content, then every
deformed mesh vertex is sampled over the exported action range.
"""

from __future__ import annotations

import json
from pathlib import Path

import bpy


PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
EXPORTS = PROJECT / "artifacts/pelag-animation-build/exports"
REPORT = PROJECT / "artifacts/pelag-animation-build/mesh-clearance-audit.json"


def clear_scene() -> None:
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def audit(path: Path) -> dict[str, object]:
    clear_scene()
    result = bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    if "FINISHED" not in result:
        raise RuntimeError(f"FBX import failed: {path}")
    armature = next((obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None)
    if armature is None or armature.animation_data is None or armature.animation_data.action is None:
        raise RuntimeError(f"No animated armature in {path}")
    action = armature.animation_data.action
    start = round(action.frame_range[0])
    end = round(action.frame_range[1])
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not mesh_objects:
        raise RuntimeError(f"No mesh in {path}")
    depsgraph = bpy.context.evaluated_depsgraph_get()
    frame_stats: list[dict[str, object]] = []
    min_z = float("inf")
    min_frame = start
    min_vertex = None
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        frame_min = float("inf")
        frame_max = float("-inf")
        frame_count = 0
        for obj in mesh_objects:
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            try:
                matrix = evaluated.matrix_world
                for vertex in mesh.vertices:
                    world = matrix @ vertex.co
                    z = float(world.z)
                    frame_min = min(frame_min, z)
                    frame_max = max(frame_max, z)
                    frame_count += 1
                    if z < min_z:
                        min_z = z
                        min_frame = frame
                        min_vertex = {
                            "object": obj.name,
                            "index": int(vertex.index),
                            "xyz": [float(world.x), float(world.y), z],
                        }
            finally:
                evaluated.to_mesh_clear()
        frame_stats.append({
            "frame": frame,
            "min_z_m": frame_min,
            "max_z_m": frame_max,
            "vertex_count": frame_count,
        })
    return {
        "file": str(path),
        "frame_range": [start, end],
        "fps": bpy.context.scene.render.fps,
        "mesh_objects": [obj.name for obj in mesh_objects],
        "min_mesh_z_m": min_z,
        "min_mesh_frame": min_frame,
        "min_mesh_vertex": min_vertex,
        "penetration_depth_m": max(0.0, -min_z),
        "frames": frame_stats,
    }


payload = {
    "ground_z_m": 0.0,
    "clips": [audit(path) for path in sorted(EXPORTS.glob("Pelag_MX_*.fbx"))],
}
REPORT.parent.mkdir(parents=True, exist_ok=True)
REPORT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(json.dumps({
    "clips": len(payload["clips"]),
    "minima": {Path(item["file"]).stem: item["min_mesh_z_m"] for item in payload["clips"]},
}, indent=2))
