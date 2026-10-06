"""Крушение v3, Swing1: кадры для владельца — клип из ВЫГРУЖЕННОГО FBX переносом как в Unity (wk_view.Transfer),
голова якоря — меш Tripo (Pelag_AnchorHead_Tripo, 0,94 м) в позе запечки, цепь — звенья 0,135 м от гнезда хвата кисти
до кольца головы (натянута — прямая, провисла — дуга длиной L).

blender -b --factory-startup -P v3_render.py -- <anim_dir> <bake.anchorbake.json> <shown.json> <out_dir> <mode> [views] [frames]
  mode: still (кадры frames × views), seq (полкадра 0..12, один вид). head: bake — путь запечки; game — показ рига при
  первом нажатии (голова со спины, сшивка AnchorBlend), берётся из shown.json, после сшивки = запечка.
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from wk_view import Transfer, TEX

argv = sys.argv[sys.argv.index("--") + 1:]
ANIM, BAKE, SHOWN, OUT, MODE = argv[:5]
VIEWS = argv[5].split(",") if len(argv) > 5 else ["q34", "side", "game"]
FRAMES = [float(x) for x in argv[6].split(",")] if len(argv) > 6 else [0, 2, 4, 7, 10, 12]
HEADSRC = os.environ.get("V3_HEAD", "bake")
os.makedirs(OUT, exist_ok=True)
CLIP = "Pelag_AN_Wreck2_Swing1"
K = 0.98881
MU = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))          # Blender → Unity (без масштаба)
SOCK = Vector((0.0, 0.0819, 0.02184)) / K
EYE = Vector((0.0, 0.405 * 0.94, 0.01 * 0.94))             # кольцо над центром, оси головы Unity
L, LINK = 1.6, 0.135


def B(u): return Vector((-u[0], -u[2], u[1])) / K


def BR(q): return MU.transposed() @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix() @ MU


bake = json.load(open(BAKE))["samples"]
shown = json.load(open(SHOWN))["samples"] if os.path.exists(SHOWN) else []
TR = Transfer(ANIM, [CLIP], "Pelag_AN_Wreck2Bind")
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

# --- голова якоря: меш Tripo, нормирован как в игре (центр габарита, большая ось = 0,94 м); оси меша Blender → Unity = MU
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
ext = max(hi - lo)
head.data.transform(Matrix.Scale(1 / ext, 4) @ Matrix.Translation(-(lo + hi) / 2))
hm = bpy.data.materials.new("anchor"); hm.use_nodes = True
hi_ = hm.node_tree.nodes.new("ShaderNodeTexImage"); hi_.image = bpy.data.images.load(AD + r"\Anchor_BaseColor.png")
hm.node_tree.links.new(hi_.outputs["Color"], hm.node_tree.nodes.get("Principled BSDF").inputs["Base Color"]); hm.node_tree.nodes.active = hi_
head.data.materials.clear(); head.data.materials.append(hm)

# --- звено цепи: овальный тор вдоль Y
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
trail = []


def bone(n): return tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].head


def bone_rot(n): return (tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].matrix).to_3x3().normalized()


def sample(src, f):
    """Поза головы в кадре f (Unity): запечка — 4 сэмпла на кадр; показ рига — 2 на кадр."""
    if src == "game" and shown and f <= shown[-1]["t"] + 1e-6:
        i = min(range(len(shown)), key=lambda k: abs(shown[k]["t"] - f)); s = shown[i]
        return s["p"], s["q"]
    i = min(range(len(bake)), key=lambda k: abs(bake[k]["t"] - f)); s = bake[i]
    return s["p"], s["q"]


def curve(a, b, n=40):
    """Цепь от хвата a к кольцу b (Blender): прямая при натяге, иначе провисшая дуга длиной L (парабола)."""
    span = (b - a).length * K
    if span >= L - 0.02: return [a.lerp(b, i / n) for i in range(n + 1)]
    lo_, hi_ = 0.0, L
    for _ in range(40):
        sag = (lo_ + hi_) / 2
        pts = [a.lerp(b, i / n) - Vector((0, 0, 4 * sag / K * (i / n) * (1 - i / n))) for i in range(n + 1)]
        ln = sum((pts[i + 1] - pts[i]).length for i in range(n)) * K
        if ln < L: lo_ = sag
        else: hi_ = sag
    return pts


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
        x = y.cross(Vector((0, 0, 1)));
        if x.length < 1e-6: x = Vector((1, 0, 0))
        x.normalize(); z = x.cross(y)
        R = Matrix((x, y, z)).transposed() @ Matrix.Rotation(math.radians(90 * (j % 2)), 3, 'Y')
        o.matrix_world = Matrix.Translation(c) @ R.to_4x4()


def pose_at(f, src):
    tgt.location = (0, 0, 0); tgt.rotation_euler = (0, 0, 0); bpy.context.view_layer.update()
    TR.apply(CLIP, f)
    hipL, hips, sp = bone("LeftUpLeg"), bone("Hips"), bone("Spine")
    up = (sp - hips).normalized(); side = (bone("LeftUpLeg") - bone("RightUpLeg")).normalized()
    fwd = -up.cross(side).normalized()
    root = hipL + side * 0.06 + up * 0.10 + fwd * 0.08
    place(sabre, root, root - fwd * 0.62 - up * 0.30, 0.045, 0.012)            # сабля за кушаком у левого бедра
    g = bone("LeftHand") + bone_rot("LeftHand") @ SOCK                          # гнездо хвата = начало цепи
    p, q = sample(src, f)
    R = BR(q)
    head.matrix_world = Matrix.Translation(B(p)) @ R.to_4x4() @ Matrix.Scale(0.94 / K, 4)
    ring = B([p[i] + c for i, c in enumerate(Quaternion((q[3], q[0], q[1], q[2])) @ EYE)])
    pts = curve(g, ring)
    place_chain(pts)
    d = (pts[1] - pts[0]).normalized()
    place(grip, g - d * 0.19, g + d * 0.02, 0.05)
    return B(p)


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
# вид: (направление взгляда по земле, наклон вниз °, центр кадра, ширина кадра м или px/м)
CAMS = {"game": ((math.cos(a15), -math.sin(a15)), 48, Vector((0.0, -0.9, 0.5)), "87px"),
        "q34": ((-0.62, 0.78), 20, Vector((0.0, -0.9, 0.75)), 5.6),
        "side": ((1, 0), 4, Vector((0.0, -0.9, 0.85)), 5.4),
        "front": ((0, 1), 6, Vector((0.0, -0.9, 0.85)), 5.4),
        "top": ((0, 1), 89, Vector((0.0, -0.9, 0.0)), 6.0),
        "fl": ((-0.70, 0.71), 22, Vector((0.0, -0.8, 0.7)), 5.0),     # спереди-слева, сверху 22°
        "fr": ((0.70, 0.71), 22, Vector((0.0, -0.8, 0.7)), 5.0),      # спереди-справа
        "bl": ((-0.70, -0.71), 22, Vector((0.0, -0.8, 0.7)), 5.0),    # сзади-слева
        "br": ((0.70, -0.71), 22, Vector((0.0, -0.8, 0.7)), 5.0),     # сзади-справа
        "rside": ((1, 0), 12, Vector((0.0, -0.8, 0.8)), 5.0),
        "lside": ((-1, 0), 12, Vector((0.0, -0.8, 0.8)), 5.0)}


def look(vn, resx):
    vd, pitch, target, width = CAMS[vn]
    p = math.radians(pitch)
    v = Vector((vd[0], vd[1], 0)).normalized()
    fw = Vector((v.x * math.cos(p), v.y * math.cos(p), -math.sin(p)))
    cam.location = target - fw * 30
    cam.rotation_euler = fw.to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = (resx / 87.0 / K) if width == "87px" else width / K


if MODE == "still":
    sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("V3_STILL", "480"))
    for f in FRAMES:
        pose_at(f, HEADSRC)
        for vn in VIEWS:
            look(vn, sc.render.resolution_x)
            sc.render.filepath = os.path.join(OUT, "f%04.1f_%s.png" % (f, vn))
            bpy.ops.render.render(write_still=True)
elif MODE == "seq":
    vn = VIEWS[0]
    res = os.environ.get("V3_RES", "720x480" if vn == "game" else "640x520").split("x")
    sc.render.resolution_x, sc.render.resolution_y = int(res[0]), int(res[1])
    if os.environ.get("V3_WIDTH"): CAMS[vn] = CAMS[vn][:3] + (float(os.environ["V3_WIDTH"]),)
    look(vn, sc.render.resolution_x)
    tag = os.environ.get("V3_TAG", HEADSRC)
    for k in range(25):
        f = k / 2
        pose_at(f, HEADSRC)
        sc.render.filepath = os.path.join(OUT, "seq_%s_%s_%02d.png" % (tag, vn, k))
        bpy.ops.render.render(write_still=True)
print("render done", MODE, VIEWS)
