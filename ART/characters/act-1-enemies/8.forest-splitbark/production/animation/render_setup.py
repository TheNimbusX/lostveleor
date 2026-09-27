"""Review scene for the baked takes: EEVEE, sun + sky, checker ground, game (52 deg, 3/4) and side cameras."""
import math

import bpy
from mathutils import Vector

RES = 480
GAME_EL, GAME_AZ, GAME_DIST = 52.0, 45.0, 3.6
TARGET = Vector((0.0, -0.1, 0.45))


def setup():
    s = bpy.context.scene
    s.render.engine = "BLENDER_EEVEE"
    try:
        s.eevee.taa_render_samples = 16
    except AttributeError:
        pass
    s.render.resolution_x = s.render.resolution_y = RES
    s.render.resolution_percentage = 100
    s.render.image_settings.file_format = "PNG"
    s.render.film_transparent = False
    s.view_settings.view_transform = "Standard"
    world = s.world or bpy.data.worlds.new("World")
    s.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.62, 0.66, 0.66, 1)
    bg.inputs[1].default_value = 0.9
    sun = bpy.data.lights.new("ReviewSun", "SUN")
    sun.energy = 3.2
    sun.angle = math.radians(8)
    so = bpy.data.objects.new("ReviewSun", sun)
    s.collection.objects.link(so)
    so.rotation_euler = (math.radians(40), math.radians(12), math.radians(-30))
    return s


def ground():
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    g = bpy.context.active_object
    g.name = "ReviewGround"
    mat = bpy.data.materials.new("ReviewGround")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    chk = nt.nodes.new("ShaderNodeTexChecker")
    chk.inputs["Scale"].default_value = 80.0  # 40 m plane -> 0.5 m tiles, pattern repeats every 1 m
    chk.inputs["Color1"].default_value = (0.50, 0.56, 0.47, 1)
    chk.inputs["Color2"].default_value = (0.44, 0.50, 0.41, 1)
    coord = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(coord.outputs["Generated"], chk.inputs["Vector"])
    nt.links.new(chk.outputs["Color"], bsdf.inputs["Base Color"])
    g.data.materials.append(mat)
    return g


def camera(name):
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    bpy.context.scene.collection.objects.link(cam)
    cam.data.clip_start, cam.data.clip_end = 0.05, 100
    return cam


def look(cam, eye, target):
    cam.location = Vector(eye)
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()


def game_camera(dist=GAME_DIST, target=TARGET):
    cam = camera("CAM_Game")
    cam.data.lens = 50
    el, az = math.radians(GAME_EL), math.radians(GAME_AZ)
    eye = target + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * dist
    look(cam, eye, target)
    return cam


def side_camera():
    cam = camera("CAM_Side")
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.5
    t = Vector((0.0, -0.15, 0.55))
    look(cam, t + Vector((6.0, 0.0, 0.5)), t)
    return cam
