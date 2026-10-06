"""Крушение v3: кадры видео для владельца по последовательности клипов (без стоп-кадров) — тело из ВЫГРУЖЕННЫХ FBX
переносом как в Unity (wk_view.Transfer), голова якоря — меш Tripo в позе запечки / живого маятника, цепь — звенья
0,135 м от гнезда хвата до кольца (натянута — прямая, провисла — дуга длиной L). Как v3_render.py (Swing1), но
последовательность задаётся файлом: [{"clip", "frame", "head": [p], "q": [x,y,z,w]}, ...] — по шагу вывода.

blender -b --factory-startup -P v3s2_render.py -- <anim_dir> <seq.json> <out_dir> <view> <tag>   (V3_RES=WxH, V3_WIDTH=м)
  view: game | fr | fl | rside | ... ; кадры пишутся out_dir/<tag>_<view>_NNN.png
  V3_STILL=1 — без цепочки: seq.json — список отдельных поз (лист ключевых поз).
Проверка стыка: «SEAMCHK a@fa b@fb» в seq.json → наибольший поворот кости между позами двух клипов (после переноса).
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from wk_view import Transfer, TEX

argv = sys.argv[sys.argv.index("--") + 1:]
ANIM, SEQ, OUT, VIEW, TAG = argv[:5]
os.makedirs(OUT, exist_ok=True)
seq = json.load(open(SEQ, encoding="utf-8"))
K = 0.98881
MU = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
SOCK = Vector((0.0, 0.0819, 0.02184)) / K
EYE = Vector((0.0, 0.405 * 0.94, 0.01 * 0.94))
L, LINK = 1.6, 0.135


def B(u): return Vector((-u[0], -u[2], u[1])) / K


def BR(q): return MU.transposed() @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix() @ MU


clips = sorted({s["clip"] for s in seq["frames"]})
TR = Transfer(ANIM, clips, "Pelag_AN_Wreck2Bind")
sc, tgt = TR.sc, TR.tgt
mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEX)
nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
for m in TR.meshes: m.data.materials.clear(); m.data.materials.append(mat)


def solid(name, color):
    m = bpy.data.materials.new(name); m.diffuse_color = color
    return m


IRON, STEEL, LEATHER = solid("iron", (0.13, 0.13, 0.15, 1)), solid("steel", (0.85, 0.85, 0.9, 1)), solid("leather", (0.36, 0.2, 0.1, 1))
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
hi_ = hm.node_tree.nodes.new("ShaderNodeTexImage"); hi_.image = bpy.data.images.load(AD + r"\Anchor_BaseColor.png")
hm.node_tree.links.new(hi_.outputs["Color"], hm.node_tree.nodes.get("Principled BSDF").inputs["Base Color"]); hm.node_tree.nodes.active = hi_
head.data.materials.clear(); head.data.materials.append(hm)
bpy.ops.mesh.primitive_torus_add(major_radius=0.042, minor_radius=0.012, major_segments=16, minor_segments=6)
link0 = bpy.context.active_object; link0.scale = (1.0, 1.75, 1.0); bpy.ops.object.transform_apply(scale=True)
link0.data.materials.append(IRON); link0.hide_render = True
links = []
for i in range(14):
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


sabre, grip = box("sabre", STEEL), box("grip", LEATHER)


def bone(n): return tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].head


def bone_rot(n): return (tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].matrix).to_3x3().normalized()


def curve(a, b, n=40):
    span = (b - a).length * K
    if span >= L - 0.02: return [a.lerp(b, i / n) for i in range(n + 1)]
    lo_, hi_ = 0.0, L
    for _ in range(40):
        sag = (lo_ + hi_) / 2
        pts = [a.lerp(b, i / n) - Vector((0, 0, 4 * sag / K * (i / n) * (1 - i / n))) for i in range(n + 1)]
        ln = sum((pts[i + 1] - pts[i]).length for i in range(n)) * K
        if ln < L: lo_ = sag
        else: hi_ = sag
    lift = min(p.z for p in pts)                     # провисшая цепь лежит на земле, а не уходит под неё
    return [Vector((p.x, p.y, max(p.z, 0.012))) for p in pts] if lift < 0.012 else pts


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


def pose_at(s):
    tgt.location = (0, 0, 0); tgt.rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
    TR.apply(s["clip"], s["frame"])
    hipL, hips, sp = bone("LeftUpLeg"), bone("Hips"), bone("Spine")
    up = (sp - hips).normalized(); side = (bone("LeftUpLeg") - bone("RightUpLeg")).normalized()
    fwd = -up.cross(side).normalized()
    root = hipL + side * 0.06 + up * 0.10 + fwd * 0.08
    place(sabre, root, root - fwd * 0.62 - up * 0.30, 0.045, 0.012)
    g = bone("LeftHand") + bone_rot("LeftHand") @ SOCK
    p, q = s["p"], s["q"]
    head.matrix_world = Matrix.Translation(B(p)) @ BR(q).to_4x4() @ Matrix.Scale(0.94 / K, 4)
    ring = B([p[i] + c for i, c in enumerate(Quaternion((q[3], q[0], q[1], q[2])) @ EYE)])
    pts = curve(g, ring)
    place_chain(pts)
    d = (pts[1] - pts[0]).normalized()
    place(grip, g - d * 0.19, g + d * 0.02, 0.05)


def seam_check(a, fa, b, fb):
    """Наибольший поворот кости (без пальцев) между позой a@fa и b@fb после переноса как в Unity."""
    def rots(c, f):
        TR.apply(c, f)
        return {pb.name: (tgt.matrix_world @ pb.matrix).to_quaternion() for pb in tgt.pose.bones}
    ra, rb = rots(a, fa), rots(b, fb)
    worst = (0.0, "")
    for n, q in ra.items():
        if any(x in n for x in ("Thumb", "Index", "Middle", "Ring", "Pinky", "_End")): continue
        ang = math.degrees(q.rotation_difference(rb[n]).angle); ang = min(ang, 360 - ang)
        if ang > worst[0]: worst = (ang, n.replace("mixamorig:", ""))
    return worst


for chk in seq.get("seamchecks", []):
    w = seam_check(*chk)
    print("SEAMCHK %s@%s -> %s@%s: %.3f deg %s" % (chk[0], chk[1], chk[2], chk[3], w[0], w[1]))

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(solid("ground", (0.42, 0.36, 0.26, 1)))
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.8
w = bpy.data.worlds.new("w"); w.color = (0.12, 0.15, 0.14); sc.world = w
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
a15 = math.radians(15)
CAMS = {"game": ((math.cos(a15), -math.sin(a15)), 48, Vector((0.0, -0.9, 0.5)), "87px"),
        "fl": ((-0.70, 0.71), 22, Vector((0.0, -0.6, 0.7)), 5.6), "fr": ((0.70, 0.71), 22, Vector((0.0, -0.6, 0.7)), 5.6),
        "bl": ((-0.70, -0.71), 22, Vector((0.0, -0.6, 0.7)), 5.6), "rside": ((1, 0), 12, Vector((0.0, -0.8, 0.8)), 5.4)}
vd, pitch, target, width = CAMS[VIEW]
res = os.environ.get("V3_RES", "720x480").split("x")
sc.render.resolution_x, sc.render.resolution_y = int(res[0]), int(res[1])
if os.environ.get("V3_WIDTH"): width = float(os.environ["V3_WIDTH"])
pr = math.radians(pitch); v = Vector((vd[0], vd[1], 0)).normalized()
fw = Vector((v.x * math.cos(pr), v.y * math.cos(pr), -math.sin(pr)))
cam.location = target - fw * 30
cam.rotation_euler = fw.to_track_quat('-Z', 'Y').to_euler()
cam.data.ortho_scale = (sc.render.resolution_x / 87.0 / K) if width == "87px" else width / K
for k, s in enumerate(seq["frames"]):
    pose_at(s)
    sc.render.filepath = os.path.join(OUT, "%s_%s_%03d.png" % (TAG, VIEW, k))
    bpy.ops.render.render(write_still=True)
print("render done", VIEW, len(seq["frames"]))
