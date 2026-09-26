"""Стартовый кадр для рефа движения Higgsfield: утверждённая модель на светлой земле, вид 3/4 сбоку.

Запуск:
blender -b -P render_ref_start.py -- <candidate.glb> <out.png> [--azimuth -40] [--elevation 12] [--size 1024]

Кандидат смотрит на −Y (optimize_new_mob.py). Азимут отсчитывается от вида спереди, минус — левее.
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, output_path = Path(arguments[0]).resolve(), Path(arguments[1]).resolve()
extra = arguments[2:]
azimuth = float(extra[extra.index("--azimuth") + 1]) if "--azimuth" in extra else -40.0
elevation = float(extra[extra.index("--elevation") + 1]) if "--elevation" in extra else 12.0
size = int(extra[extra.index("--size") + 1]) if "--size" in extra else 1024
output_path.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(model_path))
mob = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
bpy.context.view_layer.update()
points = [obj.matrix_world @ Vector(corner) for obj in mob for corner in obj.bound_box]
low = Vector(tuple(min(p[a] for p in points) for a in range(3)))
high = Vector(tuple(max(p[a] for p in points) for a in range(3)))
dims = high - low
center = (low + high) * 0.5

bpy.ops.mesh.primitive_plane_add(size=max(dims.x, dims.y) * 12, location=(center.x, center.y, low.z))
ground = bpy.context.active_object
material = bpy.data.materials.new("Ground")
material.use_nodes = True
material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.47, 0.5, 0.42, 1)
material.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
ground.data.materials.append(material)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = size
scene.render.resolution_y = size
scene.render.image_settings.file_format = "PNG"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.66, 0.6, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
scene.view_settings.view_transform = "Standard"


def look_at(obj, point):
    obj.rotation_euler = (point - obj.location).to_track_quat("-Z", "Y").to_euler()


sun_data = bpy.data.lights.new("Sun", "SUN")
sun_data.energy = 3.2
sun_data.angle = math.radians(8)
sun = bpy.data.objects.new("Sun", sun_data)
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(-15), math.radians(-35))

camera_data = bpy.data.cameras.new("Camera")
camera_data.lens = 50
camera = bpy.data.objects.new("Camera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
# Спереди — это −Y. Кадр с запасом: персонаж занимает ~60% высоты, чтобы в движении не вылетать.
direction = Vector((math.sin(math.radians(azimuth)), -math.cos(math.radians(azimuth)), math.tan(math.radians(elevation)))).normalized()
extent = max(dims.z, dims.x, dims.y)
distance = extent / 0.6 / (2 * math.tan(camera_data.angle / 2))
target = Vector((center.x, center.y, low.z + dims.z * 0.45))
camera.location = target + direction * distance
look_at(camera, target)

scene.render.filepath = str(output_path)
bpy.ops.render.render(write_still=True)
print(f"START_FRAME={output_path} DIMS={tuple(round(v, 3) for v in dims)}")
