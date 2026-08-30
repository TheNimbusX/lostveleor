import bpy
import math
import os
import sys
from mathutils import Vector


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_samples(asset, output_dir, count=12):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=asset, use_anim=True)
    armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    armature.animation_data_create()
    armature.animation_data.action = action

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.render.resolution_x = 360
    scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("QA_World")
    scene.world.color = (0.055, 0.065, 0.08)

    camera_data = bpy.data.cameras.new("QA_Ortho")
    camera = bpy.data.objects.new("QA_Ortho", camera_data)
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 1.35
    camera.location = (1.8, -3.6, 1.65)
    look_at(camera, (0.0, 0.0, 0.5))
    scene.camera = camera

    start, end = action.frame_range
    frames = [round(start + (end - start) * i / max(1, count - 1)) for i in range(count)]
    os.makedirs(output_dir, exist_ok=True)
    for index, frame in enumerate(frames):
        scene.frame_set(frame)
        scene.render.filepath = os.path.join(output_dir, f"frame_{index:02d}_f{frame:04d}.png")
        bpy.ops.render.render(write_still=True)
    print(f"SHEET={os.path.basename(asset)}|range={start}-{end}|frames={frames}|out={output_dir}")


args = sys.argv[sys.argv.index("--") + 1:]
if len(args) % 2:
    raise RuntimeError("Expected input/output pairs")
for i in range(0, len(args), 2):
    render_samples(os.path.abspath(args[i]), os.path.abspath(args[i + 1]))
