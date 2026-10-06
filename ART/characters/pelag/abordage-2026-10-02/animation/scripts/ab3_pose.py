"""Абордаж v3: копия ab_pose.py (v2) с одним добавлением — доля наклона на таз ptilt (по умолчанию .3, как v2;
принятые ключи без ptilt дают ровно прежнюю позу). Поза по набору параметров поверх стойки серии сабли.

Параметры позы (оси корня: f — вперёд по прыжку, l — влево, u — вверх; метры v6):
  W      общий вес слоёв (0 = стойка серии сабли ровно, 1 = поза целиком)
  dz     присед таза; pyaw/cyaw — рысканье таза/груди в мире (°, + к левому боку); lean — наклон вперёд (°)
  feet   {side: (f, l, u, yaw°, pitch°)}: лодыжка, разворот носка, наклон стопы сверх стойки (+ носок вниз)
  hands  {side: (f, l, u)}; poles {side: (f, l, u)} — куда смотрит локоть
  blade  (f, l, u) — направление клинка; wB — вес разворота клинка
  look   рысканье лица (°, корень) — голова следует за грудью, вбок не дальше HEAD_YAW_MAX
"""
import bpy, math, os
from mathutils import Vector, Quaternion, Matrix
from b_common import wpos, blade as blade_of
from s_lib import rotate_world, translate_world, sagittal_lean, foot_pitch, two_bone, slerp_dir, bone_world_rot
from ab_rig import M, fl, yaw_of, SIDES
import ab_arm, ab_fingers

UP = Vector((0, 0, 1)); X = Vector((1, 0, 0))
WRIST_MAX = 42.0
FOREARM_MAX = 60.0
HEAD_YAW_MAX = 30.0
CLAV_K, CLAV_MAX = 0.25, 20.0      # доля хода руки на ключицу и её предел (°)
DZ_MIN = -0.075    # правка 02.10: смещение таза в Unity ≤ 0,40 — сборка проходит и с порогом по умолчанию (было −0,125 → 0,48)
SPINE_W = (("Spine", .35), ("Spine1", .35), ("Spine2", .30))
LAST = {"sw": None}   # решение клинка прошлого кадра: сабля не перескакивает между решениями


def lerp(a, b, w):
    return a + (b - a) * w


def apply(rig, p):
    d = rig.dst; B = rig.B; W = p["W"]
    rig.reset()
    hips = d.pose.bones[M("Hips")]
    # 1. таз: присед, рысканье, лёгкий наклон таза (треть наклона корпуса)
    # присед не глубже DZ_MIN: перенос в Unity меряет смещение таза от привязки (стойка уже 0,45, предел вида рывка 0,5)
    translate_world(d, hips, Vector((0, 0, max(DZ_MIN, p["dz"] * W))))
    for _ in range(2):
        e = (lerp(B["pyaw"], p["pyaw"], W) - rig.pelvis_yaw())
        rotate_world(d, hips, Quaternion(UP, math.radians(e)), rig.P("Hips"))
    rotate_world(d, hips, Quaternion(X, math.radians(p.get("ptilt", .3) * (p["lean"] - B["lean"]) * W)), rig.P("Hips"))
    # 2. скрутка груди к тазу — позвоночником, вокруг вертикали
    for _ in range(3):
        e = lerp(B["cyaw"], p["cyaw"], W) - rig.chest_yaw()
        if abs(e) < .2: break
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(UP, math.radians(e * w)), rig.P(n))
    # 3. наклон корпуса вперёд (к −Y) позвоночником
    goal = lerp(B["lean"], p["lean"], W)
    for _ in range(6):
        e = goal - sagittal_lean(d)
        if abs(e) < .15: break
        for n, w in SPINE_W:
            rotate_world(d, d.pose.bones[M(n)], Quaternion(X, math.radians(e * w)), rig.P(n))
    # 4. ноги: лодыжка, разворот и наклон стопы, носок не ниже земли
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
    # 5–6. руки и клинок одним решением (ab_arm.py): локоть шарниром, пронация, кисть (отклонение/сгибание).
    # Свободная левая кисть задаётся от левого плеча в осях груди (f — куда смотрит грудь): поворот корпуса
    # несёт руку с собой, рука не «отстаёт» от груди.
    A = rig.ARM
    res = {}
    Rd = bone_world_rot(d, d.pose.bones[M("Spine2")]).to_quaternion() @ B["chest_rot"].inverted()   # грудь от стойки
    for s in ("Right", "Left"):
        # Абордаж: обе кисти — от своего плеча в осях КОРНЯ (цепь и кулак смотрят на цель, а не за грудью);
        # |v| — расстояние плечо–кисть ПОСЛЕ ключицы: 0,51·cos(сгиб/2), сгиб локтя задаётся длиной v.
        v = Vector(B["hand_root" + s[0]]).lerp(Vector(p["hands"][s]), W)
        if True:
            # ключица берёт часть хода руки (до CLAV_MAX): плечо подаётся за кистью, как у живого плеча
            qd = A[s]["ax0"].rotation_difference((Rd.inverted() @ fl(*v)).normalized())
            if qd.angle > 1e-4:
                qc = Quaternion(qd.axis, min(qd.angle * CLAV_K, math.radians(CLAV_MAX)))
                rotate_world(d, d.pose.bones[M(s + "Shoulder")], Rd @ qc @ Rd.inverted(), rig.P(s + "Shoulder"))
        H = rig.P(s + "Arm") + fl(*v)
        # локоть: направление локтя стойки, повёрнутое кратчайшим поворотом оси плечо–кисть стойки к новой
        ax = H - rig.P(s + "Arm")
        if s == "Left":     # локоть свободной руки — в осях груди: поворот корпуса не крутит плечо
            pole = Rd @ (A[s]["ax0"].rotation_difference((Rd.inverted() @ ax).normalized()) @ A[s]["pole0"])
        else:
            pole = A[s]["ax0"].rotation_difference(ax.normalized()) @ A[s]["pole0"]
        wp = (p.get("wp", 0.0) if s == "Right" else 0.0) * W
        if wp > 1e-4:                             # явный локоть ключа (замах-гарпун: локоть в сторону и вверх)
            pole = pole.normalized().lerp(fl(*p["poles"][s]).normalized(), min(1.0, wp))
        if s == "Right":
            D = fl(*p["blade"]).normalized()
            if p.get("fixed"):
                res[s] = ab_arm.solve(rig, s, H, pole, D, p["tv"], p.get("wd", 0.0), p.get("wf", 0.0), p.get("sv", 0.0))
            elif "arm" in p:                      # ключ с заданной рукой: (локоть sw, пронация tv, кисть wd, wf)
                sw_, tv_, wd_, wf_ = p["arm"]
                res[s] = ab_arm.solve(rig, s, H, pole, D, tv_, wd_, wf_, sw_)
            else:                                 # ключ контакта: решение под клинок рядом с подсказкой
                hint = tuple(p.get("hint", (0.0, 0.0, 0.0, 0.0)))
                res[s] = ab_arm.solve(rig, s, H, pole, D, hint[1], hint[2], hint[3], hint[0], opt=True,
                                      win=int(p.get("win", 20)), hint=hint, win_sw=int(p.get("win_sw", 30)))
        else:
            res[s] = ab_arm.solve(rig, s, H, pole)
    wrist, sw, res_ang = (res["Right"]["wd"], res["Right"]["wf"]), (res["Right"]["swivel"], res["Right"]["pron"]), res["Right"]["miss"]
    # 7. голова следует за грудью: наклон к груди как в стойке, только поворот вбок вокруг оси груди
    look = p.get("look", 0.0)
    yaw0 = rig.head_m()["faceYaw"]; fix = 0.0
    goal = lerp(yaw0, look, W)
    for _ in range(12):
        e = goal - rig.head_m()["faceYaw"]
        e = max(-HEAD_YAW_MAX - fix, min(HEAD_YAW_MAX - fix, e))
        if abs(e) < .05: break
        cu = rig.hframe("Spine2")[1]
        for n in ("Neck", "Head"):
            rotate_world(d, d.pose.bones[M(n)], Quaternion(cu, math.radians(e * .5)), rig.P(n))
        fix += e
    # 8. пальцы: хват левой (рукоять цепи) и правой (якорь / ладонь / кулак) — ab_fingers.py
    ab_fingers.apply(rig, p.get("gL", 0.0) * W, p.get("gR", 0.0) * W)
    bpy.context.view_layer.update()
    return dict(wrist=wrist, wd=res["Right"]["wd"], wf=res["Right"]["wf"], swivel_twist=sw, head_yaw_fix=round(fix, 1),
                blade_miss=round(max(0.0, res_ang), 1))
