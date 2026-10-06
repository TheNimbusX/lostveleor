"""Крушение v2: поза по набору параметров поверх стойки серии сабли (слои таза/скрутки/наклона/ног — как ab_pose.py
Абордажа v2; руки — обе на рукояти цепи, wk_grip.py).

Параметры (оси корня: f — вперёд по Direction, l — влево, u — вверх; метры v6):
  W        вес слоёв (0 = стойка серии сабли ровно)
  dz       присед таза; pyaw/cyaw — рысканье таза/груди (°, + к левому боку); lean — наклон вперёд (°)
  feet     {side: (f, l, u, yaw°, pitch°)} — лодыжка, разворот носка, наклон стопы сверх стойки (+ носок вниз)
  hand     {side: (f, l, u)} — центр кулака; hax {side: (f, l, u)} — ось кулака «мизинец→указательный»
  poles    {side: (f, l, u)} — куда локоть
  sol      {side: (sv, tv, wd, wf)} — решение руки (ключи — перебором, кадры между — интерполяцией)
  look     рысканье лица (°) — голова за грудью, вбок не дальше HEAD_YAW_MAX
  gL, gR   кулаки (wk_fingers: 0 — пальцы стойки, 1 — кулак)
"""
import bpy, math
from mathutils import Vector, Quaternion
from s_lib import rotate_world, translate_world, sagittal_lean, foot_pitch, two_bone
from wk_rig import M, fl, yaw_of, SIDES
import wk_grip, wk_fingers
from wk_keys import GAP

UP = Vector((0, 0, 1)); X = Vector((1, 0, 0))
HEAD_YAW_MAX = 30.0
DZ_MIN = -0.13          # смещение таза в Unity ≤ 0,5 длины корпуса (стойка уже 0,25; замер — wk_check)
SPINE_W = (("Spine", .35), ("Spine1", .35), ("Spine2", .30))


def lerp(a, b, w):
    return a + (b - a) * w


def body(rig, p):
    d = rig.dst; B = rig.B; W = p["W"]
    rig.reset()
    hips = d.pose.bones[M("Hips")]
    translate_world(d, hips, Vector((0, 0, max(DZ_MIN, p["dz"] * W))))
    for _ in range(2):
        e = (lerp(B["pyaw"], p["pyaw"], W) - rig.pelvis_yaw())
        rotate_world(d, hips, Quaternion(UP, math.radians(e)), rig.P("Hips"))
    rotate_world(d, hips, Quaternion(X, math.radians(.3 * (p["lean"] - B["lean"]) * W)), rig.P("Hips"))
    for _ in range(3):
        e = lerp(B["cyaw"], p["cyaw"], W) - rig.chest_yaw()
        if abs(e) < .2: break
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(UP, math.radians(e * w)), rig.P(n))
    # наклон — в плоскости груди (вперёд по взгляду груди), чтобы скрученный корпус не заваливался вбок
    goal = lerp(B["lean"], p["lean"], W)
    for _ in range(6):
        e = goal - sagittal_lean(d)
        if abs(e) < .15: break
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(X, math.radians(e * w)), rig.P(n))
    for s in SIDES:
        f, l, u, fy, fp = p["feet"][s]
        target = B["ankle"][s].lerp(fl(f, l, u), W)
        want_yaw = lerp(B["foot_yaw"][s], fy, W); want_p = B["pitch"][s] + fp * W
        foot = d.pose.bones[M(s + "Foot")]
        for _ in range(4):
            two_bone(d, [M(s + "UpLeg"), M(s + "Leg"), M(s + "Foot")], target, keep_end=True)
            cur_p, horiz = foot_pitch(d, s)
            rotate_world(d, foot, Quaternion(UP, math.radians(want_yaw - yaw_of(horiz))), rig.P(s + "Foot"))
            cur_p, horiz = foot_pitch(d, s)
            ax = UP.cross(horiz)
            if ax.length > 1e-6:
                rotate_world(d, foot, Quaternion(ax.normalized(), math.radians(want_p - cur_p)), rig.P(s + "Foot"))
            low = rig.toe_z(s)
            if low >= rig.toe_floor - 1e-4: break
            target = target + Vector((0, 0, rig.toe_floor - low))


def head(rig, p):
    d = rig.dst
    look = p.get("look", rig.B["head"]["faceYaw"])
    goal = lerp(rig.head_m()["faceYaw"], look, p["W"]); fix = 0.0
    for _ in range(12):
        e = goal - rig.head_m()["faceYaw"]
        e = max(-HEAD_YAW_MAX - fix, min(HEAD_YAW_MAX - fix, e))
        if abs(e) < .05: break
        cu = rig.hframe("Spine2")[1]
        for n in ("Neck", "Head"):
            rotate_world(d, d.pose.bones[M(n)], Quaternion(cu, math.radians(e * .5)), rig.P(n))
        fix += e
    return fix


def apply(rig, p, search=False):
    """Поза целиком. search — ключ: руки перебором (решение пишется в p["sol"])."""
    if p["W"] <= 1e-6:
        rig.reset()
        wk_fingers.apply(rig, 0.0, 0.0)
        bpy.context.view_layer.update()
        return dict(miss={"Left": 0.0, "Right": 0.0}, axerr={"Left": 0.0, "Right": 0.0}, head_fix=0.0)
    body(rig, p)
    W = p["W"]
    tg = {s: fl(*p["hand"][s]) for s in SIDES}           # кисти не смешиваются по W: ключ стойки хранит кулаки стойки
    ax = {s: fl(*p["hax"][s]).normalized() for s in SIDES}
    sol = None if search else p["sol"]
    res = {}
    out, miss, axerr = {}, {}, {}
    for s in ("Left", "Right"):
        if s == "Right" and search and p.get("handle", True) and p["gL"] > 0.5 and p["gR"] > 0.5:  # noqa
            # рукоять — куда смотрит левый кулак (к якорю): правый кулак на её конце, сам — по цепи к голове
            e = wk_grip.fist_axis(rig, "Left")
            if e.dot(ax["Left"]) < 0: e = -e
            G = wk_grip.fist_center(rig, "Left")
            tg["Right"] = G + e * p.get("gap", GAP)          # зазор кистей ключа (Charge — 0,13)
            root = lambda v: (round(-v.y, 4), round(v.x, 4), round(v.z, 4))
            p["hand"]["Right"] = root(tg["Right"]); p["hand"]["Left"] = root(G); p["hax"]["Left"] = root(e)
            ax["Right"] = e; p["hax"]["Right"] = root(e)                  # правый кулак — по рукояти, цепь гнётся за ним
        o, m, a = wk_grip.solve_one(rig, s, tg[s], ax[s], p["poles"][s], None if sol is None else sol[s],
                                    hint=p.get("hint", {}).get(s), axw=p.get("axw", {}).get(s))   # вес оси кулака ключа (Charge)
        out[s], miss[s], axerr[s] = o, m, a
    if search: p["sol"] = out
    fix = head(rig, p)
    wk_fingers.apply(rig, p.get("gL", 0.0) * W, p.get("gR", 0.0) * W)
    bpy.context.view_layer.update()
    return dict(miss=miss, axerr=axerr, head_fix=round(fix, 1))
