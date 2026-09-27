"""Workbench review renders: textured mob, checker ground (can scroll for in-place walks), ortho cameras.
Game camera: 52 deg down, three-quarter front (creature faces -Y; camera sits front-left of it)."""
import math
import bpy
from mathutils import Vector

GAME_EL, GAME_AZ = 52.0, 35.0
CELL = 0.5  # checker cell, metres


def view_dir(name):
    if name == "game":
        el, az = math.radians(GAME_EL), math.radians(GAME_AZ)
        return Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))
    if name == "side":  # creature's left profile (+X), nearly level
        return Vector((1, 0, 0.12)).normalized()
    if name == "front":
        return Vector((0, -1, 0.1)).normalized()
    raise KeyError(name)


def setup(res=(640, 480)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_cavity = True
    sh.show_shadows = True
    sh.shadow_intensity = 0.45
    sc.display.light_direction = (0.35, -0.45, 0.82)
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("W")
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.56, 0.60, 0.58)
    for o in sc.objects:
        if o.type == "ARMATURE":
            o.hide_render = True
    # checker ground
    img = bpy.data.images.new("GroundChecker", 64, 64)
    px = []
    for y in range(64):
        for x in range(64):
            c = 0.66 if ((x // 32) + (y // 32)) % 2 else 0.78
            px += [c, c * 1.03, c * 0.97, 1.0]
    img.pixels = px
    bpy.ops.mesh.primitive_plane_add(size=16, location=(0, 0, -0.001))
    ground = bpy.context.object
    ground.name = "REVIEW_Ground"
    uv = ground.data.uv_layers.active.data
    for loop in uv:
        loop.uv = loop.uv * (16 / (2 * CELL))
    mat = bpy.data.materials.new("ReviewGround")
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    ground.data.materials.append(mat)
    cd = bpy.data.cameras.new("REVIEW_Cam")
    cd.type = "ORTHO"
    cd.clip_end = 200
    cam = bpy.data.objects.new("REVIEW_Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return ground, cam


def place_camera(cam, view, center, ortho):
    d = view_dir(view)
    cam.location = Vector(center) + d * 40
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = ortho


def scroll_ground(ground, frame, speed_per_frame):
    """In-place locomotion: move the checker under the mob so planted contacts read as planted."""
    period = 2 * CELL
    ground.location.y = (speed_per_frame * frame) % period
