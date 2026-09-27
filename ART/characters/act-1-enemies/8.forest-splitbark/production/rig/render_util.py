"""Small render helpers shared by the Splitter rig scripts (Workbench textured / EEVEE)."""
import math

import bpy
from mathutils import Vector


def setup_scene(engine="WORKBENCH", res=900, samples=16):
    scene = bpy.context.scene
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    if engine == "WORKBENCH":
        scene.render.engine = "BLENDER_WORKBENCH"
        sh = scene.display.shading
        sh.light = "STUDIO"
        sh.color_type = "TEXTURE"
        sh.show_cavity = True
        sh.cavity_type = "WORLD"
        sh.show_shadows = False
        sh.background_type = "VIEWPORT"
        sh.background_color = (0.16, 0.18, 0.2)
        scene.display.render_aa = "8"
    else:
        scene.render.engine = "BLENDER_EEVEE"
        try:
            scene.eevee.taa_render_samples = samples
        except AttributeError:
            pass
        world = scene.world or bpy.data.worlds.new("World")
        scene.world = world
        world.use_nodes = True
        bg = world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs[0].default_value = (0.2, 0.22, 0.24, 1)
            bg.inputs[1].default_value = 1.0
        if "KeySun" not in bpy.data.objects:
            sun = bpy.data.lights.new("KeySun", "SUN")
            sun.energy = 3.5
            so = bpy.data.objects.new("KeySun", sun)
            scene.collection.objects.link(so)
            so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    scene.view_settings.view_transform = "Standard"
    return scene


def camera(name="RigCam", ortho=None, lens=50):
    scene = bpy.context.scene
    cam = bpy.data.objects.get(name)
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        scene.collection.objects.link(cam)
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
        cam.data.lens = lens
    cam.data.clip_start = 0.01
    cam.data.clip_end = 100
    scene.camera = cam
    return cam


def look(cam, eye, target):
    cam.location = Vector(eye)
    d = Vector(target) - Vector(eye)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def shot(path, eye, target, ortho=None, lens=50):
    cam = camera(ortho=ortho, lens=lens)
    look(cam, eye, target)
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


# Named views around an object whose front faces -Y. target ~ body centre.
VIEWS = {
    "front": ((0, -4.0, 0.75), (0, 0, 0.6)),
    "back": ((0, 4.0, 0.75), (0, 0, 0.6)),
    "left": ((-4.0, 0, 0.7), (0, 0, 0.6)),   # character's right side... camera at -X
    "right": ((4.0, 0, 0.7), (0, 0, 0.6)),
    "top": ((0, -0.001, 4.5), (0, 0, 0.6)),
    "game": ((0, -3.2, 3.6), (0, 0, 0.5)),     # high 3/4 like the game camera
    "front34": ((-2.6, -3.0, 1.8), (0, 0, 0.6)),
    "back34": ((2.6, 3.0, 1.8), (0, 0, 0.6)),
    "below": ((0, -1.2, -3.5), (0, 0, 0.5)),
    "front34_L": ((2.6, -3.0, 1.8), (0, 0, 0.6)),
    "head_close": ((0.35, -1.7, 0.75), (0, -0.45, 0.52)),
    "head_side": ((1.5, -0.75, 0.6), (0, -0.45, 0.52)),
    "leg_close": ((1.9, -1.4, 0.7), (0.4, -0.3, 0.35)),
}
