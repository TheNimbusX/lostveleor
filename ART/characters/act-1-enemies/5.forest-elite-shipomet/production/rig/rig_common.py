"""Shared helpers for the ForestThorncaster rig scripts (headless Blender 5.2)."""
import colorsys
import math

import bpy
from mathutils import Vector

ARM_NAME = "ARM_ForestThorncaster"
MESH_NAME = "SM_ForestThorncaster_LOD0"


def load_meshy(path):
    """Import the Meshy GLB into an empty scene and drop the stray helper mesh."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    for obj in list(bpy.data.objects):
        if obj.type == "MESH" and obj.name.startswith("Icosphere"):
            bpy.data.objects.remove(obj, do_unlink=True)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, mesh


def find_rig():
    arm = bpy.data.objects.get(ARM_NAME) or next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = bpy.data.objects.get(MESH_NAME) or next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, mesh


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def setup_scene(res=720, engine="BLENDER_WORKBENCH"):
    scene = bpy.context.scene
    scene.render.engine = engine
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.fps = 30
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.view_settings.view_transform = "Standard"
    if engine == "BLENDER_WORKBENCH":
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.show_cavity = True
        shading.cavity_type = "WORLD"
        shading.background_type = "VIEWPORT"
        shading.background_color = (0.16, 0.17, 0.18)
    cam = bpy.data.objects.get("ProbeCamera")
    if cam is None:
        data = bpy.data.cameras.new("ProbeCamera")
        data.type = "ORTHO"
        cam = bpy.data.objects.new("ProbeCamera", data)
        scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, cam


VIEWS = {
    # name: camera direction from target (character faces -Y)
    "front": Vector((0, -1, 0)),
    "side": Vector((1, 0, 0)),      # looks at the character's left side
    "back": Vector((0, 1, 0)),
    "game": Vector((0, -1, 1.25)),  # rough top-down arena camera
    "threeq": Vector((0.8, -1, 0.35)),
}


def place_camera(cam, view, target=(0, 0, 1.35), ortho=3.2, dist=8.0):
    direction = VIEWS[view].normalized()
    cam.location = Vector(target) + direction * dist
    cam.data.ortho_scale = ortho
    look_at(cam, target)


def bone_palette(names):
    cols = {}
    n = len(names)
    for i, name in enumerate(names):
        h = (i * 0.618034) % 1.0
        r, g, b = colorsys.hsv_to_rgb(h, 0.75, 0.95)
        cols[name] = (r, g, b, 1.0)
    return cols


def tri_count(mesh_obj):
    return sum(len(p.vertices) - 2 for p in mesh_obj.data.polygons)


def deg(x):
    return math.radians(x)
