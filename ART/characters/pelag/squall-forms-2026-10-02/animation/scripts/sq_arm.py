"""Шквал v2: рука одним решением — локоть шарниром (как в стойке), пронация предплечья и кисть под клинок.

Кости руки строятся прямо в мире:
  * плечо и предплечье: ось кости вдоль сегмента, ось шарнира локтя = нормаль плоскости плечо–локоть–кисть,
    привязанная к кости так же, как в стойке серии сабли — крутки «сами собой» нет; пронация tv — от стойки;
  * кисть (правка 02.10): кисть стойки + отклонение wd вокруг оси ладони (локальная Z кисти: + локтевое,
    к мизинцу) и сгибание wf вокруг оси «мизинец→указательный» (локальная X). Клинок лежит в плоскости
    предплечье–указательный: в стойке кисть отведена к большому пальцу на 32° от покоя, клинок к предплечью 105°;
    локтевое отклонение подводит клинок к линии предплечья (до ~37° при 40° от покоя) — так рука, вытянутая
    вперёд, держит клинок на цель без крутки предплечья. Пределы от покоя: отклонение −34…+40°, сгибание ±40°.
  * ключ решается перебором (tv в окне, wd, wf) под направление клинка; кадры между ключами берут tv/wd/wf
    интерполяцией — кисть и сабля не перескакивают между решениями.
"""
import math, os
from mathutils import Vector, Quaternion, Matrix
from b_common import blade
from sq_rig import M

PRON = (-80, 58)        # от стойки (R +12° от покоя): итого −68…+70° от покоя
STANCE_DEV = -32.4      # отклонение кисти стойки от покоя (к большому пальцу), замер probe_wrist
WD = (-2.0, 72.0)       # отклонение от стойки: итого −34…+40° от покоя
WF = (-40.0, 40.0)
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
                 Qh=Rf.transposed() @ Rh, ax0=(H - S).normalized(), pole0=(E - S) - (E - S).dot((H - S).normalized()) * (H - S).normalized())
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


def wrist(Qh, wd, wf):
    return Qh @ Matrix.Rotation(math.radians(wd), 3, 'Z') @ Matrix.Rotation(math.radians(wf), 3, 'X')


def arm_frame(rig, s, H, pole):
    a = rig.ARM[s]
    S = rig.P(s + "Arm")
    axis = (H - S).normalized()
    E, H2 = elbow(S, H, pole, a["L1"], a["L2"])
    pp = pole - pole.dot(axis) * axis
    n = pp.cross(axis).normalized()          # нормаль плоскости локтя — от подсказки, без переворота у прямой руки
    Ru = frame_rot(a["yu"], a["nu"], (E - S).normalized(), n)
    Rf0 = frame_rot(a["yf"], a["nf"], (H2 - E).normalized(), n)
    return S, E, H2, Ru, Rf0, (H2 - E).normalized()


def solve(rig, s, H, pole, D=None, tv=0.0, wd=0.0, wf=0.0, sw=0.0, opt=False, win=0, hint=None, win_sw=0):
    """H — кисть (мир), pole — куда локоть, sw — поворот локтя вокруг оси плечо–кисть (°), tv — пронация от стойки (°),
    wd/wf — кисть от стойки (°). opt: ключ — перебор sw (±win_sw), tv (±win), wd, wf под направление клинка D
    (hint = (sw, tv, wd, wf) — тянуться к нему)."""
    a = rig.ARM[s]
    ax = (H - rig.P(s + "Arm")).normalized()
    miss = 0.0
    if opt and D is not None and s == "Right":
        h_sw, h_tv, h_wd, h_wf = hint if hint is not None else (sw, tv, wd, wf)
        best = None
        for sv in range(int(round(sw)) - win_sw, int(round(sw)) + win_sw + 1, 15):
            S, E, H2, Ru, Rf0, fa = arm_frame(rig, s, H, Quaternion(ax, math.radians(sv)) @ pole)
            for c in range(int(round(tv)) - win, int(round(tv)) + win + 1, 5):
                if not PRON[0] <= c <= PRON[1]: continue
                Rf = Quaternion(fa, math.radians(c)).to_matrix() @ Rf0
                for dv in range(int(WD[0]), int(WD[1]) + 1, 3):
                    for fx in range(int(WF[0]), int(WF[1]) + 1, 8):
                        dc = Rf @ wrist(a["Qh"], dv, fx) @ a["bl"]
                        ang = math.degrees(dc.angle(D))
                        cost = ang + .10 * abs(c - h_tv) + .03 * abs(dv - h_wd) + .03 * abs(fx - h_wf) + .05 * abs(sv - h_sw)
                        if best is None or cost < best[0]: best = (cost, sv, c, dv, fx, ang)
        _, sw, tv, wd, wf, miss = best
    S, E, H2, Ru, Rf0, fa = arm_frame(rig, s, H, Quaternion(ax, math.radians(sw)) @ pole)
    Rf = Quaternion(fa, math.radians(tv)).to_matrix() @ Rf0
    Rh = Rf @ wrist(a["Qh"], wd, wf)
    if D is not None and s == "Right":
        miss = math.degrees((Rh @ a["bl"]).angle(D))
    set_world(rig, s + "Arm", S, Ru)
    set_world(rig, s + "ForeArm", E, Rf)
    set_world(rig, s + "Hand", H2, Rh)
    return dict(swivel=float(sw), pron=float(tv), wd=float(wd), wf=float(wf), miss=round(miss, 1))
