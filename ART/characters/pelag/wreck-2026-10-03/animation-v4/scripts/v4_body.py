"""Крушение v4: тело из сабли под пределы задачи — наклон назад, скрутка, цель кулаков, стопы на земле без скольжения."""
import bpy, math
from mathutils import Vector, Quaternion, Matrix
from s_lib import rotate_world, two_bone, translate_world
from wk_rig import M, SIDES
import wk_grip
from b_common import blade
import v3s4_patch
from v3s4_patch import chest_lean, _chest_left, _wrap

UP = Vector((0, 0, 1))
SPINE_W = (("Spine", .35), ("Spine1", .35), ("Spine2", .30))
LEAN_MIN, TWIST_MAX = 1.0, 38.0
REACH, GAP = 0.93, 0.15
HEAD_CLEAR = 0.32
TORSO_CLEAR = 0.40          # кулак от оси корпуса (таз–шея), м: корпус 0,18 + предплечье


def fix_body(rig):
    """Наклон назад → 1° вперёд (по взгляду груди); скрутка груди к тазу → ≤ 44°. Поворот позвоночника, ноги не трогаются."""
    d = rig.dst
    for _ in range(8):
        e = LEAN_MIN - chest_lean(d)
        if e <= 0.05: break
        Lx, _f = _chest_left(rig)
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(Lx, math.radians(e * w)), rig.P(n))
    for _ in range(6):
        tw = _wrap(rig.chest_yaw() - rig.pelvis_yaw())
        if abs(tw) <= TWIST_MAX: break
        e = math.copysign(abs(tw) - TWIST_MAX + .2, tw)
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(UP, math.radians(-e * w)), rig.P(n))


def chest_frame(rig):
    o = rig.P("Spine2"); r = (rig.P("RightArm") - rig.P("LeftArm")).normalized()
    u = (rig.P("Neck") - o); u = (u - u.dot(r) * r).normalized(); f = u.cross(r).normalized()
    return o, r, u, f


def _push_axis(p, a, b, clear):
    ab = b - a; u = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    c = a + ab * u; v = p - c; v.z = 0.0
    if v.length >= clear: return p
    if v.length < 1e-4: v = Vector((0, -1, 0))
    return c + v.normalized() * clear + Vector((0, 0, p.z - c.z))


def grip_target(rig):
    """Правый кулак сабли (Q) и ось клинка (D) → ближайшая точка, до которой достают обе руки и которая не в корпусе/бёдрах."""
    armlen = {s: rig.ARM[s]["L1"] + rig.ARM[s]["L2"] for s in SIDES}
    rf = wk_grip.fist_center(rig, "Right"); r0, tip = blade(rig.dst); D = (tip - r0).normalized()
    Q = rf.copy()
    hips, neck = rig.P("Hips"), rig.P("Neck")
    LS, RS = rig.P("LeftArm"), rig.P("RightArm")
    for _ in range(40):
        q0 = Q.copy()
        v = Q - RS
        if v.length > REACH * armlen["Right"]: Q = RS + v.normalized() * REACH * armlen["Right"]
        lf = Q - D * GAP; v = lf - LS
        if v.length > REACH * armlen["Left"]: Q = LS + v.normalized() * REACH * armlen["Left"] + D * GAP
        for off in (0.0, GAP):
            p = Q - D * off
            p2 = _push_axis(p, hips, neck, TORSO_CLEAR)
            Q = Q + (p2 - p)
        hc = rig.P("Head") + (rig.P("Head") - neck).normalized() * 0.08
        for off in (0.0, GAP):                                     # голова: кулаки и рукоять не в лице
            p = Q - D * off; v = p - hc
            if v.length < HEAD_CLEAR: Q = Q + (v.normalized() * HEAD_CLEAR - v)
        for s in SIDES:      # бёдра: кулак не ниже паха рядом с ногой
            a, b = rig.P(s + "UpLeg"), rig.P(s + "Leg")
            for off in (0.0, GAP):
                p = Q - D * off
                p2 = _push_axis(p, a, b, 0.20)
                if p2 is not p and p.z < hips.z + 0.05: Q = Q + (p2 - p)
        if (Q - q0).length < 1e-4: break
    return Q, D, (Q - rf).length


def smooth_targets(Qs, Ds, passes=2, keep=()):
    """1-2-1 по кадрам клипа (концы и кадры keep не трогаются)."""
    for _ in range(passes):
        Qn, Dn = [q.copy() for q in Qs], [d.copy() for d in Ds]
        for i in range(1, len(Qs) - 1):
            if i in keep: continue
            Qn[i] = (Qs[i - 1] + 2 * Qs[i] + Qs[i + 1]) / 4
            Dn[i] = (Ds[i - 1] + 2 * Ds[i] + Ds[i + 1]).normalized()
        Qs, Ds = Qn, Dn
    return Qs, Ds


def _smooth01(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


# Шаги тейка (кадры клипа, где стопа в воздухе); вне окон стопа стоит на земле. Замер v4_probe3 (06.10).
STEPS = {
    "Swing1": {"Left": [(1, 4)], "Right": [(4, 7)]},
    "Swing2": {"Left": [(6, 9)], "Right": [(1, 4)]},
    "Lunge": {"Left": [(1, 3), (11, 17), (17, 23)], "Right": [(3, 8), (15, 19)]},
}


def lock_feet(rig, mesh, snaps, name, end, anchors=None, ball_z=None, kscale=1):
    """Стопа вне окна шага стоит: подушечка (ToeBase) — в точке начала пролёта на высоте подушечки стойки,
    лодыжка — IK ноги, поворот стопы из клипа (пятка и разворот носка остаются). В окне шага — дуга от точки к
    точке с подъёмом (0,05 м + 0,1 длины шага). Всё в мире (с ходом корня выпада). anchors — точки опоры с конца
    прошлого удара (продолжение пролёта). Возвращает точки опоры в кадре end (для следующего удара)."""
    n = len(snaps)
    out_anchor = {}
    for s in SIDES:
        wins = [(a * kscale, b * kscale) for a, b in STEPS.get(name, {}).get(s, [])]
        free = lambda f: any(a < f < b for a, b in wins)
        ball = []
        for f in range(n):
            rig.restore(snaps[f]); ball.append(rig.P(s + "ToeBase").copy())
        target = [None] * n; frot = [None] * n
        anchor = (anchors or {}).get(s); arot = (anchors or {}).get(s + "_rot")
        for f in range(n):
            if free(f): anchor = arot = None; continue
            if anchor is None:
                anchor = Vector((ball[f].x, ball[f].y, ball_z[s]))
                rig.restore(snaps[f]); arot = (rig.dst.matrix_world @ rig.dst.pose.bones[M(s + "Foot")].matrix).to_3x3().normalized()
            target[f] = anchor; frot[f] = arot           # опорная стопа не крутится на земле (иначе пятка скользит)
        for a, b in wins:
            if b >= n or target[a] is None or target[b] is None: continue
            p0, p1 = target[a], target[b]; lift = 0.06 + 0.1 * (p1 - p0).length
            for f in range(a + 1, b):
                x = (f - a) / (b - a); u = _smooth01(min(1.0, x / 0.8))    # дошла по земле к 80% окна, дальше — вниз
                target[f] = p0.lerp(p1, u) + Vector((0, 0, lift * math.sin(math.pi * x)))
                if frot[a] is not None and frot[b] is not None:
                    frot[f] = frot[a].to_quaternion().slerp(frot[b].to_quaternion(), u).to_matrix()
        out_anchor[s] = target[end] if target[end] is not None else None
        out_anchor[s + "_rot"] = frot[end]
        for f in range(n):
            if target[f] is None: continue
            rig.restore(snaps[f])
            ankle = rig.P(s + "Foot") + (target[f] - rig.P(s + "ToeBase"))
            if frot[f] is not None:                      # поворот стопы опоры — как в начале пролёта
                fb = rig.dst.pose.bones[M(s + "Foot")]
                fw = (rig.dst.matrix_world @ fb.matrix); hd = fw.translation.copy()
                fb.matrix = rig.dst.matrix_world.inverted() @ (Matrix.Translation(hd) @ frot[f].to_4x4())
                bpy.context.view_layer.update()
                ankle = rig.P(s + "Foot") + (target[f] - rig.P(s + "ToeBase"))
            for _ in range(2):
                two_bone(rig.dst, [M(s + "UpLeg"), M(s + "Leg"), M(s + "Foot")], ankle, keep_end=True)
                ankle = ankle + (target[f] - rig.P(s + "ToeBase"))
            snaps[f] = dict(rig.snapshot(), **({"_root": snaps[f]["_root"]} if "_root" in snaps[f] else {}))
    for f in range(n):              # под землю не уходит: подъём лодыжки на глубину самой низкой вершины стопы
        for _ in range(2):
            vs = mesh.at(snaps[f]); low = mesh.feet_low(vs)
            moved = False
            for s in SIDES:
                if low[s] < 0.0:
                    ankle = rig.P(s + "Foot") + Vector((0, 0, -low[s] + 0.002))
                    two_bone(rig.dst, [M(s + "UpLeg"), M(s + "Leg"), M(s + "Foot")], ankle, keep_end=True); moved = True
            if not moved: break
            snaps[f] = dict(rig.snapshot(), **({"_root": snaps[f]["_root"]} if "_root" in snaps[f] else {}))
    return out_anchor
