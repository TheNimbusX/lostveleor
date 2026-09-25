"""Собирает обратимый игровой кандидат из оригинального Tripo GLB."""

import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector


source_path, output_dir = (Path(value).resolve() for value in sys.argv[sys.argv.index("--") + 1:])
output_dir.mkdir(parents=True, exist_ok=True)
target_triangles = 24000

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source_path))
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(meshes) != 1:
    raise RuntimeError(f"Expected one Tripo mesh, found {len(meshes)}")
obj = meshes[0]
obj.name = "SM_ForestWendigo_LOD0_Candidate"
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

# Совпадающие точки разрезаны на UV-швах исходного GLB. UV хранятся на углах
# полигонов, поэтому геометрические вершины можно соединить без потери атласа.
mesh = obj.data
mesh.calc_loop_triangles()
source_triangles = len(mesh.loop_triangles)
source_vertices = len(mesh.vertices)
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.000001)
bm.to_mesh(mesh)
bm.free()
mesh.update()
mesh.calc_loop_triangles()
welded_vertices = len(mesh.vertices)
welded_triangles = len(mesh.loop_triangles)

ratio = min(1.0, target_triangles / welded_triangles)
modifier = obj.modifiers.new("SilhouetteReduction", "DECIMATE")
modifier.decimate_type = "COLLAPSE"
modifier.ratio = ratio
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh.calc_loop_triangles()
actual_triangles = len(mesh.loop_triangles)
if actual_triangles > 25000:
    raise RuntimeError(f"Export would exceed triangle budget: {actual_triangles}")

# Tripo нормализует персонажа к 1 м с центром в середине туловища.
# Игровая геометрия получает рост 3,1 м и точку опоры у земли.
bpy.context.view_layer.update()
points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
center_x = (minimum.x + maximum.x) * 0.5
center_y = (minimum.y + maximum.y) * 0.5
height = maximum.z - minimum.z
transform = Matrix.Scale(3.1 / height, 4) @ Matrix.Translation(Vector((-center_x, -center_y, -minimum.z)))
mesh.transform(transform @ obj.matrix_world)
obj.matrix_world.identity()
mesh.update()

obj.data.name = "SM_ForestWendigo_LOD0_Candidate"
blend_path = output_dir / "Wendigo_24k_candidate.blend"
glb_path = output_dir / "Wendigo_24k_candidate.glb"
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
bpy.ops.export_scene.gltf(filepath=str(glb_path), export_format="GLB", use_selection=True,
                          export_animations=False, export_apply=True)

report = {
    "source": str(source_path),
    "source_vertices": source_vertices,
    "source_triangles": source_triangles,
    "vertices_after_welding": welded_vertices,
    "triangles_after_welding": welded_triangles,
    "candidate_triangles": actual_triangles,
    "budget_triangles": 25000,
    "candidate_height_m": 3.1,
    "materials": len(obj.data.materials),
    "blend": str(blend_path),
    "glb": str(glb_path),
}
(output_dir / "optimization_report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))
