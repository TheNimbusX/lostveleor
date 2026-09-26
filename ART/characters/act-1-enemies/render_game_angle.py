"""Моб под игровым углом на прозрачном фоне — для склейки с кадром игры перед целевыми кадрами эффектов.

blender -b -P render_game_angle.py -- <candidate.glb> <out.png> [--azimuth -60] [--elevation 52] [--size 1024]
Кандидат смотрит на −Y; азимут 0 — камера спереди, минус — камера левее морды.
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, output_path = Path(arguments[0]).resolve(), Path(arguments[1]).resolve()
extra = arguments[2:]
azimuth = float(extra[extra.index("--azimuth") + 1]) if "--azimuth" in extra else -60.0
elevation = float(extra[extra.index("--elevation") + 1]) if "--elevation" in extra else 52.0
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
center = (low + high) * 0.5

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = size
scene.render.resolution_y = size
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.6, 0.5, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
scene.view_settings.view_transform = "Standard"

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.4
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(35), math.radians(-10), math.radians(-40))

camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.ortho_scale = max(high - low) * 1.25
direction = Vector((math.sin(math.radians(azimuth)) * math.cos(math.radians(elevation)),
                    -math.cos(math.radians(azimuth)) * math.cos(math.radians(elevation)),
                    math.sin(math.radians(elevation))))
camera.location = center + direction * 20
camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = str(output_path)
bpy.ops.render.render(write_still=True)
print(f"GAME_ANGLE={output_path} ORTHO={camera.data.ortho_scale:.3f} HEIGHT={high.z - low.z:.3f}")
