"""Шквал v2: рука одним решением — локоть шарниром (как в стойке), пронация предплечья и изгиб кисти под клинок.

Вместо IK «от прошлой позы» (у рывка) кости руки строятся прямо в мире:
  * плечо и предплечье: ось кости вдоль сегмента, ось шарнира локтя = нормаль плоскости плечо–локоть–кисть,
    привязанная к кости так же, как в стойке серии сабли — крутки «сами собой» нет;
  * правая кисть: перебор поворота плоскости локтя (s) и пронации предплечья (t, от стойки), остаток —
    изгиб кисти ≤ WRIST_MAX; соседние кадры тянутся к прошлому решению (сабля не перескакивает).
  * левая кисть держит угол к предплечью стойки (кисть прямая, как в стойке).
"""
import math
from mathutils import Vector, Quaternion, Matrix
from b_common import blade
from sq_rig import M

WRIST_MAX = 45.0
import os
PRON = (-80, 58) if not os.environ.get("SQ_WIDE") else (-120, 120)   # от стойки (R +12° от покоя): итого −68…+70° от покоя
SWIVEL_MAX = 45 if not os.environ.get("SQ_WIDE") else 120
LAST = {}


def wrot(rig, n):
    return (rig.dst.matrix_world.to_3x3().normalized() @ rig.dst.pose.bones[M(n)].matrix.to_3x3().normalized())


def frame_rot(y_l, n_l, y_w, n_w):
    n_l = (n_l - n_l.dot(y_l) * y_l).normalized(); n_w = (n_w - n_w.dot(y_w) * y_w).normalized()
    Fl = Matrix((y_l, n_l, y_l.cross(n_l))).transposed()
    Fw = Matrix((y_w, n_w, y_w.cross(n_w))).transposed()
    return Fw @ Fl.transposed()


def prepare(rig):
    """Замер стойки: оси шарнира в костях, кисть к предплечью, клинок в кисти."""
    A = {}
    for s in ("Left", "Right"):
        S, E, H = rig.P(s + "Arm"), rig.P(s + "ForeArm"), rig.P(s + "Hand")
        n0 = (E - S).cross(H - E).normalized()
        Ru, Rf, Rh = wrot(rig, s + "Arm"), wrot(rig, s + "ForeArm"), wrot(rig, s + "Hand")
        a = dict(L1=(E - S).length, L2=(H - E).length,
                 yu=Ru.transposed() @ (E - S).normalized(), nu=Ru.transposed() @ n0,
                 yf=Rf.transposed() @ (H - E).normalized(), nf=Rf.transposed() @ n0,
                 Qh=Rf.transposed() @ Rh, pole0=(E - S) - (E - S).dot((H - S).normalized()) * (H - S).normalized())
        if s == "Right":
            r, t = blade(rig.dst)
            a["bl"] = Rh.transposed() @ (t - r).normalized()
        A[s] = a
    rig.ARM = A
    LAST.clear()


def elbow(S, H, pole, L1, L2):
    d = H - S
    dist = max(1e-4, min(d.length, (L1 + L2) * .999)); dn = d.normalized()
    p = pole - pole.dot(dn) * dn
    if p.length < 1e-6: p = Vector((0, 0, -1)) - dn.z * dn
    p.normalize()
    x = (L1 * L1 - L2 * L2 + dist * dist) / (2 * dist)
    h = math.sqrt(max(0.0, L1 * L1 - x * x))
    return S + dn * x + p * h, S + dn * dist


def set_world(rig, n, head, R):
    d = rig.dst
    d.pose.bones[M(n)].matrix = d.matrix_world.inverted() @ (Matrix.Translation(head) @ R.to_4x4())
    import bpy
    bpy.context.view_layer.update()


def solve(rig, s, H, pole, D=None, fixed=None):
    """H — кисть (мир), pole — куда локоть, D — направление клинка (только правая). Возвращает замер."""
    a = rig.ARM[s]
    S = rig.P(s + "Arm")
    best = None
    sw_range = range(-SWIVEL_MAX, SWIVEL_MAX + 1, 5) if D is not None else (0,)
    tw_range = range(PRON[0], PRON[1] + 1, 5) if D is not None else (0,)
    last = LAST.get(s)
    if fixed is not None:                       # кадр между ключами: s, t интерполированы — без перебора
        sw_range, tw_range, last = (fixed[0],), (fixed[1],), None
    axis = (H - S).normalized()
    for sv in sw_range:
        pl = Quaternion(axis, math.radians(sv)) @ pole
        E, H2 = elbow(S, H, pl, a["L1"], a["L2"])
        pp = pl - pl.dot(axis) * axis
        n = pp.cross(axis).normalized()          # нормаль плоскости локтя — от подсказки, без переворота у прямой руки
        Ru = frame_rot(a["yu"], a["nu"], (E - S).normalized(), n)
        Rf0 = frame_rot(a["yf"], a["nf"], (H2 - E).normalized(), n)
        for tv in tw_range:
            Rf = Quaternion((H2 - E).normalized(), math.radians(tv)).to_matrix() @ Rf0
            Rh = Rf @ a["Qh"]
            bend = 0.0
            if D is not None:
                bend = math.degrees((Rh @ a["bl"]).angle(D))
            cost = 1.0 * max(0.0, bend - WRIST_MAX) + .15 * bend + .01 * abs(sv) + .01 * abs(tv)
            if last is not None and D is not None:
                cost += .25 * abs(sv - last[0]) + .2 * abs(tv - last[1])
            if best is None or cost < best[0]:
                best = (cost, sv, tv, E, H2, Ru, Rf, Rh, bend)
    _, sv, tv, E, H2, Ru, Rf, Rh, bend = best
    miss = 0.0
    if D is not None:
        LAST[s] = (sv, tv)
        dc = Rh @ a["bl"]
        ax = dc.cross(D)
        if ax.length > 1e-8:
            Rh = Quaternion(ax.normalized(), math.radians(min(bend, WRIST_MAX))).to_matrix() @ Rh
        miss = max(0.0, bend - WRIST_MAX)
    set_world(rig, s + "Arm", S, Ru)
    set_world(rig, s + "ForeArm", E, Rf)
    set_world(rig, s + "Hand", H2, Rh)
    return dict(swivel=sv, pron=tv, wrist=round(min(bend, WRIST_MAX), 1), miss=round(miss, 1))
