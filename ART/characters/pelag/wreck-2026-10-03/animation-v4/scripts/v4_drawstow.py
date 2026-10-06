"""Крушение v4: снятие и уборка якоря — первые версии (06.10).

Draw (8 тиков, кадр 8 = Swing1 кадр 0): тело — Swing1 кадр 0 с доворотом груди вправо к плечу; правая кисть из стойки
за правое плечо к рукояти (хват в тик 3,25), рывок вверх-вперёд через плечо и вниз вправо — в позу Swing1 0; левая
присоединяется к рукояти в тики 5–8. Якорь снимается со спины физикой (цепь короткая, голова идёт через плечо).
Stow (8 тиков): тело — хвост Lunge 16–24 (возврат в стойку, шаги уже на земле); правая с рукоятью — вверх через
правое плечо (цепь сматывается, голову закидывает на спину), рукоять ложится за плечо в тик 3,5, кисть отпускает
и уходит в стойку; левая отпускает рукоять сразу и уходит в стойку к тику 3.
"""
import bpy, math
from mathutils import Vector, Quaternion
from s_lib import rotate_world
from wk_rig import M, SIDES
import wk_grip, wk_fingers, v3_arms
import v4_body as VB
import v4_lib as L4

UP = Vector((0, 0, 1))
ARMS = lambda s: [M(s + x) for x in ("Shoulder", "Arm", "ForeArm", "Hand")]


def _cr(pts, x):
    """Катмулл–Ром по ключам [(t, Vector)]."""
    ts = [t for t, _ in pts]
    i = max(0, min(len(pts) - 2, max(j for j in range(len(ts)) if ts[j] <= x) if x >= ts[0] else 0))
    t0, t1 = ts[i], ts[i + 1]; u = max(0.0, min(1.0, (x - t0) / (t1 - t0)))
    p0, p1, p2, p3 = pts[max(0, i - 1)][1], pts[i][1], pts[i + 1][1], pts[min(len(pts) - 1, i + 2)][1]
    return 0.5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3)


def _sm(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def _mix(a, b, w, names):
    out = dict(a)
    for n in names:
        (la, qa), (lb, qb) = a[n], b[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    return out


def _fingers(names):
    return [n for n in names if any(f in n for f in ("Thumb", "Index", "Middle", "Ring", "Pinky"))]


def _chest_twist(rig, deg):
    for n, w in VB.SPINE_W:
        rotate_world(rig.dst, rig.dst.pose.bones[M(n)], Quaternion(UP, math.radians(deg * w)), rig.P(n))


def _right(rig, target, D, pole, hint):
    v3_arms.CLAV.clear()
    sol, miss, ax = v3_arms.solve(rig, "Right", target, D, pole, hint or (0.0, 0.0, 0.0, 0.0), full=hint is None)
    return sol


def _up_pole(rig, t):
    """Локоть правой при снятии: вниз-наружу → назад → вверх-наружу (кисть заходит за плечо, локоть поднимается
    через «назад», без переворота), с тика 4,5 — к локтю хвата серии (наружу-вниз)."""
    o, r, u, f = VB.chest_frame(rig)
    th = math.pi * _sm((t - 1.0) / 2.25)
    a = (-u * math.cos(th) - f * math.sin(th)) * 0.8 + r * 0.6
    b = (r * 0.85 - u * 0.45 + f * 0.2)
    m = a.normalized().lerp(b.normalized(), _sm((t - 4.5) / 2.5)).normalized()
    return (-m.y, m.x, m.z)


def back_points(rig):
    o, r, u, f = VB.chest_frame(rig)
    return dict(out=o + r * 0.42 + f * 0.12 - u * 0.02, back=o + u * 0.38 - f * 0.16 + r * 0.22, up=o + u * 0.52 + f * 0.08 + r * 0.32, side=o + u * 0.08 + f * 0.36 + r * 0.42,
                Dback=(-u * 0.75 - f * 0.65).normalized())


def _pole_t(rig, v):
    m = v.normalized(); return (-m.y, m.x, m.z)


def _draw_body(rig, stance, s1, allarm, t):
    """Тело Swing1 0, руки стойки (кисти пустые), грудь доворачивает вправо к плечу и обратно."""
    base = _mix(s1[0], stance, 1.0, allarm)
    rig.restore(base)
    tw = 18.0 * (_sm(t / 2.5) - _sm((t - 4.0) / 4.0))
    if abs(tw) > 1e-3: _chest_twist(rig, tw)
    return dict(rig.snapshot(), _root=s1[0].get("_root"))


def draw(rig, stance, s1, hk, side_pole):
    """Ключевые позы правой руки решаются IK (локоть в своей стороне для каждой), между ними — повороты суставов
    (без перескока локтя через кисть). Кадр 8 = Swing1 0. Возвращает ключи (по полтика) и точки выхода цепи (мир)."""
    N = 8; keys = []; chain = []
    allarm = [n for n in stance if any(n.startswith(M(s + x)) for s in SIDES for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]
    RA = [n for n in allarm if n.startswith(M("Right"))]; LA = [n for n in allarm if n.startswith(M("Left"))]
    rig.restore(s1[0]); end_D = (wk_grip.fist_center(rig, "Right") - wk_grip.fist_center(rig, "Left")).normalized()
    KEYS = {}
    for t, where, pole in ((3.25, "back", "up"), (4.5, "up", "upout"), (6.25, "side", "down")):
        body = _draw_body(rig, stance, s1, allarm, t); rig.restore(body)
        o, r, u, f = VB.chest_frame(rig); bp = back_points(rig)
        P = {"up": u * 0.7 + f * 0.45 + r * 0.55, "upout": u * 0.3 + r * 0.9 + f * 0.2, "down": r * 0.85 - u * 0.45 + f * 0.2}[pole]
        D = {"back": bp["Dback"], "up": (bp["Dback"] + f * 0.6).normalized(), "side": (end_D + bp["Dback"] * 0.3).normalized()}[where]
        _right(rig, bp[where], D, _pole_t(rig, P), None)
        KEYS[t] = rig.snapshot()
    KEYS[0.0] = _draw_body(rig, stance, s1, allarm, 0.0); KEYS[8.0] = s1[0]
    kt = sorted(KEYS)
    for k in range(N * hk + 1):
        t = k / hk
        if k == N * hk:
            keys.append(dict(s1[0])); rig.restore(s1[0]); chain.append(wk_grip.fist_center(rig, "Right") + end_D * 0.05); break
        body = _draw_body(rig, stance, s1, allarm, t)
        i = max(j for j in range(len(kt) - 1) if kt[j] <= t); a_, b_ = kt[i], kt[i + 1]
        w = (t - a_) / (b_ - a_)
        snap = _mix(body, _mix(KEYS[a_], KEYS[b_], w, RA), 1.0, RA)
        wl = _sm((t - 5.0) / 3.0)                                     # левая: стойка → поза Swing1 0
        snap = _mix(snap, s1[0], wl, LA)
        rig.restore(snap)
        wk_fingers.apply(rig, wl, _sm((t - 2.5) / 0.75))
        keys.append(dict(rig.snapshot(), _root=s1[0].get("_root")))
        chain.append(wk_grip.fist_center(rig, "Right") + wk_grip.fist_axis(rig, "Right") * 0.05)
    return keys, chain


def stow(rig, stance, lunge, hk, end_tick, side_pole):
    """Тело — Lunge end..end+8. Правая: ключи «над правым плечом» (2) и «рукоять за плечом» (3,5) решаются IK, между —
    повороты суставов; после 3,5 кисть отпускает и уходит в стойку к 8. Левая отпускает сразу, в стойку к 3."""
    N = 8; keys = []; chain = []
    allarm = [n for n in stance if any(n.startswith(M(s + x)) for s in SIDES for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]
    RA = [n for n in allarm if n.startswith(M("Right"))]; LA = [n for n in allarm if n.startswith(M("Left"))]
    body0 = lunge[end_tick * hk]; rig.restore(body0)
    D0 = (wk_grip.fist_center(rig, "Right") - wk_grip.fist_center(rig, "Left")).normalized()
    KEYS = {0.0: body0}
    for t, where, pole in ((2.0, "up", "upout"), (3.5, "back", "up")):
        body = lunge[min(len(lunge) - 1, end_tick * hk + int(t * hk))]; rig.restore(body)
        o, r, u, f = VB.chest_frame(rig); bp = back_points(rig)
        P = {"up": u * 0.7 + f * 0.45 + r * 0.55, "upout": u * 0.3 + r * 0.9 + f * 0.2}[pole]
        D = {"back": bp["Dback"], "up": (bp["Dback"] + f * 0.6).normalized()}[where]
        tgt = bp[where] + (r * 0.08 if where == "back" else r * 0.06)     # рукоять дальше от головы (кисть не задевает лицо)
        _right(rig, tgt, D, _pole_t(rig, P), None)
        KEYS[t] = rig.snapshot()
    KEYS[8.0] = stance
    kt = sorted(KEYS)
    for k in range(N * hk + 1):
        t = k / hk
        body = lunge[min(len(lunge) - 1, end_tick * hk + k)]
        i = max(j for j in range(len(kt) - 1) if kt[j] <= t) if t < 8 else len(kt) - 2; a_, b_ = kt[i], kt[i + 1]
        w = min(1.0, (t - a_) / (b_ - a_))
        snap = _mix(body, _mix(KEYS[a_], KEYS[b_], w, RA), 1.0, RA)
        wl = _sm((t - 0.5) / 2.5)
        snap = _mix(snap, _mix(body0, stance, wl, LA), 1.0, LA)
        rig.restore(dict(snap, _root=body.get("_root")))
        wk_fingers.apply(rig, 1.0 - wl, 1.0 - _sm((t - 3.5) / 1.0))
        keys.append(dict(rig.snapshot(), _root=body.get("_root")))
        if t <= 3.5: chain.append(wk_grip.fist_center(rig, "Right") + wk_grip.fist_axis(rig, "Right") * 0.05)
        else:                                                          # рукоять осталась за плечом (у крепления)
            rig.restore(dict(_mix(snap, KEYS[3.5], 1.0, RA), _root=body.get("_root")))
            chain.append(wk_grip.fist_center(rig, "Right") + wk_grip.fist_axis(rig, "Right") * 0.05)
    return keys, chain
