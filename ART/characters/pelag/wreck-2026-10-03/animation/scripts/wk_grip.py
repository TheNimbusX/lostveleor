"""Крушение v2: обе кисти на рукояти цепи — левый кулак у кисточки (конец рукояти), правый ниже, на цепи.

Ключ задаёт точку левого кулака G (центр обхвата), ось рукояти d (от левого кулака к правому = к якорю) и зазор
кистей gap (0,10–0,25 м): правый кулак = G + gap·d. Кулак держит ось рукояти поперёк ладони: ось кисти
«мизинец → указательный» (bax) смотрит по d у обеих рук (указательный к якорю, как хват биты).
Рука решается как в Абордаже (wk_arm.solve: локоть шарниром, пронация tv, кисть wd/wf), только направление
ищется под ось рукояти, а не под клинок; ключи — перебором, кадры между ключами — интерполяцией (sv, tv, wd, wf).
Кольцо рукояти (где цепь выходит из рукояти) — G + RING·d: по нему считается проверочный кистень (wk_flail).
"""
import math
from mathutils import Vector, Quaternion, Matrix
from wk_rig import M, fl
import wk_arm, wk_fingers
from s_lib import rotate_world, bone_world_rot

REVERSE = 6.0                   # лёгкий штраф хвату «указательным от якоря»
RING = 0.10                     # кольцо рукояти от левого кулака по оси (рукоять 0,23 м, кулак у кисточки)
CLAV_K, CLAV_MAX = 0.25, 20.0
TWIST_MAX = 66.0                # крутка предплечья от покоя (предел задачи 70°)
WD_LIM, WF_LIM = 34.0, 40.0
FIST = {}


def _hand_rot(rig, s):
    return wk_arm.wrot(rig, s + "Hand")


def prepare(rig):
    """Кулак (пальцы g = 1): центр обхвата и ось мизинец→указательный в осях кости кисти."""
    wk_arm.prepare(rig)
    wk_fingers.prepare(rig)
    wk_fingers.apply(rig, 1.0, 1.0)
    import bpy
    bpy.context.view_layer.update()
    for s in ("Left", "Right"):
        H = rig.P(s + "Hand"); Rh = _hand_rot(rig, s)
        knuck = [rig.P(s + "Hand" + f + "1") for f in ("Index", "Middle", "Ring", "Pinky")]
        mid = [rig.P(s + "Hand" + f + "2") for f in ("Index", "Middle", "Ring", "Pinky")]
        c = sum(knuck + mid, Vector()) / 8.0
        palm = sum(knuck, Vector()) / 4.0
        c = c.lerp(palm, 0.25)                       # ось рукояти — между ладонью и согнутыми пальцами
        bax = (knuck[0] - knuck[3]).normalized()
        FIST[s] = dict(off=Rh.transposed() @ (c - H), bax=Rh.transposed() @ bax)
        tw = rig.B["twist_fore"][s]
        FIST[s]["tv"] = (-TWIST_MAX - tw, TWIST_MAX - tw)
    stance_fist(rig)
    return {s: dict(off=[round(x, 3) for x in FIST[s]["off"]], bax=[round(x, 3) for x in FIST[s]["bax"]]) for s in FIST}


def fist_center(rig, s):
    return rig.P(s + "Hand") + _hand_rot(rig, s) @ FIST[s]["off"]


def fist_axis(rig, s):
    return (_hand_rot(rig, s) @ FIST[s]["bax"]).normalized()


def _clavicle(rig, s, H, Rd):
    a = rig.ARM[s]
    v = H - rig.P(s + "Arm")
    qd = a["ax0"].rotation_difference((Rd.inverted() @ v).normalized())
    if qd.angle > 1e-4:
        qc = Quaternion(qd.axis, min(qd.angle * CLAV_K, math.radians(CLAV_MAX)))
        rotate_world(rig.dst, rig.dst.pose.bones[M(s + "Shoulder")], Rd @ qc @ Rd.inverted(), rig.P(s + "Shoulder"))


AXIS_W = {"Left": 0.20, "Right": 0.10}     # левый кулак — рукоять сама идёт за кулаком; правый — по рукояти


def _search(rig, s, H, pole, D, hint, target=None, axw=None):
    """Перебор (sv, tv, wd, wf) под ось рукояти D рядом с подсказкой hint."""
    a = rig.ARM[s]; F = FIST[s]
    ax = (H - rig.P(s + "Arm")).normalized()
    h_sv, h_tv, h_wd, h_wf = hint
    lo, hi = F["tv"]
    best = None
    for sv in range(-30, 31, 15):
        S, E, H2, Ru, Rf0, fa = wk_arm.arm_frame(rig, s, H, Quaternion(ax, math.radians(sv)) @ pole)
        for tv in range(int(lo), int(hi) + 1, 6):
            Rf = Quaternion(fa, math.radians(tv)).to_matrix() @ Rf0
            for wd in range(-int(WD_LIM), int(WD_LIM) + 1, 6):
                for wf in range(-int(WF_LIM), int(WF_LIM) + 1, 8):
                    Rh = Rf @ wk_arm.wrist(a["Qh"], wd, wf)
                    ang = math.degrees((Rh @ F["bax"]).angle(D))
                    ang = min(ang, 180.0 - ang + REVERSE)       # рукоять — палка: кулак держит её любой стороной
                    reach = 0.0
                    if target is not None:                    # кисть под этот кулак должна доставать от плеча
                        reach = max(0.0, (target - Rh @ F["off"] - S).length - 0.985 * (a["L1"] + a["L2"])) * 400.0
                    cost = ((AXIS_W[s] if axw is None else axw) * ang + reach + .5 * abs(tv - h_tv) + .2 * abs(wd - h_wd) + .2 * abs(wf - h_wf) + .4 * abs(sv - h_sv)
                            + .02 * (abs(wd) + abs(wf)))
                    if best is None or cost < best[0]: best = (cost, sv, tv, wd, wf, ang)
    return best


def solve_one(rig, s, target, D, pole_t, sol=None, hint=None, axw=None):
    """Одна кисть: центр кулака в target (мир), ось кулака по D. sol — (sv, tv, wd, wf) готовое (кадр между ключами),
    иначе перебор рядом с hint. Возвращает решение, промах кулака (м), угол оси кулака к D (°)."""
    F = FIST[s]; a = rig.ARM[s]
    D = D.normalized()
    Rd = bone_world_rot(rig.dst, rig.dst.pose.bones[M("Spine2")]).to_quaternion() @ rig.B["chest_rot"].inverted()
    pole = fl(*pole_t).normalized()
    Rh = _hand_rot(rig, s)
    H = target - Rh @ F["off"]
    _clavicle(rig, s, H, Rd)
    for it in range(3):
        if sol is not None:
            sv, tv, wd, wf = sol
        else:
            _, sv, tv, wd, wf, _ = _search(rig, s, H, pole, D, hint or (0.0, 0.0, 0.0, 0.0), target, axw)
        ax = (H - rig.P(s + "Arm")).normalized()
        S, E, H2, Ru, Rf0, fa = wk_arm.arm_frame(rig, s, H, Quaternion(ax, math.radians(sv)) @ pole)
        Rf = Quaternion(fa, math.radians(tv)).to_matrix() @ Rf0
        Rh = Rf @ wk_arm.wrist(a["Qh"], wd, wf)
        H = target - Rh @ F["off"]
    wk_arm.solve(rig, s, H, pole, None, tv, wd, wf, sv)
    a_ = math.degrees(fist_axis(rig, s).angle(D))
    return (float(sv), float(tv), float(wd), float(wf)), (fist_center(rig, s) - target).length, min(a_, 180.0 - a_)


def stance_fist(rig):
    """Кулаки стойки (центр обхвата и ось при руке стойки) и локти стойки — в осях корня, для ключа стойки."""
    rig.reset()
    root = lambda v: (-v.y, v.x, v.z)
    out = dict(pos={}, ax={}, pole={})
    for s in ("Left", "Right"):
        out["pos"][s] = fist_center(rig, s).copy()
        out["ax"][s] = fist_axis(rig, s).copy()
        out["pole"][s] = tuple(round(c, 4) for c in root(rig.ARM[s]["pole0"].normalized()))
    rig.STANCE_FIST = out
    return out


def ring(G, d):
    return G + d.normalized() * RING
