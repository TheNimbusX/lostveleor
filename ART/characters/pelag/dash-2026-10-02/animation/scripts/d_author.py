"""Дэш Пелага (Pelag_AN_Dash) — клип, собранный руками в Blender, без рефа движения.

Запуск: blender -b --factory-startup -P d_author.py -- <out_dir>

Владелец 02.10: «реф получился говно, кредов больше нет, поэтому собираем без
рефа». Клип ставится на риг Pelag_v6 (тот же, что в игре). Полнотелые клипы
(Roll, Skewer…) собраны на риге пропорций v6; риг Mixamo-тейков (серия сабли)
короче в ногах, и перенос RazlomPelagAuthoredClips поднимает его стопы над
землёй на ~15 см и отклоняет корпус на 8° назад (проверено d_render_v6.py на
Pelag_AN_Sabre1; в игре сабля играет только верхом тела, поэтому там не видно).

Слои поверх стойки: стойка серии сабли (Pelag_MX_SaberCombo кадр 2 = первый
кадр Pelag_AN_Sabre1), перенесённая на v6 как в Unity и поставленная стопами на
землю; затем таз, наклон корпуса, ноги (IK к точкам стоп), руки (IK от плеча),
сабля (разворот кисти к направлению клинка), шея/голова (взгляд вдоль рывка).
Каждый слой — таблица по тикам и вес; на тике 12 все веса 0 (клип кончается
стойкой), тик 0 — та же стойка с крошечным присядом: вход и выход сшиваются.

Кадр клипа = тик, 30 к/с. Таз по XY стоит (корневого хода нет — тело везёт
Sim), по Z — присед. Направление рывка — −Y (куда смотрят тейки и v6).
Расстояния в таблицах — в единицах тейка (мерились по тейкам) и при сборке
умножаются на K = длина ноги v6 / длина ноги тейка (~2,2).

Откуда что:
  * стойка тиков 0 и 12, кисти/пальцы, хват сабли — SaberCombo кадр 2;
  * тайминг «сразу в полную позу за 2 тика» — Pelag_AN_Skewer (кадры 1→3);
  * высота стопы в контакте (лодыжка 0,073), длина шага бега — Pelag_MX_Run;
  * толчок задней ногой с поднятой пяткой — Pelag_MX_RunStart (кадры 4–7);
  * наклон ~40°, длинный шаг, обе руки назад — кадр А (1-A-foam-wake.png);
  * постановка передней стопы на выходе — кадр А5 (8-A5-wake-arrival-splash.png).

v2 (02.10, правки по критику, остальное как было):
  * голова СЛЕДУЕТ за грудью. В v1 лицо держалось «на 15° ниже горизонта» в мире,
    и при корпусе 40° голова запрокидывалась к груди (relP до 50° при 9,8° в стойке —
    тот самый задранный подбородок). Теперь шея и голова сохраняют повороты стойки
    относительно груди: relP (угол лица над плоскостью груди, как в
    artifacts/tools/pelag-head/headlib.py) на каждом тике = стойке, потолок правила
    Sabre3. Разрешён только поворот вбок вокруг оси «вверх груди» (relP от него не
    меняется), чтобы лицо по горизонтали смотрело вдоль рывка.
  * левая кисть/предплечье не входят в левое (переднее) бедро: после слоя рук
    проверка по сетке v6 (пересечение треугольников + глубина), отвод руки в плече
    от бедра; поправки сглажены по тикам, тик 12 (стойка) не трогается.
"""
import bpy, sys, os, json, math
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import wpos, hand_frame, blade

CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"
SRC = CH + r"\Pelag_v5\Mixamo\Pelag_MX_SaberCombo.fbx"
BASE_FRAME = 2.0
FRAMES = 12
UP = Vector((0, 0, 1))
X = Vector((1, 0, 0))

# ---------------------------------------------------------------- таблицы ---
# Таз: смещение по Z от стойки (ед. тейка; 1 ед. ≈ 2 м в игре), наклон таза вперёд (°).
HIPS_DZ = [(0, -.005), (1, -.014), (2, -.022), (4, -.025), (5, -.027), (6, -.030), (7, -.031),
           (8, -.027), (9, -.019), (10, -.011), (11, -.004), (12, 0)]
PELVIS = [(0, 2), (1, 7), (2, 12), (5, 12), (6, 11), (7, 9), (8, 6), (10, 2.5), (12, 0)]
# Наклон корпуса (таз → шея) от вертикали в сагиттальной плоскости, градусы.
# Последний узел подменяется наклоном стойки (на тике 12 слой выключен).
LEAN = [(0, 22), (1, 32), (2, 40), (3, 41), (4, 41), (5, 40), (6, 38), (7, 35), (8, 30),
        (9, 24), (10, 17), (11, 11), (12, None)]
# Стопы: смещение лодыжки от стойки (dy, dz) и наклон стопы сверх стойки (°, + носок вниз).
# Левая — передняя: стоит на тике 0, летит вперёд-вверх, ставится на тике 6 туда же,
# где стоит в стойке (с 6 по 12 не двигается — скольжения нет). Правая — толчковая:
# пятка вверх на 0–1, тянется назад прямой ногой 2–6, подтягивается и встаёт к 11.
FOOT_L = [(0, (0, 0, 0)), (1, (-.02, .028, 8)), (2, (-.055, .048, -4)), (3, (-.068, .046, -8)),
          (4, (-.070, .040, -8)), (5, (-.042, .022, -4)), (6, (0, 0, 0)), (12, (0, 0, 0))]
FOOT_R = [(0, (0, 0, 8)), (1, (.06, .042, 20)), (2, (.17, .06, 32)), (3, (.20, .07, 34)),
          (4, (.21, .07, 34)), (5, (.20, .06, 33)), (6, (.18, .035, 32)), (7, (.14, .062, 28)),
          (8, (.09, .065, 20)), (9, (.04, .05, 10)), (10, (.01, .014, 3)), (11, (0, 0, 0)), (12, (0, 0, 0))]
# Контакты с землёй (тики): передняя левая встаёт на 6 и стоит до конца — здесь всплеск
# выхода из рывка (кадр А5); задняя правая толкается на 0, касается носком на 6
# (Sim уже стоит) и встаёт на 11. В тиках 1–5 обе стопы над землёй: тело везёт Sim
# со скоростью ~0,67 м/тик, любая опора там скользила бы.
CONTACT = {"Left": [[0, 0], [6, 12]], "Right": [[0, 0], [6, 6], [11, 12]]}
SPLASH_TICK = 6
SOURCES = {
    "0": "стойка серии сабли (SaberCombo кадр 2, перенос на v6 как в Unity, стопы на землю) + присед 1 см, корпус 22°, руки/клинок начинают назад",
    "1": "толчок задней (правой): пятка вверх как в RunStart 4–7, носок отрывается; передняя колено вперёд; корпус 32°",
    "2": "полная поза за 2 тика, как Skewer 1→3; корпус 40°, длинный шаг, сабля назад низко, левая рука назад-в сторону — кадр А",
    "3": "скольжение (как 2, корпус 41°), передняя стопа впереди над землёй, задняя нога вытянута назад",
    "4": "скольжение (пик), передняя стопа на 12 см над землёй, задняя — носок на 5 см",
    "5": "передняя стопа идёт вниз к земле, подгребая назад",
    "6": "ПОСТАНОВКА передней (левой) стопы — всплеск А5; корпус 38°, таз ниже, задняя касается носком",
    "7": "гашение: таз ниже всего, корпус 35°, задняя нога отрывается",
    "8": "подъём: корпус 30°, задняя нога подтягивается, руки и клинок возвращаются",
    "9": "задняя нога в воздухе проходит вперёд, корпус 24°",
    "10": "задняя опускается, корпус 17°, клинок поднимается к стойке",
    "11": "задняя (правая) встаёт на место стойки, корпус 11°",
    "12": "стойка серии сабли = первый кадр Pelag_AN_Sabre1 (все слои выключены)",
}
# Руки: направление от плеча (мир, герой смотрит в −Y), доля длины руки, подсказка локтя.
ARM_R_DIR = [(0, (-.30, .40, -.86)), (1, (-.30, .54, -.78)), (2, (-.32, .58, -.75)),
             (5, (-.34, .60, -.72)), (7, (-.32, .56, -.76)), (12, (-.30, .40, -.86))]
ARM_L_DIR = [(0, (.36, .38, -.85)), (1, (.46, .70, -.55)), (2, (.50, .76, -.41)),
             (5, (.54, .74, -.40)), (7, (.52, .68, -.52)), (12, (.36, .38, -.85))]
ARM_REACH = .94
POLE_R = Vector((-.35, .70, .62)).normalized()
POLE_L = Vector((.35, .70, .62)).normalized()
W_ARMS = [(0, .15), (1, .70), (2, 1), (7, 1), (8, .80), (9, .55), (10, .30), (11, .10), (12, 0)]
# Клинок: назад, чуть наружу и вниз — «сабля низко сзади». Кисть не гнётся больше WRIST_MAX.
BLADE_DIR = [(0, (-.40, .62, -.68)), (1, (-.56, .72, -.40)), (2, (-.62, .74, -.25)),
             (6, (-.62, .74, -.25)), (8, (-.55, .70, -.45)), (12, (-.40, .62, -.68))]
W_BLADE = [(0, .10), (1, .60), (2, 1), (7, 1), (9, .50), (11, .12), (12, 0)]
WRIST_MAX = 40.0
FOREARM_MAX = 50.0   # пронация предплечья, не больше
# Голова (v2): наклон к груди — как в стойке (шея/голова не гнутся), вес — только
# для поворота вбок к направлению рывка. HEAD_YAW_MAX — предел этого поворота.
W_HEAD = [(0, .60), (1, .90), (2, 1), (8, 1), (10, .70), (11, .40), (12, 0)]
HEAD_YAW_MAX = 40.0
# Отвод руки от бедра (v2): шаг и предел поворота в плече, сглаживание соседних тиков.
CLEAR_STEP = 1.5
CLEAR_MAX = 35.0
CLEAR_SPREAD = .55
CLEAR_SEARCH = .06       # глубина ищется в пределах 6 см от поверхности бедра
CLEAR_DEEP = .005        # «вошла» — вершина глубже 5 мм


# ------------------------------------------------------------- интерполяция ---
def pchip(knots, x):
    """Монотонная кубика Фрича–Карлсона (как в серии сабли): без перелётов узлов."""
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


def table(knots, t):
    first = knots[0][1]
    if isinstance(first, (tuple, list)):
        return tuple(pchip([(k, v[c]) for k, v in knots], t) for c in range(len(first)))
    return pchip(knots, t)


# ----------------------------------------------------------- позы и кости ---
def mw3(arm):
    return arm.matrix_world.to_3x3().normalized()


def bone_world_rot(arm, pb):
    return mw3(arm) @ pb.matrix.to_3x3().normalized()


def rotate_world(arm, pb, quat, pivot):
    mw = arm.matrix_world
    turn = Matrix.Translation(pivot) @ quat.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
    pb.matrix = mw.inverted() @ (turn @ (mw @ pb.matrix))
    bpy.context.view_layer.update()


def translate_world(arm, pb, delta):
    mw = arm.matrix_world
    pb.matrix = mw.inverted() @ (Matrix.Translation(delta) @ (mw @ pb.matrix))
    bpy.context.view_layer.update()


def sagittal_lean(arm):
    v = wpos(arm, "mixamorig:Neck") - wpos(arm, "mixamorig:Hips")
    return math.degrees(math.atan2(-v.y, v.z))


def foot_pitch(arm, side):
    v = wpos(arm, f"mixamorig:{side}ToeBase") - wpos(arm, f"mixamorig:{side}Foot")
    return math.degrees(math.atan2(-v.z, Vector((v.x, v.y)).length)), Vector((v.x, v.y, 0)).normalized()


def two_bone(arm, names, target, pole_hint=None, keep_end=False):
    """Две кости к цели: колено/локоть в плоскости подсказки (или своей), конец — по желанию в своей ориентации."""
    up, low, end = (arm.pose.bones[n] for n in names)
    mw = arm.matrix_world
    a = mw @ up.head; b = mw @ low.head; c = mw @ end.head
    end_world = (mw @ end.matrix).copy()
    la = (b - a).length; lb = (c - b).length
    d = target - a
    dist = max(1e-5, min(d.length, (la + lb) * 0.999))
    dn = d.normalized()
    if pole_hint is None:
        pole = (b - a) - (b - a).dot((c - a).normalized()) * (c - a).normalized()
    else:
        pole = pole_hint.copy()
    pole = pole - pole.dot(dn) * dn
    if pole.length < 1e-9: pole = Vector((0, 1, 0)) - dn.y * dn
    pole.normalize()
    x = (la * la - lb * lb + dist * dist) / (2 * dist)
    h = math.sqrt(max(0.0, la * la - x * x))
    b2 = a + dn * x + pole * h
    c2 = a + dn * dist
    rotate_world(arm, up, (b - a).rotation_difference(b2 - a), a)
    b_now = mw @ low.head; c_now = mw @ end.head
    rotate_world(arm, low, (c_now - b_now).rotation_difference(c2 - b_now), b_now)
    if keep_end:
        fw = end_world.copy(); fw.translation = mw @ end.head
        end.matrix = mw.inverted() @ fw
        bpy.context.view_layer.update()
    return (mw @ end.head - target).length


def slerp_dir(a, b, w):
    return a.normalized().slerp(b.normalized(), max(0.0, min(1.0, w))) if a.angle(b) > 1e-6 else b.normalized()


def import_rig(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    meshes = [o for o in new if o.type == 'MESH']
    return arm, (meshes[0] if meshes else None)


def body_basis(pos):
    up = (pos["mixamorig:Head"] - pos["mixamorig:Hips"]).normalized()
    right = (pos["mixamorig:LeftArm"] - pos["mixamorig:RightArm"]).normalized()
    fwd = up.cross(right).normalized()
    right = fwd.cross(up).normalized()
    return Matrix((right, fwd, up)).transposed().to_quaternion()


def hierarchy(arm):
    order = []
    def walk(b):
        order.append(b.name)
        for c in b.children: walk(c)
    walk(arm.data.bones["mixamorig:Hips"])
    return order


# ------------------------------------------------- сетка v6: части тела ---
def part_of(bone):
    """Часть тела по кости с наибольшим весом вершины (как в независимой проверке)."""
    b = bone.replace("mixamorig:", "")
    if b in ("Hips", "Spine", "Spine1", "Spine2", "LeftShoulder", "RightShoulder"): return "torso"
    if b.startswith("Neck") or b.startswith("Head") or "Eye" in b: return "head"
    for s in ("Left", "Right"):
        if b.startswith(s):
            r = b[len(s):]
            if r == "Arm": return s + "UpperArm"
            if r == "ForeArm": return s + "ForeArm"
            if r.startswith("Hand"): return s + "Hand"
            if r == "UpLeg": return s + "Thigh"
            if r == "Leg": return s + "Shin"
            if r in ("Foot", "ToeBase", "Toe_End"): return s + "Foot"
    return "other"


class Body:
    """Сетка Pelag_v6 после скиннинга: пересечения и глубина между частями тела."""

    def __init__(self, mesh):
        self.mesh = mesh
        vg = {g.index: g.name for g in mesh.vertex_groups}
        self.vpart = []
        for v in mesh.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            self.vpart.append(part_of(vg[best.group]) if best is not None else "other")
        mesh.data.calc_loop_triangles()
        self.tris = [tuple(t.vertices) for t in mesh.data.loop_triangles]
        self.tpart = []
        for t in self.tris:
            ps = {self.vpart[i] for i in t}
            self.tpart.append(ps.pop() if len(ps) == 1 else "seam")

    def verts(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.mesh.evaluated_get(dg)
        me = ev.to_mesh()
        mw = self.mesh.matrix_world
        vs = [mw @ v.co for v in me.vertices]
        ev.to_mesh_clear()
        return vs

    def bvh(self, vs, parts):
        idx = [i for i, p in enumerate(self.tpart) if p in parts]
        return BVHTree.FromPolygons(vs, [self.tris[i] for i in idx], all_triangles=True), idx

    def overlap(self, vs, a, b):
        """Число треугольников части a, пересекающих часть b (общие вершины не в счёт)."""
        ta, ia = self.bvh(vs, [a]); tb, ib = self.bvh(vs, [b])
        hit = set()
        for x, y in ta.overlap(tb):
            if set(self.tris[ia[x]]) & set(self.tris[ib[y]]): continue
            hit.add(ia[x])
        return hit

    def contact(self, a_parts, b_part, vs=None):
        """Части a_parts в части b_part: (треугольники-пересечения, вершин глубже CLEAR_DEEP,
        наибольшая глубина, центр «вошедших» вершин)."""
        vs = vs or self.verts()
        tri = set()
        for a in a_parts: tri |= self.overlap(vs, a, b_part)
        bad = {v for t in tri for v in self.tris[t]}
        tb, _ = self.bvh(vs, [b_part])
        deep = 0; worst = 0.0
        for i, (v, p) in enumerate(zip(vs, self.vpart)):
            if p not in a_parts: continue
            loc, nor, fi, dist = tb.find_nearest(v, CLEAR_SEARCH)
            if loc is None or (v - loc).dot(nor) >= 0: continue
            worst = max(worst, dist)
            if dist > CLEAR_DEEP: deep += 1; bad.add(i)
        cen = sum((vs[i] for i in bad), Vector()) / len(bad) if bad else None
        return len(tri), deep, worst, cen


def transfer_stance(src, dst, rest):
    """Поза тейка → v6 так же, как RazlomPelagAuthoredClips.Build переносит клип в Unity.

    Привязка тейка — его покой стоя (объект без поворота, масштаб тейка).
    Возвращает K — отношение длины ноги v6 к ноге тейка.
    """
    names = [n for n in dst.data.bones.keys() if n in src.data.bones]
    S = Matrix.Diagonal(src.scale.to_4d())
    bind = {n: S @ src.data.bones[n].matrix_local for n in names}
    bpos = {n: m.translation.copy() for n, m in bind.items()}
    brot = {n: m.to_3x3().normalized().to_quaternion() for n, m in bind.items()}
    tpos = {n: rest[n].translation.copy() for n in names}
    trot = {n: rest[n].to_3x3().normalized().to_quaternion() for n in names}
    A = body_basis(tpos) @ body_basis(bpos).inverted()
    hip_scale = (tpos["mixamorig:Hips"] - tpos["mixamorig:Head"]).length / (bpos["mixamorig:Hips"] - bpos["mixamorig:Head"]).length
    sp = {}
    for n in names:
        m = src.matrix_world @ src.pose.bones[n].matrix
        sp[n] = (m.translation.copy(), m.to_3x3().normalized().to_quaternion())
    mwi = dst.matrix_world.inverted()
    hips = tpos["mixamorig:Hips"] + A @ (sp["mixamorig:Hips"][0] - bpos["mixamorig:Hips"]) * hip_scale
    for n in hierarchy(dst):
        if n not in sp: continue
        pb = dst.pose.bones[n]
        rot = A @ sp[n][1] @ brot[n].inverted() @ A.inverted() @ trot[n]
        head = hips if n == "mixamorig:Hips" else (dst.matrix_world @ pb.matrix).translation
        pb.matrix = mwi @ (Matrix.Translation(head) @ rot.to_matrix().to_4x4())
        bpy.context.view_layer.update()
        kids = [c.name for c in dst.data.bones[n].children if c.name in sp]
        child = next((k for k in kids if "Spine" in k or "Neck" in k), kids[0] if kids else None)
        if child:
            want = A @ (sp[child][0] - sp[n][0])
            cur = (dst.matrix_world @ dst.pose.bones[child].matrix).translation - head
            pb.matrix = mwi @ (Matrix.Translation(head) @ (cur.rotation_difference(want) @ rot).to_matrix().to_4x4())
            bpy.context.view_layer.update()
    leg_t = tpos["mixamorig:Hips"].z - tpos["mixamorig:LeftFoot"].z
    leg_s = bpos["mixamorig:Hips"].z - bpos["mixamorig:LeftFoot"].z
    print("перенос стойки: выравнивание %.1f°, hipScale %.3f" % (math.degrees(A.angle), hip_scale))
    return leg_t / leg_s


def ground_stance(dst, rest):
    """Стойка после переноса висит над землёй (ноги тейка короче) — таз опускается,
    стопы ставятся IK на землю плоско, как в покое v6, с поворотом носка из стойки."""
    mw = dst.matrix_world
    ankle_rest = {s: rest[f"mixamorig:{s}Foot"].translation.z for s in ("Left", "Right")}
    ankles = {s: wpos(dst, f"mixamorig:{s}Foot").copy() for s in ("Left", "Right")}
    yaw = {}
    for s in ("Left", "Right"):
        v = wpos(dst, f"mixamorig:{s}ToeBase") - ankles[s]
        r = rest[f"mixamorig:{s}ToeBase"].translation - rest[f"mixamorig:{s}Foot"].translation
        a, b = Vector((r.x, r.y)).normalized(), Vector((v.x, v.y)).normalized()
        yaw[s] = math.atan2(a.x * b.y - a.y * b.x, a.dot(b))
    drop = min(ankles[s].z - ankle_rest[s] for s in ankles)
    if drop > 0:
        translate_world(dst, dst.pose.bones["mixamorig:Hips"], Vector((0, 0, -drop)))
    for s in ("Left", "Right"):
        target = Vector((ankles[s].x, ankles[s].y, ankle_rest[s]))
        two_bone(dst, [f"mixamorig:{s}UpLeg", f"mixamorig:{s}Leg", f"mixamorig:{s}Foot"], target)
        foot = dst.pose.bones[f"mixamorig:{s}Foot"]
        rot = Matrix.Rotation(yaw[s], 3, 'Z') @ rest[f"mixamorig:{s}Foot"].to_3x3().normalized()
        foot.matrix = mw.inverted() @ (Matrix.Translation(wpos(dst, f"mixamorig:{s}Foot")) @ rot.to_4x4())
        bpy.context.view_layer.update()
        for t in ("ToeBase", "Toe_End"):
            pb = dst.pose.bones[f"mixamorig:{s}{t}"]
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
        bpy.context.view_layer.update()
    print("стойка: таз опущен на %.3f м, стопы на земле" % max(drop, 0))
    return min(rest[f"mixamorig:{s}{t}"].translation.z for s in ("Left", "Right") for t in ("ToeBase", "Toe_End"))


# ----------------------------------------------------------------- сборка ---
def build():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = argv[0]
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    dst, dst_mesh = import_rig(V6)
    dst.name = "PelagDash"; dst_mesh.name = "Pelag_v6"
    if dst.animation_data: dst.animation_data.action = None
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    export_scale = dst.scale.copy()
    dst.scale = (1, 1, 1)          # сборка в метрах; перед выгрузкой масштаб рига возвращается
    for pb in dst.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    rest = {n: (dst.matrix_world @ dst.data.bones[n].matrix_local) for n in dst.data.bones.keys()}

    src, src_mesh = import_rig(SRC)
    scene.frame_set(int(BASE_FRAME))
    bpy.context.view_layer.update()
    K = transfer_stance(src, dst, rest)
    for o in (src, src_mesh):
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    toe_floor = ground_stance(dst, rest)
    base = {pb.name: pb.matrix_basis.copy() for pb in dst.pose.bones}
    print("K (нога v6 / нога тейка) %.3f, носок в покое %.4f" % (K, toe_floor))

    def reset():
        for pb in dst.pose.bones:
            loc, rot, scl = base[pb.name].decompose()
            pb.location = loc; pb.rotation_quaternion = rot; pb.scale = scl
        bpy.context.view_layer.update()

    reset()
    B = dict(hips=wpos(dst, "mixamorig:Hips").copy(), lean=sagittal_lean(dst),
             ankle={s: wpos(dst, f"mixamorig:{s}Foot").copy() for s in ("Left", "Right")},
             pitch={s: foot_pitch(dst, s)[0] for s in ("Left", "Right")},
             toe={s: min(wpos(dst, f"mixamorig:{s}ToeBase").z, wpos(dst, f"mixamorig:{s}Toe_End").z) for s in ("Left", "Right")})
    LEAN[-1] = (12, B["lean"])
    # Лицо в покое v6 (стоит, смотрит в −Y).
    head_rest = rest["mixamorig:Head"].to_3x3().normalized()
    face_local = head_rest.transposed() @ Vector((0, -1, 0))

    def face():
        return bone_world_rot(dst, dst.pose.bones["mixamorig:Head"]) @ face_local

    def face_pitch():
        f = face()
        return math.degrees(math.atan2(-f.z, Vector((f.x, f.y)).length)), f

    B["face"] = face_pitch()[0]

    # Оси груди/шеи/головы в покое — как artifacts/tools/pelag-head/headlib.py
    # (тот же замер relP, по которому принимали правку головы в Sabre3).
    def P_(n): return rest["mixamorig:" + n].translation
    up0 = (P_("Head") - P_("Hips")).normalized()
    right0 = P_("RightArm") - P_("LeftArm"); right0 = (right0 - right0.dot(up0) * up0).normalized()
    fwd0 = up0.cross(right0).normalized()
    if (P_("LeftToeBase") - P_("LeftFoot")).dot(fwd0) < 0: fwd0 = -fwd0
    right0 = fwd0.cross(up0).normalized()
    HL = {}
    for n in ("Spine2", "Neck", "Head"):
        r = rest["mixamorig:" + n].to_3x3().normalized().to_quaternion().inverted()
        HL[n] = (r @ fwd0, r @ up0, r @ right0)

    def hframe(n):
        q = bone_world_rot(dst, dst.pose.bones["mixamorig:" + n]).to_quaternion()
        return tuple(q @ v for v in HL[n])

    def asind(x): return math.degrees(math.asin(max(-1.0, min(1.0, x))))

    def head_m():
        cf, cu, cr = hframe("Spine2"); nf, nu, nr = hframe("Neck"); hf, hu, hr = hframe("Head")
        return dict(relP=round(asind(hf.dot(cu)), 2), relY=round(math.degrees(math.atan2(hf.dot(cr), hf.dot(cf))), 2),
                    neckP=round(asind(nf.dot(cu)), 2), headOnNeckP=round(asind(hf.dot(nu)), 2),
                    chestP=round(asind(cf.z), 2), faceP=round(asind(hf.z), 2),
                    faceYaw=round(math.degrees(math.atan2(hf.x, -hf.y)), 2))

    B["head"] = head_m()
    body = Body(dst_mesh)
    vs0 = body.verts()
    print("сетка: высота %.3f м, частей %d" % (max(v.z for v in vs0) - min(v.z for v in vs0), len(set(body.tpart))))
    B["contact"] = {s: body.contact([s + "Hand", s + "ForeArm"], s + "Thigh", vs0)[:3] for s in ("Left", "Right")}
    print("стойка: голова", B["head"], "кисть/предплечье в бедре", B["contact"])
    arm_len = {}
    for s in ("Right", "Left"):
        arm_len[s] = ((wpos(dst, f"mixamorig:{s}ForeArm") - wpos(dst, f"mixamorig:{s}Arm")).length
                      + (wpos(dst, f"mixamorig:{s}Hand") - wpos(dst, f"mixamorig:{s}ForeArm")).length)
    r0, t0 = blade(dst)
    B["blade_len"] = (t0 - r0).length
    print("BASE hips %s lean %.1f face %.1f pitchL %.1f pitchR %.1f toeL %.3f toeR %.3f arm %.3f blade %.3f" % (
        tuple(round(c, 3) for c in B["hips"]), B["lean"], B["face"], B["pitch"]["Left"], B["pitch"]["Right"],
        B["toe"]["Left"], B["toe"]["Right"], arm_len["Right"], B["blade_len"]))

    act = bpy.data.actions.new("Dash")
    act.use_fake_user = True
    dst.animation_data_create(); dst.animation_data.action = None   # ключи — после обоих проходов
    prev = {}
    rows = []

    def chest_q():
        return bone_world_rot(dst, dst.pose.bones["mixamorig:Spine2"]).to_quaternion()

    def clear_arm(side):
        """Кисть и предплечье — вон из бедра той же стороны: поворот руки в плече
        шагами CLEAR_STEP от оси бедра к вошедшим вершинам. Возвращает поворот
        в осях груди (для сглаживания по тикам) и замер до/после."""
        parts = [side + "Hand", side + "ForeArm"]
        thigh = side + "Thigh"
        n, deep, worst, cen = body.contact(parts, thigh)
        before = (n, deep, round(worst, 4))
        total = Quaternion()
        steps = 0
        while (n or deep) and (steps + 1) * CLEAR_STEP <= CLEAR_MAX:
            sh = wpos(dst, f"mixamorig:{side}Arm")
            a = wpos(dst, f"mixamorig:{side}UpLeg"); b = wpos(dst, f"mixamorig:{side}Leg")
            ab = b - a
            t = max(0.0, min(1.0, (cen - a).dot(ab) / ab.length_squared))
            push = cen - (a + ab * t)
            push = push - push.dot(ab.normalized()) * ab.normalized()
            axis = (cen - sh).cross(push)
            if axis.length < 1e-8: break
            q = Quaternion(axis.normalized(), math.radians(CLEAR_STEP))
            rotate_world(dst, dst.pose.bones[f"mixamorig:{side}Arm"], q, sh)
            total = q @ total
            steps += 1
            n, deep, worst, cen = body.contact(parts, thigh)
        c = chest_q()
        return c.inverted() @ total @ c, dict(before=before, after=(n, deep, round(worst, 4)), steps=steps)

    def pose(tick, arm_fix=None):
        reset()
        hips = dst.pose.bones["mixamorig:Hips"]
        # 1. таз: XY стойки, присед по Z, наклон таза вперёд
        translate_world(dst, hips, Vector((0, 0, table(HIPS_DZ, tick) * K)))
        rotate_world(dst, hips, Quaternion(X, math.radians(table(PELVIS, tick))), wpos(dst, "mixamorig:Hips"))
        # 2. наклон корпуса позвоночником (.4/.3/.3), несколько проходов
        target_lean = table(LEAN, tick)
        for _ in range(5):
            err = target_lean - sagittal_lean(dst)
            if abs(err) < .15: break
            for name, wgt in (("mixamorig:Spine", .4), ("mixamorig:Spine1", .3), ("mixamorig:Spine2", .3)):
                pb = dst.pose.bones[name]
                rotate_world(dst, pb, Quaternion(X, math.radians(err * wgt)), wpos(dst, name))
        # 3. ноги: лодыжка к точке, стопа — наклон носка
        for side, tab in (("Left", FOOT_L), ("Right", FOOT_R)):
            dy, dz, dp = table(tab, tick)
            target = B["ankle"][side] + Vector((0, dy * K, dz * K))
            for _ in range(3):
                two_bone(dst, [f"mixamorig:{side}UpLeg", f"mixamorig:{side}Leg", f"mixamorig:{side}Foot"], target, keep_end=True)
                cur, horiz = foot_pitch(dst, side)
                want = B["pitch"][side] + dp
                axis = UP.cross(horiz)
                if axis.length > 1e-6:
                    rotate_world(dst, dst.pose.bones[f"mixamorig:{side}Foot"], Quaternion(axis.normalized(), math.radians(want - cur)),
                                 wpos(dst, f"mixamorig:{side}Foot"))
                # Пятка вверх поворотом вокруг лодыжки опускает носок под землю —
                # лодыжка поднимается ровно на столько, чтобы носок остался на земле.
                low = min(wpos(dst, f"mixamorig:{side}ToeBase").z, wpos(dst, f"mixamorig:{side}Toe_End").z)
                if low >= toe_floor - 1e-4: break
                target = target + Vector((0, 0, toe_floor - low))
        # 4. руки от плеча; правая — с клинком
        w_arm = table(W_ARMS, tick)
        for side, tab, pole in (("Right", ARM_R_DIR, POLE_R), ("Left", ARM_L_DIR, POLE_L)):
            if w_arm < 1e-3: continue
            sh = wpos(dst, f"mixamorig:{side}Arm")
            ik = sh + Vector(table(tab, tick)).normalized() * ARM_REACH * arm_len[side]
            hand = wpos(dst, f"mixamorig:{side}Hand")
            elbow = wpos(dst, f"mixamorig:{side}ForeArm")
            cur_pole = (elbow - sh) - (elbow - sh).dot((hand - sh).normalized()) * (hand - sh).normalized()
            hint = slerp_dir(cur_pole if cur_pole.length > 1e-6 else pole, pole, w_arm)
            two_bone(dst, [f"mixamorig:{side}Arm", f"mixamorig:{side}ForeArm", f"mixamorig:{side}Hand"],
                     hand.lerp(ik, w_arm), pole_hint=hint)
        w_bl = table(W_BLADE, tick)
        wrist = 0.0; twist = (0, 0)
        if w_bl > 1e-3:
            # Клинок назад: кисть стойки держит саблю большим пальцем вперёд-вверх, а
            # рука ушла назад-вниз — клинок смотрел бы в землю перед ногой. Разворот:
            # (1) поворот вокруг оси предплечья, пока клинок не окажется в одной
            # плоскости с предплечьем и целью — 60% плечом (вся рука вокруг линии
            # плечо–кисть, кисть на месте), 40% пронацией предплечья; (2) остаток —
            # чистый изгиб кисти в этой плоскости, не больше WRIST_MAX.
            # Углы считаются к полной цели и умножаются на вес — разворот и возврат
            # идут плавно, без перескока азимута на полпути.
            want = Vector(table(BLADE_DIR, tick)).normalized()

            def azimuth(v, axis):
                return (v - v.dot(axis) * axis).normalized()

            def signed(a, b, axis):
                return math.degrees(math.atan2(a.cross(b).dot(axis), a.dot(b)))

            # Поиск на полной цели: s — вращение всей руки вокруг линии плечо–кисть
            # (кисть на месте), tw — пронация предплечья, остаток — изгиб кисти.
            # Всё жёсткое, считается без пересчёта сцены; применяется × вес.
            sh = wpos(dst, "mixamorig:RightArm"); el = wpos(dst, "mixamorig:RightForeArm")
            wr = wpos(dst, "mixamorig:RightHand")
            wv = (wr - sh).normalized(); f0 = (wr - el).normalized()
            r, t = blade(dst); d0 = (t - r).normalized()
            best = None
            for sv in range(-150, 151, 3):
                qs = Quaternion(wv, math.radians(sv))
                fs = qs @ f0; ds = qs @ d0
                for tv in range(-int(FOREARM_MAX), int(FOREARM_MAX) + 1, 5):
                    d = Quaternion(fs, math.radians(tv)) @ ds
                    res = math.degrees(d.angle(want))
                    cost = 3 * max(0.0, res - WRIST_MAX) + .2 * res + .02 * abs(sv) + .05 * abs(tv)
                    if best is None or cost < best[0]: best = (cost, sv, tv)
            _, sv, tv = best
            rotate_world(dst, dst.pose.bones["mixamorig:RightArm"], Quaternion(wv, math.radians(sv * w_bl)), sh)
            el = wpos(dst, "mixamorig:RightForeArm"); wr = wpos(dst, "mixamorig:RightHand")
            f = (wr - el).normalized()
            rotate_world(dst, dst.pose.bones["mixamorig:RightForeArm"], Quaternion(f, math.radians(tv * w_bl)), el)
            r, t = blade(dst); d0 = (t - r).normalized()
            ang = math.degrees(d0.angle(want))
            n = d0.cross(want)
            if n.length > 1e-8 and ang > 1e-3:
                wrist = min(ang, WRIST_MAX) * w_bl
                rotate_world(dst, dst.pose.bones["mixamorig:RightHand"], Quaternion(n.normalized(), math.radians(wrist)),
                             wpos(dst, "mixamorig:RightHand"))
            twist = (round(sv * w_bl, 1), round(tv * w_bl, 1))
        # 4б. левая рука вон из переднего бедра: сглаженная поправка прохода 1 + остаток.
        # Тик 12 — стойка серии, не трогается (там пересечений нет).
        clear = None
        if tick < FRAMES:
            if arm_fix is not None and arm_fix.angle > 1e-6:
                c = chest_q()
                rotate_world(dst, dst.pose.bones["mixamorig:LeftArm"], c @ arm_fix @ c.inverted(),
                             wpos(dst, "mixamorig:LeftArm"))
            clear = clear_arm("Left")
        # 5. шея и голова (v2): следуют за грудью — их повороты стойки относительно груди
        # не меняются, поэтому relP = стойке. Лицо поворачивается только вбок, вокруг оси
        # «вверх груди» (relP от этого не меняется), к направлению рывка (−Y) с весом W_HEAD.
        w_head = table(W_HEAD, tick)
        yaw0 = head_m()["faceYaw"]
        yaw_fix = 0.0
        if w_head > 1e-3:
            goal = yaw0 * (1 - w_head)
            for _ in range(12):
                err = goal - head_m()["faceYaw"]
                err = max(-HEAD_YAW_MAX - yaw_fix, min(HEAD_YAW_MAX - yaw_fix, err))
                if abs(err) < .05: break
                cu = hframe("Spine2")[1]
                for name in ("mixamorig:Neck", "mixamorig:Head"):
                    rotate_world(dst, dst.pose.bones[name], Quaternion(cu, math.radians(err * .5)), wpos(dst, name))
                yaw_fix += err
        return wrist, twist, clear, (round(yaw0, 2), round(yaw_fix, 2))

    # Проход 1: поправки руки по тикам; сглаживание: тик берёт наибольшую из своей
    # и долей CLEAR_SPREAD соседей — отвод нарастает и спадает без рывка.
    raw = {}
    for tick in range(FRAMES + 1):
        _, _, clear, _ = pose(tick)
        raw[tick] = clear[0] if clear else Quaternion()
        print("проход 1 t%02d отвод %.1f° %s" % (tick, math.degrees(raw[tick].angle), clear[1] if clear else ""))
    arm_fix = {}
    for t in range(FRAMES + 1):
        cands = [raw[t]] + [Quaternion().slerp(raw[n], CLEAR_SPREAD) for n in (t - 1, t + 1) if 0 <= n <= FRAMES]
        arm_fix[t] = max(cands, key=lambda q: q.angle) if t < FRAMES else Quaternion()

    # Проход 2: позы с поправками; снимок поз — ключи ставятся после (сетка
    # перечитывается на каждом тике, анимация к этому моменту не подключена).
    snaps = {}
    for tick in range(FRAMES + 1):
        wrist, twist, clear, yaw = pose(tick, arm_fix[tick])
        snaps[tick] = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in dst.pose.bones}
        vs = body.verts()
        hm = head_m()
        # отчёт
        h = wpos(dst, "mixamorig:Hips")
        r, t = blade(dst)
        row = dict(tick=tick, hips=[round(c, 4) for c in h], lean=round(sagittal_lean(dst), 1),
                   face_pitch=round(face_pitch()[0], 1), wrist=round(wrist, 1), swivel_twist=twist,
                   blade_tip_z=round(t.z, 3), head=hm, head_yaw_fix=yaw,
                   arm_clear=dict(fix_deg=round(math.degrees(arm_fix[tick].angle), 1),
                                  extra_steps=clear[1]["steps"] if clear else 0),
                   contact={s: list(body.contact([s + "Hand", s + "ForeArm"], s + "Thigh", vs)[:3]) for s in ("Left", "Right")},
                   upper_arm_torso={s: len(body.overlap(vs, s + "UpperArm", "torso")) for s in ("Left", "Right")})
        for s in ("Left", "Right"):
            a = wpos(dst, f"mixamorig:{s}Foot")
            row[s] = dict(ankle=[round(c, 3) for c in a], pitch=round(foot_pitch(dst, s)[0], 1),
                          toe_z=round(min(wpos(dst, f"mixamorig:{s}ToeBase").z, wpos(dst, f"mixamorig:{s}Toe_End").z), 4),
                          hand=[round(c, 3) for c in wpos(dst, f"mixamorig:{s}Hand")])
        rows.append(row)
        print("t%02d hips z %.3f xy %.4f,%.4f lean %5.1f face %5.1f | L %s p%5.1f toe %.3f | R %s p%5.1f toe %.3f | RH %s LH %s tip z %.3f wrist %.0f sw/tw %s" % (
            tick, h.z, h.x, h.y, row["lean"], row["face_pitch"], row["Left"]["ankle"], row["Left"]["pitch"], row["Left"]["toe_z"],
            row["Right"]["ankle"], row["Right"]["pitch"], row["Right"]["toe_z"], row["Right"]["hand"], row["Left"]["hand"], t.z, wrist, twist))
        print("    голова relP %.1f (стойка %.1f) relY %.1f neckP %.1f hOnN %.1f chestP %.1f faceP %.1f faceYaw %.1f fix %s | рука-бедро L %s R %s | плечо-корпус %s | отвод %.1f°+%d шагов" % (
            hm["relP"], B["head"]["relP"], hm["relY"], hm["neckP"], hm["headOnNeckP"], hm["chestP"], hm["faceP"], hm["faceYaw"], yaw,
            row["contact"]["Left"], row["contact"]["Right"], row["upper_arm_torso"], row["arm_clear"]["fix_deg"], row["arm_clear"]["extra_steps"]))

    # ключи — из снимков поз прохода 2
    dst.animation_data.action = act
    for tick in range(FRAMES + 1):
        for pb in dst.pose.bones:
            loc, rot = snaps[tick][pb.name]
            rot = rot.copy()
            q = prev.get(pb.name)
            if q is not None and q.dot(rot) < 0: rot.negate()
            prev[pb.name] = rot.copy()
            pb.location = loc; pb.rotation_quaternion = rot
            pb.keyframe_insert("location", frame=tick, group=pb.name)
            pb.keyframe_insert("rotation_quaternion", frame=tick, group=pb.name)

    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'

    bpy.ops.object.select_all(action='DESELECT')
    dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = export_scale       # риг выгружается в своём масштабе (.01), как Roll/Bind v6
    bpy.context.view_layer.update()
    scene.frame_start, scene.frame_end = 0, FRAMES
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Pelag_AN_Dash.fbx"), use_selection=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.animation_data.action = None
    for pb in dst.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    # Привязка — покой v6. Покой этого рига СТОИТ при повороте объекта +90° по X
    # (кости вдоль Y арматуры), поэтому поворот объекта сохраняется — как у
    # Pelag_AN_RollBind/MobilityBind. Риг тейка сабли наоборот лежит в покое, и там
    # поворот обнуляли (память razlom-mixamo-take-bind-upright); здесь «стоя» = как есть.
    # Проверка — d_render_v6.py печатает долю смещения таза (порог Unity .4).
    hips_w = dst.matrix_world @ dst.data.bones["mixamorig:Hips"].head_local
    head_w = dst.matrix_world @ dst.data.bones["mixamorig:Head"].head_local
    print("bind hips", tuple(round(c, 4) for c in hips_w), "head", tuple(round(c, 4) for c in head_w))
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Pelag_AN_DashBind.fbx"), use_selection=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=False, armature_nodetype='NULL')
    dst.animation_data.action = act
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "Pelag_Dash_Work.blend"))
    # Всплеск: где передняя стопа встаёт относительно начала модели (корня героя), метры v6.
    lf = next(r for r in rows if r["tick"] == SPLASH_TICK)["Left"]["ankle"]
    hx, hy = 0.0, 0.0
    timing = dict(
        clip="Pelag_AN_Dash", bind="Pelag_AN_DashBind", fps=30, frames=FRAMES + 1, length_s=round(FRAMES / 30, 4),
        frame_is_tick=True, root_motion="нет: таз по XY постоянен, по Z присед (корень везёт Sim)",
        phases=dict(anticipation=[0, 1], burst=[1, 6], recovery=[6, 12]),
        contacts=CONTACT, front_foot="Left", front_foot_plant_tick=6,
        arrival_splash=dict(tick=SPLASH_TICK, bone="mixamorig:LeftFoot",
                            ahead_of_root_m=round(-(lf[1] - hy), 3), left_of_root_m=round(lf[0] - hx, 3),
                            note="от начала модели (корня героя), метры v6 в масштабе 1; в игре умножить на масштаб тела. Таз стоит на 4,4 см сзади и 1,4 см левее корня весь клип (как в стойке серии)"),
        sim_assumption="Sim везёт ~4 м за тики 0–6 и стоит с 6-го; если рывок кончается не на 6-м тике — сдвинуть постановку (FOOT_L, CONTACT)",
        lean_deg={str(r["tick"]): r["lean"] for r in rows},
        face_below_horizon_deg={str(r["tick"]): r["face_pitch"] for r in rows},
        head_rule=dict(
            rule="голова следует за грудью: relP (угол лица над плоскостью груди, headlib.py) не выше стойки на каждом тике; "
                 "шея и голова держат повороты стойки относительно груди, разрешён только поворот вбок вокруг оси груди к рывку",
            stance=B["head"],
            relP={str(r["tick"]): r["head"]["relP"] for r in rows},
            max_relP_over_stance=round(max(r["head"]["relP"] for r in rows) - B["head"]["relP"], 2),
            face_yaw_from_dash_deg={str(r["tick"]): r["head"]["faceYaw"] for r in rows},
            yaw_fix_deg={str(r["tick"]): r["head_yaw_fix"][1] for r in rows}),
        left_arm_clearance=dict(
            rule="кисть и предплечье не входят в бедро: пересечение треугольников сетки v6 и вершины глубже %.0f мм (поиск %.0f см)" % (
                CLEAR_DEEP * 1000, CLEAR_SEARCH * 100),
            shoulder_fix_deg={str(r["tick"]): r["arm_clear"]["fix_deg"] for r in rows},
            contact_tris_deep_worst={str(r["tick"]): r["contact"] for r in rows},
            upper_arm_torso_tris={str(r["tick"]): r["upper_arm_torso"] for r in rows}),
        hips_drop_m={str(r["tick"]): round(rows[-1]["hips"][2] - r["hips"][2], 3) for r in rows},
        poses=SOURCES,
        base=dict(source="Pelag_MX_SaberCombo кадр 2 (= Pelag_AN_Sabre1 тик 0)", lean=round(B["lean"], 1),
                  face_pitch=round(B["face"], 1), K_leg_ratio=round(K, 3)),
        rows=rows)
    json.dump(timing, open(os.path.join(out, "timing.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("authored Dash", FRAMES + 1, "frames")


build()
