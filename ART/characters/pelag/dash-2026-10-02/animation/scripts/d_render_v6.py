"""Лист поз дэша на нашей модели: Pelag_v6 в цвете, перенос как в Unity.

Запуск: blender -b --factory-startup -P d_render_v6.py -- <anim_dir> <frames_dir> [gif]

Читает ВЫГРУЖЕННЫЕ Pelag_AN_Dash.fbx и Pelag_AN_DashBind.fbx (проверка самого
экспорта) и переносит позы на Pelag_v6_MixamoRig тем же способом, что
RazlomPelagAuthoredClips.Build: дельта поворота каждой кости от привязки в
мировом пространстве, выравнивание по базису тела, поправка направления
сегмента к ребёнку, таз — смещение от привязки × отношение длин корпуса.
Печатает ту же проверку таза (доля от длины корпуса, порог .4) и худшую
ошибку сегмента — до Unity.

Якорь на спине — заглушка по рендеру after-back (кольцо над правым плечом,
лапы у левого бедра), сабля — та же заглушка-клинок, что в серии сабли.
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix, Quaternion
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import make_blade_object, place_blade, blade

argv = sys.argv[sys.argv.index("--") + 1:]
anim, out = argv[0], argv[1]
mode = argv[2] if len(argv) > 2 else "sheet"
os.makedirs(out, exist_ok=True)
CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"
TEX = CH + r"\Pelag_v6\Pelag_v6_BaseColor.jpg"


def load(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    arm.name = name
    return arm, [o for o in new if o.type == 'MESH']


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30
CLIP = os.environ.get("DASH_CLIP", "Pelag_AN_Dash")      # для сверки можно подать чужой клип/привязку
BIND = os.environ.get("DASH_BIND", "Pelag_AN_DashBind")
src, _ = load(os.path.join(anim, CLIP + ".fbx"), "Src")
bind, _ = load(os.path.join(anim, BIND + ".fbx"), "Bind")
tgt, meshes = load(V6, "Tgt")
tgt.scale = (1, 1, 1)                       # v6 приходит в .01 — ставим в метры для съёмки
if tgt.animation_data: tgt.animation_data.action = None
for pb in tgt.pose.bones:
    pb.rotation_mode = 'QUATERNION'
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
act = src.animation_data.action
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
bpy.context.view_layer.update()
print("src frames", f0, f1, "src rot", tuple(round(math.degrees(c), 1) for c in src.rotation_euler),
      "bind rot", tuple(round(math.degrees(c), 1) for c in bind.rotation_euler))


def rest_world(arm, name):
    m = arm.matrix_world @ arm.data.bones[name].matrix_local
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def pose_world(arm, name):
    m = arm.matrix_world @ arm.pose.bones[name].matrix
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def body_basis(pos):
    up = (pos["mixamorig:Head"] - pos["mixamorig:Hips"]).normalized()
    right = (pos["mixamorig:LeftArm"] - pos["mixamorig:RightArm"]).normalized()
    fwd = up.cross(right).normalized()
    right = fwd.cross(up).normalized()
    return Matrix((right, fwd, up)).transposed().to_quaternion()


names = [b.name for b in tgt.data.bones if b.name.startswith("mixamorig:")]
bind_pos = {n: rest_world(bind, n)[0] for n in names}
bind_rot = {n: rest_world(bind, n)[1] for n in names}
tgt_pos = {n: rest_world(tgt, n)[0] for n in names}
tgt_rot = {n: rest_world(tgt, n)[1] for n in names}
A = body_basis(tgt_pos) @ body_basis(bind_pos).inverted()
body_bind = (bind_pos["mixamorig:Hips"] - bind_pos["mixamorig:Head"]).length
hip_scale = (tgt_pos["mixamorig:Hips"] - tgt_pos["mixamorig:Head"]).length / body_bind
print("alignment deg", round(math.degrees(A.angle), 2), "hipScale", round(hip_scale, 4))

order = []
def walk(b):
    order.append(b.name)
    for c in b.children: walk(c)
walk(tgt.data.bones["mixamorig:Hips"])
src_names = set(b.name for b in src.data.bones)


def child_of(name):
    kids = [c.name for c in tgt.data.bones[name].children if c.name in src_names]
    for k in kids:
        if "Spine" in k or "Neck" in k: return k
    return kids[0] if kids else None


def retarget(frame):
    sc.frame_set(frame)
    bpy.context.view_layer.update()
    sp = {n: pose_world(src, n) for n in names}
    mwi = tgt.matrix_world.inverted()
    hips_rest = tgt_pos["mixamorig:Hips"]
    hips = hips_rest + A @ (sp["mixamorig:Hips"][0] - bind_pos["mixamorig:Hips"]) * hip_scale
    ratio = (hips - hips_rest).length / (body_bind * hip_scale)
    for n in order:
        pb = tgt.pose.bones[n]
        rot = A @ sp[n][1] @ bind_rot[n].inverted() @ A.inverted() @ tgt_rot[n]
        head = hips if n == "mixamorig:Hips" else (tgt.matrix_world @ pb.matrix).translation
        pb.matrix = mwi @ (Matrix.Translation(head) @ rot.to_matrix().to_4x4())
        bpy.context.view_layer.update()
        c = child_of(n)
        if c:
            want = A @ (sp[c][0] - sp[n][0])
            cur = (tgt.matrix_world @ tgt.pose.bones[c].matrix).translation - head
            fix = cur.rotation_difference(want)
            pb.matrix = mwi @ (Matrix.Translation(head) @ (fix @ rot).to_matrix().to_4x4())
            bpy.context.view_layer.update()
    worst = (0.0, "")
    for n in names:
        p = tgt.data.bones[n].parent
        if p is None or p.name not in src_names: continue
        e = math.degrees((A @ (sp[n][0] - sp[p.name][0])).angle(
            (tgt.matrix_world @ tgt.pose.bones[n].matrix).translation - (tgt.matrix_world @ tgt.pose.bones[p.name].matrix).translation))
        if e > worst[0]: worst = (e, n)
    return ratio, worst


# ---------------------------------------------------------------- сцена ---
mesh = meshes[0]
mat = bpy.data.materials.new("Pelag_v6")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEX)
nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
nt.nodes.active = img
for m in meshes:
    m.data.materials.clear(); m.data.materials.append(mat)

# якорь-заглушка (в позе покоя v6, герой стоит лицом в −Y; правый бок — −X)
def box(name, a, b, w, color, parent_bone="mixamorig:Spine2"):
    d = b - a
    me = bpy.data.meshes.new(name)
    v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
    me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    y = d.normalized(); x = y.cross(Vector((0, -1, 0)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    o.color = color
    bpy.context.view_layer.update()
    o.parent = tgt; o.parent_type = 'BONE'; o.parent_bone = parent_bone
    bpy.context.view_layer.update()
    o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * d.length, z * w)).transposed().to_4x4()
    return o

bpy.context.view_layer.update()
sp2 = tgt_pos["mixamorig:Spine2"]
back_y = sp2.y + 0.17
iron = (0.16, 0.16, 0.18, 1)
top = Vector((-0.13, back_y, tgt_pos["mixamorig:Neck"].z + 0.16))
bot = Vector((0.11, back_y + 0.02, tgt_pos["mixamorig:Spine"].z - 0.05))
box("AnchorShank", bot, top, 0.07, iron)
shank = (top - bot).normalized(); side = shank.cross(Vector((0, 1, 0))).normalized()
for sgn in (1, -1):
    tip = bot + side * sgn * 0.24 + shank * 0.14
    box("AnchorArm", bot, tip, 0.075, iron)
box("AnchorRing", top, top + shank * 0.11, 0.05, iron)

bl = make_blade_object((0.80, 0.80, 0.84, 1))
BLADE_SHOW = 0.75   # заглушка серии длиннее настоящей сабли (~1,17 м против ~0,85 м по after-side) — укорочена для вида


def place_sabre(o, arm, thickness):
    r, t = blade(arm)
    d = (t - r) * BLADE_SHOW
    y = d.normalized(); x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    o.matrix_world = Matrix.Translation(r) @ Matrix((x * thickness * 1.6, y * d.length, z * thickness * .5)).transposed().to_4x4()

steel = bpy.data.materials.new("steel"); steel.diffuse_color = (0.80, 0.80, 0.84, 1); bl.data.materials.append(steel)
iron_m = bpy.data.materials.new("iron"); iron_m.diffuse_color = iron
for o in bpy.data.objects:
    if o.name.startswith("Anchor"): o.data.materials.append(iron_m)
# земля
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.active_object; ground.color = (0.46, 0.34, 0.22, 1)
gm = bpy.data.materials.new("ground"); ground.data.materials.append(gm); gm.diffuse_color = (0.46, 0.34, 0.22, 1)
dots = bpy.data.materials.new("dots"); dots.diffuse_color = (0.36, 0.27, 0.17, 1)
for gx in range(-10, 11):
    for gy in range(-30, 31):
        bpy.ops.mesh.primitive_cube_add(size=0.03, location=(gx * 0.25, gy * 0.25, 0.0))
        bpy.context.active_object.data.materials.append(dots)

sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'
sh.show_shadows = True; sh.shadow_intensity = 0.35
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'
sc.view_settings.exposure = 0.8
sh.show_cavity = False
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


a = math.radians(15)
VIEWS = {
    "game": ((math.cos(a), -math.sin(a)), 48),
    "away": ((math.sin(math.radians(35)), -math.cos(math.radians(35))), 48),
    "side": ((1, 0), 0),
}
report = []
toe_rest = min((tgt.matrix_world @ tgt.data.bones[f"mixamorig:{s}Toe_End"].head_local).z for s in ("Left", "Right"))
if mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 400
    ticks = [int(t) for t in (argv[3].split(",") if len(argv) > 3 else "0,2,4,6,8,10,12".split(","))]
    for fr in range(f0, f1 + 1):
        t = fr - f0                               # импорт FBX ставит первый кадр на 1; тик = кадр − f0
        ratio, worst = retarget(fr)
        feet = {s: round(min((tgt.matrix_world @ tgt.pose.bones[f"mixamorig:{s}{b}"].head).z for b in ("ToeBase", "Toe_End", "Foot")), 3)
                for s in ("Left", "Right")}
        lean_v = (tgt.matrix_world @ tgt.pose.bones["mixamorig:Neck"].head) - (tgt.matrix_world @ tgt.pose.bones["mixamorig:Hips"].head)
        report.append(dict(frame=t, hip_offset_ratio=round(ratio, 3), worst_segment=[round(worst[0], 1), worst[1]], feet_min_z=feet,
                           lean=round(math.degrees(math.atan2(-lean_v.y, lean_v.z)), 1)))
        print("f%02d hip ratio %.3f worst seg %.1f %s feet %s lean %.1f" % (t, ratio, worst[0], worst[1], feet, report[-1]["lean"]))
        if t not in ticks: continue
        place_sabre(bl, tgt, 0.035)
        for vn, (vd, pitch) in VIEWS.items():
            look(vd, pitch, Vector((0, 0.12, 0.72)), 2.6)
            sc.render.filepath = os.path.join(out, f"{vn}_{t:03d}.png")
            bpy.ops.render.render(write_still=True)
    json.dump(dict(toe_rest=round(toe_rest, 3), frames=report), open(os.path.join(out, "v6_check.json"), "w"), indent=1)
    # Итог проверки переноса — в timing.json рядом с клипом.
    tp = os.path.join(anim, "timing.json")
    if CLIP == "Pelag_AN_Dash" and os.path.exists(tp):
        tj = json.load(open(tp, encoding="utf-8"))
        tj["unity_transfer_check"] = dict(
            method="d_render_v6.py: перечитанные FBX → Pelag_v6 по формуле RazlomPelagAuthoredClips.Build",
            alignment_deg=round(math.degrees(A.angle), 2), hip_scale=round(hip_scale / 100.0, 4),
            max_hip_offset_ratio=max(r["hip_offset_ratio"] for r in report), unity_default_limit=0.4,
            max_segment_error_deg=max(r["worst_segment"][0] for r in report),
            feet_min_z_m={str(r["frame"]): r["feet_min_z"] for r in report}, toe_rest_z_m=round(toe_rest, 3))
        json.dump(tj, open(tp, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
else:
    # Движение в игре: Sim везёт героя ~4 м за тики 0–6 (здесь — равномерно, допущение),
    # камера стоит, как игровая (48°), кадр шире пути.
    sc.render.resolution_x = 640; sc.render.resolution_y = 400
    seq = [0] * 8 + list(range(0, f1 - f0 + 1)) + [f1 - f0] * 14
    for i, t in enumerate(seq):
        tgt.location = (0, 0, 0)
        bpy.context.view_layer.update()
        retarget(f0 + t)
        travel = 4.0 * min(1.0, max(0.0, t / 6.0))
        tgt.location = (0, -travel + 2.0, 0)
        bpy.context.view_layer.update()
        place_sabre(bl, tgt, 0.035)
        look(VIEWS["game"][0], 48, Vector((0, 0.0, 0.6)), 6.4)
        sc.render.filepath = os.path.join(out, f"gif_{i:03d}.png")
        bpy.ops.render.render(write_still=True)
print("v6 render done", mode)
