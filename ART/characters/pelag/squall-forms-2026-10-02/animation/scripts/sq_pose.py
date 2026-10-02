"""Шквал v2: поза по набору параметров поверх стойки серии сабли (слои как у рывка d_author.py).

Параметры позы (оси корня: f — вперёд по прыжку, l — влево, u — вверх; метры v6):
  W      общий вес слоёв (0 = стойка серии сабли ровно, 1 = поза целиком)
  dz     присед таза; pyaw/cyaw — рысканье таза/груди в мире (°, + к левому боку); lean — наклон вперёд (°)
  feet   {side: (f, l, u, yaw°, pitch°)}: лодыжка, разворот носка, наклон стопы сверх стойки (+ носок вниз)
  hands  {side: (f, l, u)}; poles {side: (f, l, u)} — куда смотрит локоть
  blade  (f, l, u) — направление клинка; wB — вес разворота клинка
  look   рысканье лица (°, корень) — голова следует за грудью, вбок не дальше HEAD_YAW_MAX
"""
import bpy, math
from mathutils import Vector, Quaternion, Matrix
from b_common import wpos, blade as blade_of
from s_lib import rotate_world, translate_world, sagittal_lean, foot_pitch, two_bone, slerp_dir, bone_world_rot
from sq_rig import M, fl, yaw_of, SIDES
import sq_arm

UP = Vector((0, 0, 1)); X = Vector((1, 0, 0))
WRIST_MAX = 42.0
FOREARM_MAX = 60.0
HEAD_YAW_MAX = 30.0
SPINE_W = (("Spine", .35), ("Spine1", .35), ("Spine2", .30))
LAST = {"sw": None}   # решение клинка прошлого кадра: сабля не перескакивает между решениями


def lerp(a, b, w):
    return a + (b - a) * w


def apply(rig, p):
    d = rig.dst; B = rig.B; W = p["W"]
    rig.reset()
    hips = d.pose.bones[M("Hips")]
    # 1. таз: присед, рысканье, лёгкий наклон таза (треть наклона корпуса)
    translate_world(d, hips, Vector((0, 0, p["dz"] * W)))
    for _ in range(2):
        e = (lerp(B["pyaw"], p["pyaw"], W) - rig.pelvis_yaw())
        rotate_world(d, hips, Quaternion(UP, math.radians(e)), rig.P("Hips"))
    rotate_world(d, hips, Quaternion(X, math.radians(.3 * (p["lean"] - B["lean"]) * W)), rig.P("Hips"))
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
    # 5–6. руки и клинок одним решением (sq_arm.py): локоть шарниром, пронация, изгиб кисти
    A = rig.ARM; wB = p.get("wB", 1.0) * W
    res = {}
    for s in ("Right", "Left"):
        H = B["hand"][s].lerp(fl(*p["hands"][s]), W)
        # локоть «вниз и наружу» от груди (подсказка ключа больше не нужна: она вырождалась у руки вдоль неё)
        out = rig.P(s + "Arm") - rig.P("Spine2"); out.z = 0
        nat = (Vector((0, 0, -1)) + out.normalized() * 0.7).normalized()
        pole = slerp_dir(A[s]["pole0"], nat, W)
        D = None
        if s == "Right":
            D = slerp_dir(B["blade"], fl(*p["blade"]), wB)
        fixed = (p["sv"], p["tv"]) if (s == "Right" and p.get("fixed")) else None
        res[s] = sq_arm.solve(rig, s, H, pole, D, fixed)
    wrist, sw, res_ang = res["Right"]["wrist"], (res["Right"]["swivel"], res["Right"]["pron"]), res["Right"]["miss"]
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
    return dict(wrist=round(wrist, 1), swivel_twist=sw, head_yaw_fix=round(fix, 1), blade_miss=round(max(0.0, res_ang), 1))
