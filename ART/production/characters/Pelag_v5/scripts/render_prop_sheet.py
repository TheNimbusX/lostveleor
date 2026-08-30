import bpy
import math
import os
import sys
from mathutils import Vector


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_asset(asset, output_dir):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=asset, use_anim=False)
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    mins = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
    upper = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
    center = (mins + upper) * 0.5
    size = max(upper - mins)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = 420
    scene.render.resolution_y = 420
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("QA_World")
    scene.world.color = (0.055, 0.065, 0.08)

    camera_data = bpy.data.cameras.new("QA_Ortho")
    camera = bpy.data.objects.new("QA_Ortho", camera_data)
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = size * 1.35
    scene.camera = camera

    directions = [
        (0, -1, 0), (1, -1, 0.35), (1, 0, 0), (1, 1, 0.35),
        (0, 1, 0), (-1, 1, 0.35), (-1, 0, 0), (0.25, -0.5, 1),
    ]
    os.makedirs(output_dir, exist_ok=True)
    for index, direction in enumerate(directions):
        d = Vector(direction)
        d.normalize()
        camera.location = center + d * (size * 4)
        look_at(camera, center)
        scene.render.filepath = os.path.join(output_dir, f"view_{index:02d}.png")
        bpy.ops.render.render(write_still=True)
    print(f"PROP={os.path.basename(asset)}|min={tuple(mins)}|max={tuple(upper)}|out={output_dir}")


args = sys.argv[sys.argv.index("--") + 1:]
if len(args) % 2:
    raise RuntimeError("Expected input/output pairs")
for i in range(0, len(args), 2):
    render_asset(os.path.abspath(args[i]), os.path.abspath(args[i + 1]))
