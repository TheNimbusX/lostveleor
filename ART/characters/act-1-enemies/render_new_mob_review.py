"""Контрольные ракурсы Tripo-модели нового моба леса; сам GLB не меняет.

Запуск:
blender -b -P render_new_mob_review.py -- <model.glb> <pelag.fbx> <out_dir> <prefix> <height_m> [--mini 0.6] [--yaw 90] [--static-only]

По образцу 3.forest-elite-wendigo/review/render_model_review.py: четыре вида, игровая камера,
поворот и сравнение с Пелагом (1,8 м). --mini добавляет в сравнение копию на долю роста
(детёныш Расщепеня той же моделью).
"""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
model_path, pelag_path, output_path = (Path(value).resolve() for value in arguments[:3])
prefix = arguments[3]
target_height = float(arguments[4])
extra = arguments[5:]
static_only = "--static-only" in extra
mini = float(extra[extra.index("--mini") + 1]) if "--mini" in extra else 0.0
output_path.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(model_path))
mob = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
# Tripo по виду 3/4 ставит морду на +X; поворачиваем к +Y, где стоит камера «спереди».
yaw = float(extra[extra.index("--yaw") + 1]) if "--yaw" in extra else 90.0
for obj in [obj for obj in bpy.context.scene.objects if obj.parent is None]:
    # glTF ставит кватернионы — крутим матрицей, а не rotation_euler.
    obj.matrix_world = Matrix.Rotation(math.radians(yaw), 4, "Z") @ obj.matrix_world


def bounds(objects):
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    return (Vector(tuple(min(point[axis] for point in points) for axis in range(3))),
            Vector(tuple(max(point[axis] for point in points) for axis in range(3))))


def look_at(obj, point):
    obj.rotation_euler = (point - obj.location).to_track_quat("-Z", "Y").to_euler()


minimum, maximum = bounds(mob)
size = maximum - minimum
center = (minimum + maximum) * 0.5
triangles = sum(len(obj.data.polygons) for obj in mob)
print(f"MOB_MESHES={len(mob)} FACES={triangles} MATERIALS={len({mat.name for obj in mob for mat in obj.data.materials if mat})}")
print(f"MOB_RAW_BOUNDS={tuple(round(value, 4) for value in size)}")
print(f"MOB_BOUNDS_AT_TARGET={tuple(round(value * target_height / size.z, 3) for value in size)}")

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
    lamp.location = center + Vector(offset) * max(1.0, size.length / 3.0)
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
    scene.render.filepath = str(output_path / f"{prefix}_{name}.png")
    bpy.ops.render.render(write_still=True)

if not static_only:
    turntable_path = output_path / "turntable_frames"
    turntable_path.mkdir(exist_ok=True)
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    for frame in range(24):
        angle = 2 * math.pi * frame / 24
        vector = Vector((math.sin(angle), math.cos(angle), 0.22)).normalized()
        camera.location = center + vector * camera_distance
        look_at(camera, center)
        scene.render.filepath = str(turntable_path / f"{frame:02d}.png")
        bpy.ops.render.render(write_still=True)
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900

# Сравнение: моб в целевом росте, при --mini его копия, Пелаг 1,8 м.
mob_roots = [obj for obj in bpy.context.scene.objects if obj.name != camera.name and obj not in lights and obj.parent is None]
for obj in mob_roots:
    obj.scale *= target_height / size.z
bpy.context.view_layer.update()
mob_minimum, mob_maximum = bounds(mob)
mob_width = (mob_maximum - mob_minimum).x
for obj in mob_roots:
    obj.location += Vector((-mob_maximum.x - 0.4, 0, -mob_minimum.z))

compare = list(mob)
if mini > 0:
    copies = []
    for obj in mob_roots:
        dup = obj.copy()
        if obj.data:
            dup.data = obj.data
        scene.collection.objects.link(dup)
        copies.append(dup)
    for dup in copies:
        dup.scale *= mini
    bpy.context.view_layer.update()
    mini_meshes = [obj for obj in copies if obj.type == "MESH"]
    if mini_meshes:
        low, high = bounds(mini_meshes)
        for dup in copies:
            dup.location += Vector((-low.x + 0.3, 0, -low.z))
        compare += mini_meshes

before_import = set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(pelag_path), global_scale=100.0)
pelag_objects = [obj for obj in bpy.context.scene.objects if obj not in before_import]
pelag_meshes = [obj for obj in pelag_objects if obj.type == "MESH"]
pelag_minimum, pelag_maximum = bounds(pelag_meshes)
pelag_height = pelag_maximum.z - pelag_minimum.z
pelag_roots = [obj for obj in pelag_objects if obj.parent is None]
for obj in pelag_roots:
    obj.scale *= 1.8 / pelag_height
bpy.context.view_layer.update()
pelag_minimum, pelag_maximum = bounds(pelag_meshes)
_, placed_maximum = bounds(compare)
for obj in pelag_roots:
    obj.location += Vector((placed_maximum.x + 0.9 - pelag_minimum.x, 0, -pelag_minimum.z))

bpy.context.view_layer.update()
all_minimum, all_maximum = bounds(compare + pelag_meshes)
print(f"COMPARISON_BOUNDS={tuple(round(value, 4) for value in all_maximum - all_minimum)}")
scene.camera.data.ortho_scale = max((all_maximum - all_minimum).x * 1.2,
                                    (all_maximum - all_minimum).z * 1.3)
target = (all_minimum + all_maximum) * 0.5
for name, vector in (("vs_pelag", Vector((0, 1, 0.13))), ("vs_pelag_game", Vector((0.35, 1, 0.9)))):
    camera.location = target + vector.normalized() * 14
    look_at(camera, target)
    for lamp, offset in zip(lights, ((2.5, 3.5, 3.0), (-3.0, 2.0, 2.0), (0.0, -3.0, 3.2))):
        lamp.location = target + Vector(offset) * 1.6
        look_at(lamp, target)
    scene.render.filepath = str(output_path / f"{prefix}_{name}.png")
    bpy.ops.render.render(write_still=True)
