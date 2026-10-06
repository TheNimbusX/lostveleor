"""Абордаж v2: лист поз (игровая камера + сбоку + 3/4) и GIF трёх бросков в масштабе игры.

blender -b --factory-startup -P ab_render.py -- <anim_dir> <frames_dir> sheet
blender -b --factory-startup -P ab_render.py -- <anim_dir> <frames_dir> gif
Читает выгруженные FBX и Pelag_AN_Abordage2Bind.fbx, перенос на v6 — формула Unity (ab_view.py, как sq_view.py).
Сабля — за кушаком у левого бедра (как BeginAnchorUse), якорь — от правой кисти к цели, цепь — от левой кисти к якорю.
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_view import Transfer, TEX

argv = sys.argv[sys.argv.index("--") + 1:]
anim, out, mode = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
P = "Pelag_AN_Abordage2_"
CLIPS = [P + c for c in ("Throw", "Pull", "Punch", "Recover")]
TR = Transfer(anim, CLIPS, "Pelag_AN_Abordage2Bind")
sc, tgt = TR.sc, TR.tgt

mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEX)
nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
for m in TR.meshes:
    m.data.materials.clear(); m.data.materials.append(mat)


def solid(name, color):
    m = bpy.data.materials.new(name); m.diffuse_color = color
    return m


IRON = solid("iron", (0.16, 0.16, 0.18, 1)); STEEL = solid("steel", (0.85, 0.85, 0.9, 1))
FOE = solid("foe", (0.30, 0.42, 0.22, 1))


def unit_box(name, m):
    me = bpy.data.meshes.new(name)
    v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
    me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(m)
    return o


def place(o, a, b, w, d=None):
    """Брусок от a до b толщиной w (d — вторая толщина)."""
    v = b - a
    y = v.normalized() if v.length > 1e-6 else Vector((0, 0, 1))
    x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * max(v.length, 1e-4), z * (d or w))).transposed().to_4x4()


sabre = unit_box("sabre", STEEL)
chain = unit_box("chain", IRON)
anchor = unit_box("anchor", IRON)
anchor_arm = unit_box("anchor_arm", IRON)


def bone(n):
    return tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].head


def place_sabre():
    """Сабля за кушаком у левого бедра: от левого бока таза назад-вниз (как BeginAnchorUse)."""
    hip = bone("LeftUpLeg"); hips = bone("Hips"); sp = bone("Spine")
    up = (sp - hips).normalized()
    side = (bone("LeftUpLeg") - bone("RightUpLeg")).normalized()
    fwd = up.cross(side).normalized() * -1
    root = hip + side * 0.06 + up * 0.10 + fwd * 0.08
    tip = root - fwd * 0.62 - up * 0.30
    place(sabre, root, tip, 0.045, 0.012)


def place_anchor(at, d):
    """Якорь: веретено вдоль d, рога поперёк."""
    shank_a = at - d * 0.05; shank_b = at - d * 0.42
    place(anchor, shank_a, shank_b, 0.06)
    side = d.cross(Vector((0, 0, 1)))
    if side.length < 1e-6: side = Vector((1, 0, 0))
    side.normalize()
    place(anchor_arm, at - d * 0.02 - side * 0.17, at - d * 0.02 + side * 0.17, 0.06)


def hide(objs, flag):
    for o in objs:
        o.hide_render = flag


bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(solid("ground", (0.46, 0.34, 0.22, 1)))
dots = solid("dots", (0.36, 0.27, 0.17, 1))
vs, fs = [], []
for gx in range(-30, 31):
    for gy in range(-30, 31):
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
VIEWS = {"game": ((math.cos(a15), -math.sin(a15)), 48), "side": ((1, 0), 0), "q34": ((0.5, 0.866), 8)}


def pose_at(clip, frame, loc=(0, 0, 0), yaw=0.0):
    tgt.location = (0, 0, 0); tgt.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    ratio = TR.apply(clip, frame)
    tgt.location = loc; tgt.rotation_euler = (0, 0, yaw)
    bpy.context.view_layer.update()
    place_sabre()
    return ratio


exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ab_render_modes.py"), encoding="utf-8").read())
