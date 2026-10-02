"""Серия сабли Пелага: три клипа из Mixamo-тейка Pelag_MX_SaberCombo по таблице тиков.

Запуск: blender -b --factory-startup -P b_author.py -- <out_dir> [preview]

Тейк SaberCombo — это и есть серия: справа налево (контакт кадр ~11), слева
направо (~28) и рубящий сверху с шагом правой ноги (~44), потом возврат в
стойку (~74 ≈ кадр 2, тейк замкнут). Клипы — его куски, перевременённые под
сроки Sim (razlom/Docs/PelagBasicCombo.md): кадр клипа = тик, 30 к/с.

Время источника по тику — монотонная кубика через узлы (тик → кадр тейка):
замах тянется, удар идёт быстрее родного, проводка — родным темпом.
Таз в тейке стоит на месте (XY постоянны), так что корень клипа не уезжает.
"""
import bpy, sys, os, json, math
from mathutils import Vector, Quaternion, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import *

SRC = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_SaberCombo.fbx"

# (тик клипа, кадр тейка). Контакт: удар 1 — тик 4, удар 2 — тик 4, добивающий — 7.
# После конца удара (8 / 8 / 14) — хвост, который играет, только если серию не продолжили.
HITS = {
    "Sabre1": dict(knots=[(0, 2.0), (1, 3.4), (2.5, 7.0), (4, 11.0), (5, 13.4), (6, 15.2), (8, 19.0), (14, 20.0)],
                   contact=4, end=8, frames=14),
    "Sabre2": dict(knots=[(0, 20.0), (1, 22.0), (2, 25.0), (3, 26.7), (4, 28.2), (5, 29.2), (6, 30.0), (8, 31.3), (14, 31.8)],
                   contact=4, end=8, frames=14,
                   # Замах обратного удара: в тейке сабля за пять кадров обходит голову
                   # (RazlomCharacterImport, «полный оборот вокруг персонажа»). Верх тела
                   # идёт из конца первого удара (20) прямо в взведённую позу (26.7).
                   upper_blend=(0.0, 3.0, 20.0, 26.7)),
    "Sabre3": dict(knots=[(0, 31.5), (1.5, 34.5), (3, 39.0), (5, 42.0), (6, 43.0), (7, 44.0), (8, 45.2), (10, 49.0),
                          (14, 56.0), (18, 64.0), (22, 72.0)],
                   contact=7, end=14, frames=22, lunge=(3, 7),
                   # Верх тела проходит от занесённой сабли (кадр 36) прямо к верхней точке
                   # перед рубкой (42), без петли тейка вниз-влево (37–41): в семь тиков
                   # замаха петля читалась как дрожь. Ноги и таз идут по времени — шаг цел.
                   upper_blend=(2.0, 5.0, 36.0, 42.0),
                   # Выпад: Sim везёт корень на 0,6 м (≈ 0,30 ед. тейка) за тики 3–7. Задняя
                   # (левая) стопа в тейке почти стоит — с корнем она бы ехала по земле
                   # 3 м/с. Держим её на месте до тика 10, потом подтягиваем шагом с подъёмом.
                   plant=dict(side="Left", hold=(3, 10), release=(10, 14), lift=0.035, shift=0.30),
                   # Корпус: в тейке рубящий складывает героя пополам (наклон до 71° на
                   # тиках 8–9, голова у колен) — сверху читался комок, а при 41° голову
                   # закрывал якорь на спине (съёмка V5). Лист ключевых поз (поза 5) —
                   # наклон ~25°. Мягкий предел: сверх 20° остаётся 20% (максимум ~30°).
                   torso=dict(soft_from=20.0, keep=0.20)),
}

UPPER_ROOTS = ("mixamorig:Spine",)


def is_upper(dst, name):
    b = dst.data.bones[name]
    while b is not None:
        if b.name in UPPER_ROOTS: return True
        b = b.parent
    return False


def lunge_shift(spec, tick):
    a, b = spec["lunge"]
    return spec["plant"]["shift"] * min(1.0, max(0.0, (tick - a) / float(b - a)))


def solve_leg(arm, side, target):
    """Две кости ноги к цели стопы: колено остаётся в своей плоскости, стопа — в своей ориентации."""
    up, low, foot = (arm.pose.bones[f"mixamorig:{side}{n}"] for n in ("UpLeg", "Leg", "Foot"))
    mw = arm.matrix_world; inv = mw.inverted()
    hip = mw @ up.head; knee = mw @ low.head; ankle = mw @ foot.head
    foot_world = mw @ foot.matrix
    a = (knee - hip).length; b = (ankle - knee).length
    d = target - hip
    dist = min(d.length, (a + b) * 0.995)
    dn = d.normalized()
    pole = (knee - hip) - (knee - hip).dot((ankle - hip).normalized()) * (ankle - hip).normalized()
    pole = pole - pole.dot(dn) * dn
    if pole.length < 1e-9: pole = Vector((0, -1, 0))
    pole.normalize()
    x = (a * a - b * b + dist * dist) / (2 * dist)
    h = math.sqrt(max(0.0, a * a - x * x))
    knee2 = hip + dn * x + pole * h
    ankle2 = hip + dn * dist
    r1 = (knee - hip).rotation_difference(knee2 - hip)
    up_world = mw @ up.matrix
    up_new = Matrix.Translation(hip) @ r1.to_matrix().to_4x4() @ Matrix.Translation(-hip) @ up_world
    up.matrix = inv @ up_new
    bpy.context.view_layer.update()
    knee_now = mw @ low.head
    shin_old = (mw @ foot.head) - knee_now
    r2 = shin_old.rotation_difference(ankle2 - knee_now)
    low_world = mw @ low.matrix
    low_new = Matrix.Translation(knee_now) @ r2.to_matrix().to_4x4() @ Matrix.Translation(-knee_now) @ low_world
    low.matrix = inv @ low_new
    bpy.context.view_layer.update()
    fw = foot_world.copy(); fw.translation = mw @ foot.head
    foot.matrix = inv @ fw
    bpy.context.view_layer.update()


def soften_torso(arm, soft_from, keep):
    """Наклон корпуса (таз → шея) от вертикали сверх soft_from сжимается до keep.

    Лишний наклон снимается поворотом позвоночника (Spine/Spine1/Spine2 по
    .5/.3/.2) вокруг одной оси — плечи, руки и сабля идут за грудью, ноги и
    таз не трогаются. Возвращает снятый угол в градусах.
    """
    mw = arm.matrix_world
    hips = mw @ arm.pose.bones["mixamorig:Hips"].head
    neck = mw @ arm.pose.bones["mixamorig:Neck"].head
    v = (neck - hips).normalized()
    up = Vector((0, 0, 1))
    lean = math.degrees(v.angle(up))
    if lean <= soft_from: return 0.0
    # Цель — от наклона тейка, а не от уже исправленного: запоминается на кадр.
    raw = arm.get("_raw_lean")
    if raw is None or raw < lean: raw = lean
    arm["_raw_lean"] = raw
    target = soft_from + (raw - soft_from) * keep
    if lean <= target + .5: return 0.0
    excess = math.radians(lean - target)
    axis = v.cross(up)
    if axis.length < 1e-6: return 0.0
    axis.normalize()
    for name, weight in (("mixamorig:Spine", .5), ("mixamorig:Spine1", .3), ("mixamorig:Spine2", .2)):
        pb = arm.pose.bones[name]
        world = mw @ pb.matrix
        pivot = world.translation.copy()
        turn = Matrix.Translation(pivot) @ Quaternion(axis, excess * weight).to_matrix().to_4x4() @ Matrix.Translation(-pivot)
        pb.matrix = mw.inverted() @ (turn @ world)
        bpy.context.view_layer.update()
    return math.degrees(excess)


def reach_hand(arm, side, target):
    """Поворот плеча вокруг его головки: кисть смотрит на прежнюю точку, локоть цел."""
    mw = arm.matrix_world
    upper = arm.pose.bones[f"mixamorig:{side}Arm"]
    world = mw @ upper.matrix
    pivot = world.translation.copy()
    hand = mw @ arm.pose.bones[f"mixamorig:{side}Hand"].head
    turn_q = (hand - pivot).rotation_difference(target - pivot)
    turn = Matrix.Translation(pivot) @ turn_q.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
    upper.matrix = mw.inverted() @ (turn @ world)
    bpy.context.view_layer.update()


def smoother(t):
    t = max(0.0, min(1.0, t))
    return t * t * t * (t * (t * 6 - 15) + 10)


def pchip(knots, x):
    """Монотонная кубика Фрича–Карлсона: время источника не идёт назад и не перелетает узлы."""
    xs = [k[0] for k in knots]; ys = [k[1] for k in knots]
    n = len(xs)
    if x <= xs[0]: return ys[0]
    if x >= xs[-1]: return ys[-1]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0: m[i] = 0.0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    i = max(j for j in range(n - 1) if xs[j] <= x)
    t = (x - xs[i]) / h[i]
    h00 = 2 * t ** 3 - 3 * t ** 2 + 1; h10 = t ** 3 - 2 * t ** 2 + t
    h01 = -2 * t ** 3 + 3 * t ** 2; h11 = t ** 3 - t ** 2
    return h00 * ys[i] + h10 * h[i] * m[i] + h01 * ys[i + 1] + h11 * h[i] * m[i + 1]


def sample_pose(scene, src, frame):
    f = int(math.floor(frame)); sub = frame - f
    scene.frame_set(f, subframe=sub)
    bpy.context.view_layer.update()
    return {pb.name: pb.matrix_basis.copy() for pb in src.pose.bones}


def build():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = argv[0]
    preview = len(argv) > 1 and argv[1] == "preview"
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    src = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    scene = bpy.context.scene
    scene.render.fps = 30

    # Копия рига без анимации — в неё пишутся новые действия.
    dst = src.copy(); dst.data = src.data.copy(); dst.name = "PelagSabre"
    dst.animation_data_clear()
    scene.collection.objects.link(dst)
    for pb in dst.pose.bones: pb.rotation_mode = 'QUATERNION'

    report = {}
    actions = {}
    for name, spec in HITS.items():
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        dst.animation_data_create(); dst.animation_data.action = act
        prev = {}
        rows = []
        planted = held = None
        for tick in range(spec["frames"] + 1):
            s = pchip(spec["knots"], tick)
            pose = sample_pose(scene, src, s)
            ub = spec.get("upper_blend")
            if ub and ub[0] <= tick < ub[1]:
                a = sample_pose(scene, src, ub[2]); b = sample_pose(scene, src, ub[3])
                w = smoother((tick - ub[0]) / (ub[1] - ub[0]))
                for n in pose:
                    if not is_upper(dst, n): continue
                    la, ra, _ = a[n].decompose(); lb, rb, _ = b[n].decompose()
                    if ra.dot(rb) < 0: rb.negate()
                    pose[n] = Matrix.LocRotScale(la.lerp(lb, w), ra.slerp(rb, w), Vector((1, 1, 1)))
            for pb in dst.pose.bones:
                loc, rot, scl = pose[pb.name].decompose()
                pb.location = loc; pb.rotation_quaternion = rot; pb.scale = scl
            bpy.context.view_layer.update()
            torso = spec.get("torso")
            if "_raw_lean" in dst: del dst["_raw_lean"]
            if torso:
                hands = {side: dst.matrix_world @ dst.pose.bones[f"mixamorig:{side}Hand"].head
                         for side in ("Right", "Left")}
                # Поворот идёт вокруг позвоночника, а наклон меряется от таза:
                # за проход снимается не весь излишек — три прохода сходятся.
                for _ in range(3):
                    soften_torso(dst, torso["soft_from"], torso["keep"])
                # Выпрямленная грудь подняла бы руки, и рубящий кончался бы саблей
                # на уровне груди (съёмка V4: кончик 1,3–2 м). Руки возвращаются к
                # своим точкам из тейка: поза 5 листа — корпус прямее, руки вниз.
                for side, target in hands.items():
                    reach_hand(dst, side, target)
            plant = spec.get("plant")
            if plant:
                side = plant["side"]
                ankle = dst.matrix_world @ dst.pose.bones[f"mixamorig:{side}Foot"].head
                h0, h1 = plant["hold"]; r0, r1 = plant["release"]
                if tick == h0: planted = ankle.copy()
                if h0 < tick <= h1:
                    # корень уехал на shift — в пространстве клипа стопа отъезжает назад (+Y)
                    target = planted + Vector((0, lunge_shift(spec, tick), 0))
                    held = target.copy()
                    solve_leg(dst, side, target)
                elif r0 < tick < r1:
                    u = smoother((tick - r0) / float(r1 - r0))
                    target = held.lerp(ankle, u)
                    target.z += plant["lift"] * math.sin(math.pi * u)
                    solve_leg(dst, side, target)
            for pb in dst.pose.bones:
                rot = pb.rotation_quaternion.copy()
                q = prev.get(pb.name)
                if q is not None and q.dot(rot) < 0: rot.negate(); pb.rotation_quaternion = rot
                prev[pb.name] = rot.copy()
                pb.keyframe_insert("location", frame=tick, group=pb.name)
                pb.keyframe_insert("rotation_quaternion", frame=tick, group=pb.name)
            rows.append(dict(tick=tick, source=round(s, 3)))
        actions[name] = act
        report[name] = rows
    json.dump(report, open(os.path.join(out, "timing.json"), "w"), indent=1)

    # Линейные ключи: кадр клипа = тик, сглаживание уже в выборке.
    for act in actions.values():
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'

    # Экспорт: по одному клипу на FBX и привязка (тот же риг в покое).
    bpy.ops.object.select_all(action='DESELECT')
    dst.select_set(True); bpy.context.view_layer.objects.active = dst
    for name, act in actions.items():
        dst.animation_data.action = act
        scene.frame_start, scene.frame_end = 0, HITS[name]["frames"]
        bpy.ops.export_scene.fbx(filepath=os.path.join(out, f"Pelag_AN_{name}.fbx"), use_selection=True,
                                 object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
                                 bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                                 bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.animation_data.action = None
    for pb in dst.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    # Покой рига Mixamo из тейка лежит (поворот объекта +90° по X, стоя по Z
    # арматуры), а кадры тейка стоят. RazlomPelagAuthoredClips меряет повороты и
    # таз от покоя привязки — привязка выгружается стоя, без поворота объекта
    # (иначе таз «уезжал» на 1,65 длины корпуса и сборка клипа падала).
    keep_rotation = dst.rotation_euler.copy()
    dst.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Pelag_AN_SabreBind.fbx"), use_selection=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=False, armature_nodetype='NULL')
    dst.rotation_euler = keep_rotation
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "Pelag_Sabre_Work.blend"))

    if preview:
        render_preview(out, src, dst, mesh, actions)
    print("authored", list(actions))


def render_preview(out, src, dst, mesh, actions):
    """Тейк-меш вешается на новый риг; клинок-заглушка; кадры спереди и с игровой камеры."""
    scene = bpy.context.scene
    src.animation_data.action = None
    mod = [m for m in mesh.modifiers if m.type == 'ARMATURE'][0]
    mod.object = dst
    mw = mesh.matrix_world.copy(); mesh.parent = dst; mesh.matrix_world = mw
    # Метки на земле через 0,05 ед. (~10 см) — по ним видно, скользит ли стопа.
    for gx in range(-6, 7):
        for gy in range(-8, 9):
            bpy.ops.mesh.primitive_cube_add(size=0.012, location=(gx * 0.05, gy * 0.05, 0.003))
            bpy.context.active_object.color = (0.2, 0.2, 0.2, 1)
    mesh.color = (0.78, 0.74, 0.70, 1)
    bl = make_blade_object()
    cam = setup_render(300)
    height = (wpos(dst, "mixamorig:Head") - wpos(dst, "mixamorig:LeftFoot")).length
    trace = {}
    only = os.environ.get("SABRE_PREVIEW_ONLY")
    for name, act in actions.items():
        if only and name not in only.split(","): continue
        dst.animation_data.action = act
        spec = HITS[name]
        tips = []
        for tick in range(spec["frames"] + 1):
            scene.frame_set(tick)
            # Выпад добивающего: корень едет как в Sim (0,6 м ≈ 0,3 ед. тейка за 4 тика).
            shift = 0.0
            if "lunge" in spec:
                a, b = spec["lunge"]
                shift = 0.30 * min(1.0, max(0.0, (tick - a) / float(b - a)))
            dst.location = (0, -shift, 0)
            bpy.context.view_layer.update()
            place_blade(bl, dst, height * 0.018)
            r, t = blade(dst)
            tips.append([round(v, 4) for v in t] + [round(v, 4) for v in r])
            tgt = Vector((0, -0.1, 0.35))
            for v in ("front", "game", "top"):
                aim_camera(cam, tgt, v, height, height * 2.8)
                scene.render.filepath = os.path.join(out, "preview", f"{name}_{v}_{tick:03d}.png")
                bpy.ops.render.render(write_still=True)
        trace[name] = tips
        dst.location = (0, 0, 0)
    json.dump(trace, open(os.path.join(out, "blade_trace.json"), "w"))


build()
