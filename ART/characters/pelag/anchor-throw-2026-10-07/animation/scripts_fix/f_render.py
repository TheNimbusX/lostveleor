"""Кадры клипа броска из Pelag_AnchorThrow2_Work.blend (действие AnchorThrow2_Master, кадр = тик):
blender -b --factory-startup -P t_render.py -- <work_dir> <out_dir> <view> <ticks: 1,2,5 | all | a:b:step>
view: side (правый бок, герой смотрит вправо экрана — как лист поз) | game (камера игры: орто, 48°, бросок вправо-вверх)
      | front | back.  Окружение: T_RES=WxH, T_WIDTH=м (ширина кадра), T_ZC=высота центра.
Якорь и цепь: голова якоря (Tripo) в правом кулаке на замахе, дальше по линии броска — прямая цепь от левого кулака
(рукоять) через правый к голове (схема для сверки позы с листом, не игровой риг).
"""
import bpy, sys, os, math, json
HKR = float(os.environ.get("T_HK", "4"))
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
WORK, OUT, VIEW, TK = argv[:4]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(WORK, "Pelag_AnchorThrow2_Work.blend"))
bpy.context.scene.render.fps = int(30 * HKR)
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH' and o.find_armature() == rig][0]
for o in list(bpy.data.objects):
    if o not in (rig, mesh): bpy.data.objects.remove(o, do_unlink=True)
rig.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(CH + r"\Pelag_v6\Pelag_v6_BaseColor.jpg")
nt.links.new(img.outputs["Color"], nt.nodes.get("Principled BSDF").inputs["Base Color"]); nt.nodes.active = img
mesh.data.materials.clear(); mesh.data.materials.append(mat)


def solid(name, color):
    m = bpy.data.materials.new(name); m.diffuse_color = color; return m


IRON, LEATHER, RED = solid("iron", (0.13, 0.13, 0.15, 1)), solid("leather", (0.36, 0.2, 0.1, 1)), solid("red", (0.75, 0.12, 0.08, 1))
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
head.data.transform(Matrix.Scale(0.95 / max(hi - lo), 4) @ Matrix.Translation(-(lo + hi) / 2))
head.data.materials.clear(); head.data.materials.append(IRON)
# Ось веретена Tripo — самая длинная ось габарита; кольцо — на её «верхнем» конце.
ext = hi - lo; AX = max(range(3), key=lambda i: ext[i])
bpy.ops.mesh.primitive_torus_add(major_radius=0.042, minor_radius=0.012, major_segments=12, minor_segments=6)
link0 = bpy.context.active_object; link0.scale = (1.0, 1.75, 1.0); bpy.ops.object.transform_apply(scale=True)
link0.data.materials.append(IRON); link0.hide_render = True
links = []
for i in range(120):
    o = bpy.data.objects.new("link%d" % i, link0.data); sc.collection.objects.link(o); links.append(o)


def P(n): return rig.matrix_world @ rig.pose.bones["mixamorig:" + n].head


def fist(s): return P(s + "Hand").lerp(P(s + "HandMiddle1"), 0.75)


def chain(pts):
    acc = [0.0]
    for i in range(len(pts) - 1): acc.append(acc[-1] + (pts[i + 1] - pts[i]).length)
    step = 0.135
    for j, o in enumerate(links):
        s_ = (j + 0.5) * step
        if s_ > acc[-1]: o.hide_render = True; continue
        o.hide_render = False
        i = 0
        while i < len(acc) - 2 and acc[i + 1] < s_: i += 1
        u = (s_ - acc[i]) / max(1e-6, acc[i + 1] - acc[i])
        c = pts[i].lerp(pts[i + 1], u); y = (pts[i + 1] - pts[i]).normalized()
        x = y.cross(Vector((0, 0, 1)))
        if x.length < 1e-6: x = Vector((1, 0, 0))
        x.normalize(); z = x.cross(y)
        R = Matrix((x, y, z)).transposed() @ Matrix.Rotation(math.radians(90 * (j % 2)), 3, 'Y')
        o.matrix_world = Matrix.Translation(c) @ R.to_4x4()


def place_head(center, up_dir):
    """Голова: веретено (ось AX меша) — вдоль up_dir (кольцо к цепи), центр в center."""
    a = Vector((0, 0, 0)); a[AX] = 1.0
    q = a.rotation_difference(up_dir.normalized())
    head.matrix_world = Matrix.Translation(center) @ q.to_matrix().to_4x4()


FWD = Vector((0, -1, 0))
# Голова по линии броска: тик выпуска 2, полёт 1 м/тик от руки (0,5 м) до 7 м (тик 9), ждёт до рывка (11),
# возврат p(k) = k(T+2k)/(3T²), T = 6 — к руке в тик ловли 17 (контракт §0).


def head_along(t):
    if t <= 2: return None
    if t <= 9: return 0.5 + (t - 2) * 1.0 * 6.5 / 7.0
    if t <= 11: return 7.0
    if t < 17:
        k = t - 11; Tn = 6.0; p = k * (Tn + 2 * k) / (3 * Tn * Tn)
        return 7.0 + (0.5 - 7.0) * p
    return None


def fist_axis(s):
    return (P(s + "HandIndex1") - P(s + "HandPinky1")).normalized()


GRIP_CENTER = float(os.environ.get("T_GRIP", "0.24"))     # центр головы от центра кулака по оси кулака (якорь венцом по оси)


def scene_at(t):
    f = t * HKR
    sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f)); bpy.context.view_layer.update()
    fl_, fr_ = fist("Left"), fist("Right")
    a = head_along(t)
    if a is None:
        if t < 2 or t < 20.4:
            ax = fist_axis("Right")
            place_head(fr_ + ax * GRIP_CENTER, -ax)          # в кулаке: веретено по оси кулака, кольцо к руке
            chain([fl_, fl_ + (fr_ - fl_) * 0.5 + Vector((0, 0, -0.30)), fr_])
        else:
            sp = P("Spine2"); nk = P("Neck")
            back = (rig.matrix_world @ rig.pose.bones["mixamorig:Spine2"].matrix).to_3x3() @ Vector((0, 0, -1))
            place_head(sp + back.normalized() * 0.16 + (nk - sp) * 0.2, Vector((0, 0, 1)))
            chain([fl_, sp + back.normalized() * 0.12])
        return
    h = Vector((0, 0, fl_.z)) + FWD * a
    h.x = 0.0
    hc = h + FWD * 0.38
    place_head(hc, -FWD)
    chain([fl_, fr_, h] if (fr_ - fl_).dot(FWD) > 0 else [fl_, h])


bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(solid("ground", (0.80, 0.80, 0.78, 1)))
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.25
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.6
w = bpy.data.worlds.new("w"); w.color = (0.86, 0.86, 0.84); sc.world = w
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
res = os.environ.get("T_RES", "520x600").split("x")
sc.render.resolution_x, sc.render.resolution_y = int(res[0]), int(res[1])
width = float(os.environ.get("T_WIDTH", "2.3")); zc = float(os.environ.get("T_ZC", "0.78"))
fc = float(os.environ.get("T_FC", "0.25"))
if VIEW == "side":   # правый бок: камера в −X, смотрит в +X; вперёд героя (−Y) — вправо экрана
    fw = Vector((1, 0, -0.08)).normalized(); target = Vector((0, -fc, zc))
elif VIEW == "sheet":  # как лист поз: правый бок, камера довёрнута на 30° вперёд (видна грудь), чуть сверху
    a = math.radians(30); fw = Vector((math.cos(a), math.sin(a), -0.10)).normalized(); target = Vector((0, -fc, zc))
elif VIEW == "front":
    fw = Vector((0, 1, -0.1)).normalized(); target = Vector((0, 0, zc))
elif VIEW == "back":
    fw = Vector((0, -1, -0.1)).normalized(); target = Vector((0, 0, zc))
else:                # игра: орто под 48°, бросок вправо-вверх экрана (рысканье камеры 35° от броска)
    yaw = math.radians(float(os.environ.get("T_YAW", "-55"))); pr = math.radians(48)
    v = Vector((math.cos(yaw), math.sin(yaw), 0))
    fw = Vector((v.x * math.cos(pr), v.y * math.cos(pr), -math.sin(pr))); target = Vector((0, -fc, 0.5))
cam.location = target - fw * 30
cam.rotation_euler = fw.to_track_quat('-Z', 'Y').to_euler()
cam.data.ortho_scale = width
if TK == "all": ticks = [k / 2 for k in range(0, 51)]
elif ":" in TK:
    a_, b_, s_ = (float(x) for x in TK.split(":")); ticks = []; x = a_
    while x <= b_ + 1e-6: ticks.append(round(x, 4)); x += s_
else: ticks = [float(x) for x in TK.split(",")]
for i, t in enumerate(ticks):
    scene_at(t)
    sc.render.filepath = os.path.join(OUT, "f_%03d_t%05.2f.png" % (i, t))
    bpy.ops.render.render(write_still=True)
print("RENDERED", VIEW, len(ticks))
