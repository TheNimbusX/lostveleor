"""Крушение v4: кадры для владельца — тело из Pelag_Wreck4_Work.blend (действия клипов) или сабля (перенос
Pelag_AN_Sabre1–3 на тот же риг), голова якоря (меш Tripo) и цепь по физике wreck4sim, без стоп-кадров.

blender -b --factory-startup -P v4_render.py -- <author_dir> <seq.json> <out_dir> <view> <tag>
  seq.json: {"frames": [{"mode": "wreck"|"sabre", "clip", "frame", "root": [x,y,z] (Блендер), "yaw" (рад, Блендер),
             "p", "q" (голова, Unity), "grip", "ring", "lfist"?, "cable", "span"}]}
  view: game | side | front | top ; V4_RES=WxH, V4_WIDTH=м, V4_TGT=x,y,z (Блендер)
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import v4_lib as L
from b_common import blade

argv = sys.argv[sys.argv.index("--") + 1:]
AUTH, SEQ, OUT, VIEW, TAG = argv[:5]
os.makedirs(OUT, exist_ok=True)
seq = json.load(open(SEQ, encoding="utf-8"))["frames"]
K = 0.98881
MU = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
EYE = Vector((0.0, 0.405 * 0.94, 0.01 * 0.94))
LINK = 0.135
DZ = float(os.environ.get("V4_DZ", "-0.095"))
B = lambda u: Vector((-u[0], -u[2], u[1])) / K
BR = lambda q: MU.transposed() @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix() @ MU

bpy.ops.wm.open_mainfile(filepath=os.path.join(AUTH, "Pelag_Wreck4_Work.blend"))
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH' and o.find_armature() == rig][0]
for o in list(bpy.data.objects):
    if o not in (rig, mesh): bpy.data.objects.remove(o, do_unlink=True)
root = bpy.data.objects.new("Root", None); sc.collection.objects.link(root)
mw = rig.matrix_world.copy(); rig.parent = root; rig.matrix_world = mw
for pb in rig.pose.bones: pb.rotation_mode = 'QUATERNION'
CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(CH + r"\Pelag_v6\Pelag_v6_BaseColor.jpg")
nt.links.new(img.outputs["Color"], nt.nodes.get("Principled BSDF").inputs["Base Color"]); nt.nodes.active = img
mesh.data.materials.clear(); mesh.data.materials.append(mat)


def solid(name, color):
    m = bpy.data.materials.new(name); m.diffuse_color = color; return m


IRON, STEEL, LEATHER, RED = (solid("iron", (0.13, 0.13, 0.15, 1)), solid("steel", (0.85, 0.85, 0.9, 1)),
                             solid("leather", (0.36, 0.2, 0.1, 1)), solid("red", (0.75, 0.12, 0.08, 1)))
AD = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Weapons\Pelag\AnchorDemo"
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=AD + r"\Pelag_AnchorHead_Tripo.fbx")
new = [o for o in bpy.data.objects if o not in before]
head = [o for o in new if o.type == 'MESH'][0]
for o in new:
    if o is not head: bpy.data.objects.remove(o, do_unlink=True)
head.data.transform(head.matrix_world); head.matrix_world = Matrix.Identity(4)
vs = [v.co for v in head.data.vertices]
lo = Vector((min(v[i] for v in vs) for i in range(3))); hi = Vector((max(v[i] for v in vs) for i in range(3)))
head.data.transform(Matrix.Scale(1 / max(hi - lo), 4) @ Matrix.Translation(-(lo + hi) / 2))
hm = bpy.data.materials.new("anchor"); hm.use_nodes = True
ht = hm.node_tree.nodes.new("ShaderNodeTexImage"); ht.image = bpy.data.images.load(AD + r"\Anchor_BaseColor.png")
hm.node_tree.links.new(ht.outputs["Color"], hm.node_tree.nodes.get("Principled BSDF").inputs["Base Color"]); hm.node_tree.nodes.active = ht
head.data.materials.clear(); head.data.materials.append(hm)
bpy.ops.mesh.primitive_torus_add(major_radius=0.042, minor_radius=0.012, major_segments=16, minor_segments=6)
link0 = bpy.context.active_object; link0.scale = (1.0, 1.75, 1.0); bpy.ops.object.transform_apply(scale=True)
link0.data.materials.append(IRON); link0.hide_render = True
links = []
for i in range(40):
    o = bpy.data.objects.new("link%d" % i, link0.data); sc.collection.objects.link(o); links.append(o)


def box(name, m):
    me = bpy.data.meshes.new(name)
    v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
    me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(m)
    return o


def place(o, a, b, w, d=None):
    v = b - a
    y = v.normalized() if v.length > 1e-6 else Vector((0, 0, 1))
    x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * max(v.length, 1e-4), z * (d or w))).transposed().to_4x4()


grip_o, tassel, sabre = box("grip", LEATHER), box("tassel", RED), box("sabre", STEEL)


def curve(a, b, length, n=40):
    span = (b - a).length * K
    if span >= length - 0.02: return [a.lerp(b, i / n) for i in range(n + 1)]
    lo_, hi_ = 0.0, length
    for _ in range(40):
        sag = (lo_ + hi_) / 2
        pts = [a.lerp(b, i / n) - Vector((0, 0, 4 * sag / K * (i / n) * (1 - i / n))) for i in range(n + 1)]
        ln = sum((pts[i + 1] - pts[i]).length for i in range(n)) * K
        if ln < length: lo_ = sag
        else: hi_ = sag
    return [Vector((p.x, p.y, max(p.z, 0.012))) for p in pts]


def place_chain(pts):
    acc = [0.0]
    for i in range(len(pts) - 1): acc.append(acc[-1] + (pts[i + 1] - pts[i]).length)
    step = LINK / K
    for j, o in enumerate(links):
        s = (j + 0.5) * step
        if s > acc[-1]: o.hide_render = True; continue
        o.hide_render = False
        i = max(k for k in range(len(acc) - 1) if acc[k] <= s) if s > 0 else 0
        u = (s - acc[i]) / max(1e-6, acc[i + 1] - acc[i])
        c = pts[i].lerp(pts[i + 1], u); y = (pts[i + 1] - pts[i]).normalized()
        x = y.cross(Vector((0, 0, 1)))
        if x.length < 1e-6: x = Vector((1, 0, 0))
        x.normalize(); z = x.cross(y)
        R = Matrix((x, y, z)).transposed() @ Matrix.Rotation(math.radians(90 * (j % 2)), 3, 'Y')
        o.matrix_world = Matrix.Translation(c) @ R.to_4x4()


def P(n): return rig.matrix_world @ rig.pose.bones["mixamorig:" + n].head


def fist(s):
    h, m = P(s + "Hand"), P(s + "HandMiddle1")
    return h.lerp(m, 0.75)


src = None
for a in bpy.data.actions: a.use_fake_user = True


def pose_at(s):
    global src
    root.location = Vector(s["root"]); root.rotation_euler = (0, 0, s["yaw"])
    bpy.context.view_layer.update()
    if s["mode"] == "sabre":
        keep = root.matrix_world.copy(); root.matrix_world = Matrix.Identity(4); bpy.context.view_layer.update()
        if src is None:
            rig.animation_data.action = None
            src = L.SabreSource(rig)
        for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        src.apply(s["clip"], s["frame"])
        hips = rig.pose.bones["mixamorig:Hips"]
        hips.matrix = rig.matrix_world.inverted() @ (Matrix.Translation((0, 0, DZ)) @ (rig.matrix_world @ hips.matrix))
        root.matrix_world = keep; bpy.context.view_layer.update()
        r0, t0 = blade(rig); sabre.hide_render = False; place(sabre, r0, t0, 0.035, 0.008)
        head.hide_render = grip_o.hide_render = tassel.hide_render = True
        for o in links: o.hide_render = True
        return
    sabre.hide_render = True; head.hide_render = grip_o.hide_render = tassel.hide_render = False
    rig.animation_data.action = bpy.data.actions[s["clip"]]
    f = s["frame"]; sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
    bpy.context.view_layer.update()
    p, q = s["p"], s["q"]
    head.matrix_world = Matrix.Translation(B(p)) @ BR(q).to_4x4() @ Matrix.Scale(0.94 / K, 4)
    ring = B([p[i] + c for i, c in enumerate(Quaternion((q[3], q[0], q[1], q[2])) @ EYE)])
    g = B(s["grip"]); fl, fr = fist("Left"), fist("Right")
    ax = (fr - fl).normalized() if (fr - fl).length > 1e-4 else (g - fl).normalized()
    if "h0" in s:                                              # рукоять из слоя якоря (спина / одна рука / две руки)
        h0, h1 = B(s["h0"]), B(s["h1"]); hx = (h1 - h0).normalized() if (h1 - h0).length > 1e-4 else ax
        place(grip_o, h0, h1, 0.045); place(tassel, h0 - hx * 0.08, h0, 0.03)
    else:
        place(grip_o, fl - ax * 0.09, g, 0.045)                # рукоять: от кисточки за левым кулаком до выхода цепи
        place(tassel, fl - ax * 0.17, fl - ax * 0.09, 0.03)
    if "chain" in s: place_whip([B(c) for c in s["chain"]], BR(q) @ Vector((1, 0, 0)))
    else: place_chain(curve(g, ring, s["cable"]))


def place_whip(pts, ring_x):
    """Цепь-хлыст (06.10): звенья по решённой кривой с постоянным шагом ОТ КОЛЬЦА (новые выходят из кулака),
    каждое — вдоль цепи, соседние повёрнуты на 90°; нормаль переносится вдоль кривой без закрутки."""
    pts = pts[::-1]
    acc = [0.0]
    for i in range(len(pts) - 1): acc.append(acc[-1] + (pts[i + 1] - pts[i]).length)
    step = LINK / K; xp = None; i = 0
    for j, o in enumerate(links):
        s_ = (j + 0.5) * step
        if s_ > acc[-1] + 0.25 * step: o.hide_render = True; continue
        o.hide_render = False; s_ = min(s_, acc[-1])
        while i < len(acc) - 2 and acc[i + 1] < s_: i += 1
        u = (s_ - acc[i]) / max(1e-6, acc[i + 1] - acc[i])
        c = pts[i].lerp(pts[i + 1], u)
        a_, b_ = max(0.0, s_ - step / 2), min(acc[-1], s_ + step / 2)
        y = (at(pts, acc, b_) - at(pts, acc, a_))
        y = y.normalized() if y.length > 1e-6 else (pts[i + 1] - pts[i]).normalized()
        x = (xp if xp is not None else ring_x).copy()
        x = x - y * x.dot(y)
        if x.length < 1e-5: x = y.orthogonal()
        x.normalize(); xp = x; z = x.cross(y)
        R = Matrix((x, y, z)).transposed() @ Matrix.Rotation(math.radians(90 * (j % 2)), 3, 'Y')
        o.matrix_world = Matrix.Translation(c) @ R.to_4x4()


def at(pts, acc, s_):
    k = 0
    while k < len(acc) - 2 and acc[k + 1] < s_: k += 1
    u = (s_ - acc[k]) / max(1e-6, acc[k + 1] - acc[k])
    return pts[k].lerp(pts[k + 1], min(1.0, max(0.0, u)))


bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(solid("ground", (0.42, 0.36, 0.26, 1)))
for i in range(-12, 13):                                       # сетка 0,5 м — видно, едут ли стопы
    for a, b_ in (((i * .5, -6, 0.002), (i * .5, 6, 0.002)), ((-6, i * .5, 0.002), (6, i * .5, 0.002))):
        place(box("grid", solid("gridm", (0.33, 0.28, 0.2, 1))), Vector(a), Vector(b_), 0.012, 0.001)
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.8
w = bpy.data.worlds.new("w"); w.color = (0.12, 0.15, 0.14); sc.world = w
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
a15 = math.radians(15)
CAMS = {"game": ((math.cos(a15), -math.sin(a15)), 48, Vector((0.0, -1.0, 0.5)), 7.6),
        "side": ((1, 0), 10, Vector((0.0, -1.0, 0.85)), 6.4), "front": ((0, 1), 12, Vector((0.0, -1.0, 0.85)), 6.4),
        "top": ((0.0001, 1), 89.9, Vector((0.0, -1.0, 0.0)), 6.4), "back": ((0.6, -0.8), 12, Vector((0.0, -1.0, 0.9)), 3.0)}
vd, pitch, target, width = CAMS[VIEW]
if os.environ.get("V4_TGT"): target = Vector(tuple(float(x) for x in os.environ["V4_TGT"].split(",")))
res = os.environ.get("V4_RES", "960x540").split("x")
sc.render.resolution_x, sc.render.resolution_y = int(res[0]), int(res[1])
if os.environ.get("V4_WIDTH"): width = float(os.environ["V4_WIDTH"])
pr = math.radians(pitch); v = Vector((vd[0], vd[1], 0)).normalized()
fw = Vector((v.x * math.cos(pr), v.y * math.cos(pr), -math.sin(pr)))
cam.location = target - fw * 30
cam.rotation_euler = fw.to_track_quat('-Z', 'Y').to_euler()
cam.data.ortho_scale = width / K
ONLY = set(int(x) for x in os.environ.get("V4_ONLY", "").split(",") if x)
for k, s in enumerate(seq):
    if ONLY and k not in ONLY: continue
    pose_at(s)
    sc.render.filepath = os.path.join(OUT, "%s_%s_%03d.png" % (TAG, VIEW, k))
    bpy.ops.render.render(write_still=True)
print("render done", VIEW, len(seq))
