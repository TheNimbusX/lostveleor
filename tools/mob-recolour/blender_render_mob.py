"""Моб под игровым углом с ЗАДАННОЙ текстурой цвета — для сравнения палитр до/после без Unity.

blender -b --factory-startup -P blender_render_mob.py -- <model.fbx> <albedo.png|jpg> <out.png>
        [--azimuth -35] [--elevation 52] [--size 900] [--frame 12] [--floor 0.30,0.21,0.15]
        [--height 2.4] [--mask <silhouette.png>]

Все мобы рендерятся ОДНИМ светом и одним простым диффузным материалом: сравниваются краски атласа,
а не шейдеры (у Корнехвата в игре URP Lit с нормалями, у Хранителя — Texture Toon). Пол — цвет земли
арены, чтобы сразу видно, читается ли силуэт. Модель масштабируется к одной высоте (--height, м).
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, albedo_path, output_path = (Path(a).resolve() for a in arguments[:3])
extra = arguments[3:]


def option(name, default, cast=float):
    return cast(extra[extra.index(name) + 1]) if name in extra else default


azimuth = option("--azimuth", -35.0)
elevation = option("--elevation", 52.0)
size = option("--size", 900, int)
frame = option("--frame", 12, int)
height = option("--height", 2.4)
floor_rgb = tuple(float(x) for x in option("--floor", "0.30,0.21,0.15", str).split(","))
output_path.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(model_path))
scene = bpy.context.scene
scene.frame_set(frame)

meshes = [obj for obj in scene.objects if obj.type == "MESH"]
roots = [obj for obj in scene.objects if obj.parent is None]

# Один материал на всё тело: альбедо как есть, без блика — сравниваем краску.
material = bpy.data.materials.new("Albedo")
material.use_nodes = True
nodes = material.node_tree.nodes
bsdf = nodes["Principled BSDF"]
image = nodes.new("ShaderNodeTexImage")
image.image = bpy.data.images.load(str(albedo_path))
image.image.colorspace_settings.name = "sRGB"
material.node_tree.links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 1.0
if "Specular IOR Level" in bsdf.inputs:
    bsdf.inputs["Specular IOR Level"].default_value = 0.0
for obj in meshes:
    obj.data.materials.clear()
    obj.data.materials.append(material)

# Высота к общему знаменателю, ноги на пол.
bpy.context.view_layer.update()


def bounds():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        points += [evaluated.matrix_world @ v.co for v in mesh.vertices]
        evaluated.to_mesh_clear()
    low = Vector(tuple(min(p[a] for p in points) for a in range(3)))
    high = Vector(tuple(max(p[a] for p in points) for a in range(3)))
    return low, high


low, high = bounds()
scale = height / max(1e-4, high.z - low.z)
for root in roots:
    root.scale = root.scale * scale
bpy.context.view_layer.update()
low, high = bounds()
offset = Vector(((low.x + high.x) * -0.5, (low.y + high.y) * -0.5, -low.z))
for root in roots:
    root.location = root.location + offset
bpy.context.view_layer.update()
low, high = bounds()
center = (low + high) * 0.5

bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
floor = bpy.context.active_object
floor_material = bpy.data.materials.new("Floor")
floor_material.use_nodes = True
floor_bsdf = floor_material.node_tree.nodes["Principled BSDF"]
floor_bsdf.inputs["Base Color"].default_value = (*[c ** 2.2 for c in floor_rgb], 1)
floor_bsdf.inputs["Roughness"].default_value = 1.0
if "Specular IOR Level" in floor_bsdf.inputs:
    floor_bsdf.inputs["Specular IOR Level"].default_value = 0.0
floor.data.materials.append(floor_material)

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = size
scene.render.resolution_y = size
scene.render.film_transparent = False
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.view_settings.view_transform = "Standard"
scene.world.use_nodes = True
background = scene.world.node_tree.nodes["Background"]
background.inputs[0].default_value = (0.62, 0.66, 0.74, 1)
background.inputs[1].default_value = 0.55

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.2
sun.data.color = (1.0, 0.95, 0.86)
sun.data.angle = math.radians(8)
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(-12), math.radians(-35))

camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = "ORTHO"
camera.data.ortho_scale = height * 1.45
direction = Vector((math.sin(math.radians(azimuth)) * math.cos(math.radians(elevation)),
                    -math.cos(math.radians(azimuth)) * math.cos(math.radians(elevation)),
                    math.sin(math.radians(elevation))))
camera.location = center + direction * 30
camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = str(output_path)
bpy.ops.render.render(write_still=True)
print(f"[render] {model_path.name} + {albedo_path.name} -> {output_path}")

# Силуэт тем же кадром: пол спрятан, фон прозрачный — альфа и есть маска тела для замера
# «насколько тело отличается от земли арены».
if "--mask" in extra:
    floor.hide_render = True
    scene.render.film_transparent = True
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = str(Path(extra[extra.index("--mask") + 1]).resolve())
    bpy.ops.render.render(write_still=True)
