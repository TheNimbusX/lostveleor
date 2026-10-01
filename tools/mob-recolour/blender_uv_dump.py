"""Треугольники UV модели — в .npy, чтобы считать цвет только по тем пикселям атласа, которые реально лежат на теле.

blender -b --factory-startup -P blender_uv_dump.py -- <model.fbx|.glb> <out.npy>

Пишет массив float32 формы (N, 3, 2): N треугольников, по три UV-точки (u, v) первой UV-карты.
Растеризует их guardian_recolour.py — Blender нужен только чтобы прочитать FBX.
"""

import sys
from pathlib import Path

import bpy
import numpy as np


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, output_path = Path(arguments[0]).resolve(), Path(arguments[1]).resolve()
output_path.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
if model_path.suffix.lower() == ".fbx":
    bpy.ops.import_scene.fbx(filepath=str(model_path))
else:
    bpy.ops.import_scene.gltf(filepath=str(model_path))

triangles = []
for obj in bpy.context.scene.objects:
    if obj.type != "MESH" or not obj.data.uv_layers:
        continue
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv = mesh.uv_layers[0].data
    flat = np.empty(len(uv) * 2, dtype=np.float32)
    uv.foreach_get("uv", flat)
    flat = flat.reshape(-1, 2)
    loops = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int32)
    mesh.loop_triangles.foreach_get("loops", loops)
    triangles.append(flat[loops].reshape(-1, 3, 2))

result = np.concatenate(triangles) if triangles else np.zeros((0, 3, 2), np.float32)
np.save(str(output_path), result)
print(f"[uv] {model_path.name}: {len(result)} треугольников -> {output_path}")
