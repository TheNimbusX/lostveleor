"""Крушение v3, формы (06.10): общие помощники «хирургии» позы поверх принятого клипа базы (Blender).

Поза базы берётся из рабочего .blend клипа (ключи на тик), затем меняются только таз (вниз), ноги (стопы — жёстким
переносом стопы базы: сдвиг по земле, поворот вокруг подушечки), голова (кивок к груди); кисти возвращаются ТОЧНО
в мировые матрицы кистей базы (двухзвенник плечо–предплечье, локоть в плоскости базы), пальцы — локальные базы.
Так гнездо хвата (кисть × grip_socket) совпадает с базой, и запечка якоря та же.
Оси корня: f — вперёд (−Y Blender), l — влево (+X), u — вверх.
"""
import math, os, bpy
from mathutils import Vector, Quaternion, Matrix
from s_lib import rotate_world, translate_world, two_bone, foot_pitch
from wk_rig import M, fl, yaw_of, SIDES

UP = Vector((0, 0, 1))
V3 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))


def blend_of(clip, where=V3):
    return os.path.join(where, "Pelag_Wreck2_%s_v3_Work.blend" % clip.replace("Pelag_AN_Wreck2_", ""))


def action_snaps(rig, clip, n, where=V3):
    """Снимки позы клипа (кадры 0..n) из его рабочего .blend — как v3s4_author.action_snapshot."""
    with bpy.data.libraries.load(blend_of(clip, where), link=False) as (src, dst):
        dst.actions = [clip]
    act = bpy.data.actions[clip]
    vals = {}
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    vals[(fc.data_path, fc.array_index)] = fc
    base = {k: (v[0].copy(), v[1].copy()) for k, v in rig.snapshot().items()}
    out = []
    for f in range(n + 1):
        snap = {}
        for name, (l0, q0) in base.items():
            lp, rp = 'pose.bones["%s"].location' % name, 'pose.bones["%s"].rotation_quaternion' % name
            loc = Vector([vals[(lp, i)].evaluate(f) if (lp, i) in vals else l0[i] for i in range(3)])
            q = Quaternion([vals[(rp, i)].evaluate(f) if (rp, i) in vals else q0[i] for i in range(4)]).normalized()
            snap[name] = (loc, q)
        out.append(snap)
    bpy.data.actions.remove(act)
    return out


def wmat(rig, n):
    return rig.dst.matrix_world @ rig.dst.pose.bones[M(n)].matrix


def set_wmat(rig, n, m):
    pb = rig.dst.pose.bones[M(n)]
    pb.matrix = rig.dst.matrix_world.inverted() @ m
    bpy.context.view_layer.update()


def hip_ratio(rig):
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    return (rig.P("Hips") - rig.rest[M("Hips")].translation).length / L


def drop_for_ratio(rig, ratio):
    """Насколько можно опустить таз (м, вниз), чтобы смещение таза к покою было ≤ ratio длины корпуса."""
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    v = rig.P("Hips") - rig.rest[M("Hips")].translation
    lo, hi = 0.0, 0.3
    for _ in range(40):
        mid = (lo + hi) / 2
        if (v - Vector((0, 0, mid))).length / L <= ratio: lo = mid
        else: hi = mid
    return lo


def ball_point(rig, s):
    """Подушечка стопы (ось на носке, как wk_clips.on_toe): ToeBase + 0,04 м вперёд по стопе."""
    _, horiz = foot_pitch(rig.dst, s)
    return rig.P(s + "ToeBase") + horiz * 0.04


def lateral_axis(rig, s):
    _, horiz = foot_pitch(rig.dst, s)
    return UP.cross(horiz).normalized()


def turn_about(m, pivot, q):
    """Матрица m (мир) повёрнута кватернионом q вокруг точки pivot."""
    return Matrix.Translation(pivot) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pivot) @ m


def foot_frame(rig, s):
    """Состояние стопы: мировая матрица Foot, подушечка, ось поперёк стопы, наклон (° от стойки, + пятка вверх), рысканье."""
    p, horiz = foot_pitch(rig.dst, s)
    return dict(F=wmat(rig, s + "Foot").copy(), ball=ball_point(rig, s).copy(), lat=UP.cross(horiz).normalized(),
                pitch=p - rig.B["pitch"][s], yaw=yaw_of(horiz), toe=min(rig.P(s + "ToeBase").z, rig.P(s + "Toe_End").z))


def leg_len(rig, s):
    a, b, c = rig.P(s + "UpLeg"), rig.P(s + "Leg"), rig.P(s + "Foot")
    return (b - a).length + (c - b).length


def knee_pole(rig, s, out_deg):
    """Плоскость колена базы, повёрнутая наружу на out_deg вокруг оси бедро–лодыжка."""
    a, k, c = rig.P(s + "UpLeg"), rig.P(s + "Leg"), rig.P(s + "Foot")
    ax = (c - a).normalized()
    pole = (k - a) - (k - a).dot(ax) * ax
    if abs(out_deg) < 1e-6 or pole.length < 1e-6: return pole
    side = (rig.P("LeftUpLeg") - rig.P("RightUpLeg")); side.z = 0; side.normalize()
    sgn = 1.0 if s == "Left" else -1.0
    best = None
    for d in (out_deg, -out_deg):
        v = Quaternion(ax, math.radians(d)) @ pole
        score = v.normalized().dot(side) * sgn
        if best is None or score > best[0]: best = (score, v)
    return best[1]


def heel(F, ball, lat, deg):
    """Стопа F, повёрнутая вокруг подушечки: + пятка вверх (лодыжка вверх и вперёд к носку)."""
    return turn_about(F, ball, Quaternion(lat, math.radians(deg)))


LOC = {}


def foot_local(rig, body):
    """Точки подошвы в осях кости Foot по стойке (стопа плашмя на земле): пятка и носок — крайние вершины подошвы сетки,
    подушечка — под ToeBase + 0,04; у каждой — высота в стойке h0 (на земле)."""
    vs = body.verts()
    for s in SIDES:
        F = wmat(rig, s + "Foot"); _, horiz = foot_pitch(rig.dst, s)
        ids = [i for i, p in enumerate(body.vpart) if p == s + "Foot"]
        z0 = min(vs[i].z for i in ids)
        sole = [vs[i] for i in ids if vs[i].z < z0 + 0.012]
        hv = min(sole, key=lambda v: v.dot(horiz)); tv = max(sole, key=lambda v: v.dot(horiz))
        b = ball_point(rig, s); bs = Vector((b.x, b.y, z0))
        out_ = max(sole, key=lambda v: v.dot(UP.cross(horiz))); in_ = min(sole, key=lambda v: v.dot(UP.cross(horiz)))
        Fi = F.inverted()
        LOC[s] = {k: (Fi @ v, v.z) for k, v in dict(heel=hv, tip=tv, ball=bs, edge1=out_, edge2=in_).items()}
    return LOC


def pts(F, s):
    return {k: F @ v for k, (v, h0) in LOC[s].items()}


def ground(F, s, floor=None, ball=None):
    """Стопа по вертикали: нижняя точка подошвы (над своей высотой в стойке) ложится на пол → (F, сдвиг м)."""
    d = min((F @ v).z - h0 for v, h0 in LOC[s].values())
    return Matrix.Translation(Vector((0, 0, -d))) @ F, -d


def place_foot(rig, s, F, ball, lat, knee_out=0.0, reach=0.997, max_turn=35.0, pivot="auto"):
    """Нога к мировой матрице стопы F. Не достаёт — стопа поворачивается вокруг подушечки (пятка вверх) или пятки (носок
    вверх; передняя прямая нога) — что даёт меньший угол; точка опоры не едет. Пальцы под полом сгибаются.
    → (поворот °, '+' — пятка вверх / '-' — носок вверх, промах лодыжки м)."""
    hip = rig.P(s + "UpLeg"); Ls = leg_len(rig, s)
    turn = 0.0
    if (F.translation - hip).length > reach * Ls:
        best = None
        P_ = pts(F, s); hz = {k: (F @ v).z - h0 for k, (v, h0) in LOC[s].items()}; low = min(hz.values())
        cands = []
        if hz["ball"] <= low + 0.005 and pivot != "heel": cands.append((1.0, ball))          # опора — подушечка: пятка вверх
        if hz["heel"] <= low + 0.005 and pivot != "ball": cands.append((-1.0, P_["heel"]))  # опора — пятка: носок вверх
        for sgn, piv in cands:
            if (turn_about(F, piv, Quaternion(lat, math.radians(sgn * max_turn))).translation - hip).length > reach * Ls: continue
            lo, hi = 0.0, max_turn
            for _ in range(30):
                mid = (lo + hi) / 2
                if (turn_about(F, piv, Quaternion(lat, math.radians(sgn * mid))).translation - hip).length > reach * Ls: lo = mid
                else: hi = mid
            if best is None or hi < best[0]: best = (hi, sgn, piv)
        if best is not None:
            turn = best[0] * best[1]
            F = turn_about(F, best[2], Quaternion(lat, math.radians(turn)))
    pole = knee_pole(rig, s, knee_out)
    miss = two_bone(rig.dst, [M(s + "UpLeg"), M(s + "Leg"), M(s + "Foot")], F.translation.copy(), pole_hint=pole)
    Fw = F.copy(); Fw.translation = rig.P(s + "Foot")
    set_wmat(rig, s + "Foot", Fw)
    toes_on_floor(rig, s)
    return turn, miss


def toes_on_floor(rig, s, floor=None):
    """Кончик пальцев ниже пола (пятка поднята) — пальцы сгибаются в ToeBase, ложатся на пол."""
    floor = rig.toe_floor if floor is None else floor
    tb, te = rig.P(s + "ToeBase"), rig.P(s + "Toe_End")
    if te.z >= floor - 1e-4: return 0.0
    v = te - tb; h = Vector((v.x, v.y, 0.0))
    if h.length < 1e-6: return 0.0
    ax = UP.cross(h.normalized())
    want = math.asin(max(-1.0, min(1.0, (floor - tb.z) / v.length)))
    cur = math.atan2(v.z, h.length)
    ang = want - cur
    rotate_world(rig.dst, rig.dst.pose.bones[M(s + "ToeBase")], Quaternion(ax, -ang), tb)
    if rig.P(s + "Toe_End").z < te.z: rotate_world(rig.dst, rig.dst.pose.bones[M(s + "ToeBase")], Quaternion(ax, 2 * ang), tb)
    return math.degrees(ang)


def place_arm(rig, s, H, pole):
    """Рука к мировой матрице кисти H (точно): плечо–предплечье двухзвенником, локоть в плоскости pole, кисть = H."""
    miss = two_bone(rig.dst, [M(s + "Arm"), M(s + "ForeArm"), M(s + "Hand")], H.translation.copy(), pole_hint=pole, keep_end=True)
    Hw = H.copy(); Hw.translation = rig.P(s + "Hand")
    set_wmat(rig, s + "Hand", Hw)
    return miss


def arm_pole(rig, s):
    a, e, w = rig.P(s + "Arm"), rig.P(s + "ForeArm"), rig.P(s + "Hand")
    ax = (w - a).normalized()
    return (e - a) - (e - a).dot(ax) * ax


def arm_reach(rig, s, target):
    a, e, w = rig.P(s + "Arm"), rig.P(s + "ForeArm"), rig.P(s + "Hand")
    return (target - a).length / ((e - a).length + (w - e).length)


def nod(rig, deg):
    """Кивок (+ подбородок вниз) вокруг поперечной оси груди: шея 40 %, голова 60 % (голова за грудью ±5° — wk_check relP)."""
    if abs(deg) < 1e-6: return
    cf, cu, cr = rig.hframe("Spine2")
    for n, w in (("Neck", .4), ("Head", .6)):
        rotate_world(rig.dst, rig.dst.pose.bones[M(n)], Quaternion(cr, math.radians(-deg * w)), rig.P(n))   # + вокруг cr — подбородок вверх


def root_vec(df, dl):
    return fl(df, dl, 0.0)
