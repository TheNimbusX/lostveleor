"""Контрольные ракурсы исходной Tripo-модели; не меняет сам GLB."""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, pelag_path, output_path = (Path(value).resolve() for value in arguments[:3])
static_only = "--static-only" in arguments[3:]
output_path.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(model_path))
wendigo = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]


def bounds(objects):
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    return (Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
            Vector(tuple(max(point[axis] for point in points) for axis in range(3))))


def look_at(obj, point):
    obj.rotation_euler = (point - obj.location).to_track_quat("-Z", "Y").to_euler()


minimum, maximum = bounds(wendigo)
size = maximum - minimum
center = (minimum + maximum) * 0.5
print(f"WENDIGO_MESHES={len(wendigo)} MATERIALS={len({mat.name for obj in wendigo for mat in obj.data.materials if mat})}")
print(f"WENDIGO_RAW_BOUNDS={tuple(round(value, 4) for value in size)}")

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.use_nodes = True
scene.world.node_tree.nodes.get("Background").inputs[0].default_value = (0.105, 0.12, 0.12, 1)
scene.world.node_tree.nodes.get("Background").inputs[1].default_value = 0.65
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"

lights = []
for name, offset, energy, color in (
    ("Key", (2.5, 3.5, 3.0), 650, (1.0, 0.91, 0.78)),
    ("Fill", (-3.0, 2.0, 2.0), 450, (0.72, 0.86, 1.0)),
    ("Rim", (0.0, -3.0, 3.2), 850, (1.0, 0.96, 0.85)),
):
    lamp_data = bpy.data.lights.new(name, "AREA")
    lamp_data.energy = energy
    lamp_data.shape = "DISK"
    lamp_data.size = 3.0
    lamp_data.color = color
    lamp = bpy.data.objects.new(name, lamp_data)
    scene.collection.objects.link(lamp)
    lamp.location = center + Vector(offset)
    look_at(lamp, center)
    lights.append(lamp)

camera_data = bpy.data.cameras.new("ReviewCamera")
camera = bpy.data.objects.new("ReviewCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = max(size.z * 1.18, size.x * 1.25, size.y * 1.25)

camera_distance = max(size.length * 2.2, 8)

for name, vector in {
    "front": Vector((0, 1, 0.13)),
    "left": Vector((-1, 0, 0.13)),
    "back": Vector((0, -1, 0.13)),
    "right": Vector((1, 0, 0.13)),
    "game_camera": Vector((1, 1, 0.8)),
}.items():
    camera.location = center + vector.normalized() * camera_distance
    look_at(camera, center)
    scene.render.filepath = str(output_path / f"wendigo_{name}.png")
    bpy.ops.render.render(write_still=True)

if not static_only:
    turntable_path = output_path / "turntable_frames"
    turntable_path.mkdir(exist_ok=True)
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    for frame in range(20):
        angle = 2 * math.pi * frame / 20
        vector = Vector((math.sin(angle), math.cos(angle), 0.22)).normalized()
        camera.location = center + vector * camera_distance
        look_at(camera, center)
        scene.render.filepath = str(turntable_path / f"{frame:02d}.png")
        bpy.ops.render.render(write_still=True)
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900

# Сравнение силуэтов после приведения вендиго к целевым 3,1 м и Пелага к 1,8 м.
wendigo_roots = [obj for obj in bpy.context.scene.objects if obj.name != camera.name and obj not in lights and obj.parent is None]
for obj in wendigo_roots:
    obj.scale *= 3.1 / size.z
bpy.context.view_layer.update()
wendigo_minimum, wendigo_maximum = bounds(wendigo)
for obj in wendigo_roots:
    obj.location += Vector((-1.5, 0, -wendigo_minimum.z))

before_import = set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(pelag_path), global_scale=100.0)
pelag_objects = [obj for obj in bpy.context.scene.objects if obj not in before_import]
pelag_meshes = [obj for obj in pelag_objects if obj.type == "MESH"]
pelag_minimum, pelag_maximum = bounds(pelag_meshes)
pelag_height = pelag_maximum.z - pelag_minimum.z
print(f"PELAG_RAW_HEIGHT={pelag_height:.4f} MESHES={len(pelag_meshes)}")
print(f"PELAG_OBJECTS={[(obj.name, obj.parent.name if obj.parent else None, obj.hide_render) for obj in pelag_objects]}")
pelag_roots = [obj for obj in pelag_objects if obj.parent is None]
for obj in pelag_roots:
    obj.scale *= 1.8 / pelag_height
bpy.context.view_layer.update()
pelag_minimum, pelag_maximum = bounds(pelag_meshes)
print(f"PELAG_SCALED_BOUNDS={tuple(round(value, 4) for value in pelag_maximum - pelag_minimum)}")
for obj in pelag_roots:
    obj.location += Vector((1.7, 0, -pelag_minimum.z))

bpy.context.view_layer.update()
all_minimum, all_maximum = bounds(wendigo + pelag_meshes)
print(f"COMPARISON_BOUNDS={tuple(round(value, 4) for value in all_maximum - all_minimum)}")
scene.camera.data.ortho_scale = max((all_maximum - all_minimum).x * 1.25,
                                    (all_maximum - all_minimum).z * 1.18)
target = (all_minimum + all_maximum) * 0.5
camera.location = target + Vector((0, 1, 0.13)).normalized() * 12
look_at(camera, target)
for lamp, offset in zip(lights, ((2.5, 3.5, 3.0), (-3.0, 2.0, 2.0), (0.0, -3.0, 3.2))):
    lamp.location = target + Vector(offset) * 1.4
    look_at(lamp, target)
scene.render.filepath = str(output_path / "wendigo_vs_pelag.png")
bpy.ops.render.render(write_still=True)
