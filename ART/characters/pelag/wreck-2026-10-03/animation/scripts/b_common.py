"""Общие функции Blender-скриптов серии сабли (запуск: blender -b -P <скрипт> -- ...)."""
import bpy, math, os
from mathutils import Vector, Matrix

# Клинок в «анатомической» рамке кисти: a — к среднему пальцу, b — от мизинца к
# указательному, c — третья ось. Снято в Unity с игровой посадки сабли
# (ArenaView.WoleWeaponLocal*), длины — в длинах предплечья (локоть–кисть).
BLADE_ROOT = (0.2067, -1.0333, 0.1372)
BLADE_TIP = (1.6970, 3.3069, 0.1609)

def wpos(arm, name):
    return arm.matrix_world @ arm.pose.bones[name].head

def hand_frame(arm, side="Right"):
    h = wpos(arm, f"mixamorig:{side}Hand")
    mid = wpos(arm, f"mixamorig:{side}HandMiddle1")
    idx = wpos(arm, f"mixamorig:{side}HandIndex1")
    pk = wpos(arm, f"mixamorig:{side}HandPinky1")
    fore = wpos(arm, f"mixamorig:{side}ForeArm")
    a = (mid - h).normalized()
    b = (idx - pk); b = (b - b.dot(a) * a).normalized()
    c = -(a.cross(b))  # Unity левосторонний: векторное произведение меняет знак
    return h, a, b, c, (h - fore).length

def blade(arm):
    h, a, b, c, L = hand_frame(arm)
    r = h + L * (BLADE_ROOT[0] * a + BLADE_ROOT[1] * b + BLADE_ROOT[2] * c)
    t = h + L * (BLADE_TIP[0] * a + BLADE_TIP[1] * b + BLADE_TIP[2] * c)
    return r, t

def make_blade_object(color=(0.95, 0.85, 0.2, 1)):
    mesh = bpy.data.meshes.new("blade")
    verts = [(-0.5, 0, -0.5), (0.5, 0, -0.5), (0.5, 0, 0.5), (-0.5, 0, 0.5),
             (-0.5, 1, -0.5), (0.5, 1, -0.5), (0.5, 1, 0.5), (-0.5, 1, 0.5)]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    mesh.from_pydata(verts, [], faces)
    o = bpy.data.objects.new("blade", mesh)
    bpy.context.scene.collection.objects.link(o)
    o.color = color
    return o

def place_blade(o, arm, thickness):
    r, t = blade(arm)
    d = t - r
    y = d.normalized()
    x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    m = Matrix((x * thickness, y * d.length, z * thickness)).transposed()
    o.matrix_world = Matrix.Translation(r) @ m.to_4x4()

def setup_render(res=360, engine='BLENDER_WORKBENCH'):
    sc = bpy.context.scene
    sc.render.engine = engine
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'OBJECT'
    sc.render.resolution_x = res; sc.render.resolution_y = res
    w = bpy.data.worlds.new("w"); w.color = (0.45, 0.45, 0.45); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'
    return cam

def aim_camera(cam, target, view, height, scale):
    """view: front (персонаж смотрит на камеру), side, game (изометрия 48°, вид сзади-сверху)."""
    cam.data.ortho_scale = scale
    if view == "front":
        cam.location = target + Vector((0, -10 * height, 0))
        cam.rotation_euler = (math.radians(90), 0, 0)
    elif view == "side":
        cam.location = target + Vector((10 * height, 0, 0))
        cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    elif view == "back":
        cam.location = target + Vector((0, 10 * height, 0))
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    elif view == "top":
        cam.location = target + Vector((0, 0, 10 * height))
        cam.rotation_euler = (0, 0, 0)
    else:  # игровая камера: сверху под 48° от горизонта, герой смотрит вправо-вверх экрана
        pitch = math.radians(48)
        yaw = math.radians(135)
        dist = 10 * height
        d = Vector((math.cos(pitch) * math.sin(yaw), -math.cos(pitch) * math.cos(yaw), math.sin(pitch)))
        cam.location = target + d * dist
        cam.rotation_euler = (math.radians(90) - pitch, 0, yaw)
