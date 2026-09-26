"""Игровой кандидат нового моба леса из исходного Tripo GLB (по образцу Вендиго, optimize_candidate.py).

Запуск:
blender -b -P optimize_new_mob.py -- <model.glb> <out_dir> <Name> <height_m> [--triangles 24000] [--yaw -90]

Сваривает UV-швы, прореживает до бюджета (владелец 26.09: не больше 20–25 тыс. треугольников),
ставит точку опоры на землю, рост в метрах и морду на −Y (Tripo по виду 3/4 ставит её на +X).
"""

import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
source_path, output_dir = (Path(value).resolve() for value in arguments[:2])
name = arguments[2]
height_m = float(arguments[3])
extra = arguments[4:]
target_triangles = int(extra[extra.index("--triangles") + 1]) if "--triangles" in extra else 24000
yaw = float(extra[extra.index("--yaw") + 1]) if "--yaw" in extra else -90.0
budget = 25000
output_dir.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source_path))
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(meshes) != 1:
    raise RuntimeError(f"Expected one Tripo mesh, found {len(meshes)}")
obj = meshes[0]
# Родитель-пустышка glTF нам не нужен: переносим мировую матрицу на сам меш.
world = obj.matrix_world.copy()
obj.parent = None
obj.matrix_world = world
obj.name = f"SM_{name}_LOD0"
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

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

modifier = obj.modifiers.new("SilhouetteReduction", "DECIMATE")
modifier.decimate_type = "COLLAPSE"
modifier.ratio = min(1.0, target_triangles / welded_triangles)
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh.calc_loop_triangles()
actual_triangles = len(mesh.loop_triangles)
if actual_triangles > budget:
    raise RuntimeError(f"Export would exceed triangle budget: {actual_triangles}")

# Поворот к −Y, рост в метрах, опора у земли по центру следа.
mesh.transform(Matrix.Rotation(math.radians(yaw), 4, "Z") @ obj.matrix_world)
obj.matrix_world.identity()
mesh.update()
bpy.context.view_layer.update()
xs = [v.co.x for v in mesh.vertices]
ys = [v.co.y for v in mesh.vertices]
zs = [v.co.z for v in mesh.vertices]
height = max(zs) - min(zs)
transform = Matrix.Scale(height_m / height, 4) @ Matrix.Translation(
    Vector((-(min(xs) + max(xs)) * 0.5, -(min(ys) + max(ys)) * 0.5, -min(zs))))
mesh.transform(transform)
mesh.update()
xs = [v.co.x for v in mesh.vertices]
ys = [v.co.y for v in mesh.vertices]

obj.data.name = f"SM_{name}_LOD0"
stem = f"{name}_{actual_triangles // 1000}k_candidate"
blend_path = output_dir / f"{stem}.blend"
glb_path = output_dir / f"{stem}.glb"
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
    "budget_triangles": budget,
    "height_m": height_m,
    "footprint_m": [round(max(xs) - min(xs), 3), round(max(ys) - min(ys), 3)],
    "front_axis": "-Y",
    "materials": len(obj.data.materials),
    "blend": str(blend_path),
    "glb": str(glb_path),
}
(output_dir / "optimization_report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))
