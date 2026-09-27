"""Review render scene: Workbench textured + shadows, checker floor (1 m period), game and side cameras."""
import math

import bpy
from mathutils import Vector

GAME_AZ, GAME_EL = -40.0, 52.0     # azimuth 0 = camera in front of the face (-Y), minus = to the mob's right
TARGET = Vector((0.0, -0.25, 1.25))


def setup(res=640):
    s = bpy.context.scene
    s.render.engine = "BLENDER_WORKBENCH"
    s.render.resolution_x = s.render.resolution_y = res
    s.render.resolution_percentage = 100
    s.render.fps = 30
    s.render.image_settings.file_format = "PNG"
    s.view_settings.view_transform = "Standard"
    sh = s.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_shadows = True
    sh.shadow_intensity = 0.45
    sh.show_cavity = True
    sh.cavity_type = "WORLD"
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.30, 0.33, 0.34)
    s.display.light_direction = (0.35, 0.45, 0.82)
    s.display.shadow_shift = 0.02
    if s.world is None:
        s.world = bpy.data.worlds.new("World")
    cam = bpy.data.objects.get("REVIEW_Cam")
    if cam is None:
        cam = bpy.data.objects.new("REVIEW_Cam", bpy.data.cameras.new("REVIEW_Cam"))
        s.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.1, 200
    s.camera = cam
    return s, cam, floor()


def floor():
    ob = bpy.data.objects.get("REVIEW_Floor")
    if ob:
        return ob
    img = bpy.data.images.new("REVIEW_Checker", 64, 64)
    px = []
    for y in range(64):
        for x in range(64):
            c = 0.46 if ((x // 32) + (y // 32)) % 2 else 0.38
            line = x % 32 == 0 or y % 32 == 0
            c = 0.30 if line else c
            px += [c, c * 1.02, c * 0.94, 1.0]
    img.pixels = px
    size = 12.0
    me = bpy.data.meshes.new("REVIEW_Floor")
    me.from_pydata([(-size / 2, -size / 2, 0), (size / 2, -size / 2, 0), (size / 2, size / 2, 0), (-size / 2, size / 2, 0)], [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new()
    for li, (u, v) in zip(range(4), [(0, 0), (size, 0), (size, size), (0, size)]):
        uv.data[li].uv = (u / 2, v / 2)     # checker image = 2x2 cells of 0.5 m -> 1 m period
    mat = bpy.data.materials.new("REVIEW_Floor")
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes.active = tex
    me.materials.append(mat)
    ob = bpy.data.objects.new("REVIEW_Floor", me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def place(cam, view, scale=None, target=None):
    t = TARGET if target is None else Vector(target)
    if view == "game":
        az, el = math.radians(GAME_AZ), math.radians(GAME_EL)
        d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
        cam.data.ortho_scale = scale or 4.6
    else:  # side: from the mob's left (+X), slightly above
        d = Vector((1.0, 0.0, 0.08)).normalized()
        cam.data.ortho_scale = scale or 4.4
    cam.location = t + d * 30
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
