"""Шквал v2: лист поз (игровая камера + сбоку) и GIF серии из 4 прыжков в масштабе игры.

blender -b --factory-startup -P sq_render.py -- <anim_dir> <frames_dir> sheet
blender -b --factory-startup -P sq_render.py -- <anim_dir> <frames_dir> gif
Читает выгруженные FBX и Pelag_AN_Squall2Bind.fbx, перенос на v6 — формула Unity (sq_view.py).
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sq_view import Transfer, TEX, BLADE_SHOW
from b_common import make_blade_object, blade

argv = sys.argv[sys.argv.index("--") + 1:]
anim, out, mode = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
P = "Pelag_AN_Squall2_"
CLIPS = [P + c for c in ("Load", "Forehand", "Backhand", "FinishFore", "FinishBack", "ReturnFore", "ReturnBack")]
TR = Transfer(anim, CLIPS, "Pelag_AN_Squall2Bind")
sc, tgt = TR.sc, TR.tgt

# ---- сцена: модель в цвете, якорь-заглушка на спине, клинок, земля ----------------------
mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEX)
nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
for m in TR.meshes:
    m.data.materials.clear(); m.data.materials.append(mat)
iron = (0.16, 0.16, 0.18, 1)
iron_m = bpy.data.materials.new("iron"); iron_m.diffuse_color = iron


def box(name, a, b, w):
    d = b - a
    me = bpy.data.meshes.new(name)
    v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
    me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); o.data.materials.append(iron_m)
    y = d.normalized(); x = y.cross(Vector((0, -1, 0)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    bpy.context.view_layer.update()
    o.parent = tgt; o.parent_type = 'BONE'; o.parent_bone = "mixamorig:Spine2"
    bpy.context.view_layer.update()
    o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * d.length, z * w)).transposed().to_4x4()


back_y = TR.tpos["mixamorig:Spine2"].y + 0.17
top = Vector((-0.13, back_y, TR.tpos["mixamorig:Neck"].z + 0.16))
bot = Vector((0.11, back_y + 0.02, TR.tpos["mixamorig:Spine"].z - 0.05))
box("AnchorShank", bot, top, 0.07)
shank = (top - bot).normalized(); side = shank.cross(Vector((0, 1, 0))).normalized()
for sgn in (1, -1): box("AnchorArm", bot, bot + side * sgn * 0.24 + shank * 0.14, 0.075)
box("AnchorRing", top, top + shank * 0.11, 0.05)
bl = make_blade_object((0.80, 0.80, 0.84, 1))
steel = bpy.data.materials.new("steel"); steel.diffuse_color = (0.85, 0.85, 0.9, 1); bl.data.materials.append(steel)


def place_sabre():
    r, t = blade(tgt)
    d = (t - r) * BLADE_SHOW
    y = d.normalized(); x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    bl.matrix_world = Matrix.Translation(r) @ Matrix((x * 0.056, y * d.length, z * 0.0175)).transposed().to_4x4()


bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
gm = bpy.data.materials.new("ground"); gm.diffuse_color = (0.46, 0.34, 0.22, 1); bpy.context.active_object.data.materials.append(gm)
dots = bpy.data.materials.new("dots"); dots.diffuse_color = (0.36, 0.27, 0.17, 1)
vs, fs = [], []
for gx in range(-24, 25):          # точки сетки 0,5 м одной сеткой (по одному кубу операторами — минуты)
    for gy in range(-24, 25):
        i = len(vs); c = Vector((gx * 0.5, gy * 0.5, 0.003)); e = 0.02
        vs += [c + Vector((-e, -e, 0)), c + Vector((e, -e, 0)), c + Vector((e, e, 0)), c + Vector((-e, e, 0))]
        fs.append((i, i + 1, i + 2, i + 3))
dm = bpy.data.meshes.new("dots"); dm.from_pydata(vs, [], fs)
do = bpy.data.objects.new("dots", dm); sc.collection.objects.link(do); dm.materials.append(dots)
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.8
w = bpy.data.worlds.new("w"); w.color = (0.12, 0.15, 0.14); sc.world = w
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'


def look(view_dir, pitch_deg, target, scale, dist=30.0):
    p = math.radians(pitch_deg)
    v = Vector((view_dir[0], view_dir[1], 0)).normalized()
    fwd = Vector((v.x * math.cos(p), v.y * math.cos(p), -math.sin(p)))
    cam.location = target - fwd * dist
    cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale


a15 = math.radians(15)
VIEWS = {"game": ((math.cos(a15), -math.sin(a15)), 48), "side": ((1, 0), 0)}


def pose_at(clip, tick, loc=(0, 0, 0), yaw=0.0):
    tgt.location = (0, 0, 0); tgt.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    ratio = TR.apply(clip, tick)
    tgt.location = loc; tgt.rotation_euler = (0, 0, yaw)
    bpy.context.view_layer.update()
    place_sabre()
    return ratio


exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sq_render_modes.py"), encoding="utf-8").read())
