"""Бросок якоря (копия Абордажа v2 + абсолютные цели кистей и правая на цепи): поза по набору параметров поверх стойки серии сабли (слои как у рывка d_author.py).

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
from at_rig import M, fl, yaw_of, SIDES
import at_arm, at_fingers

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


SOCKET = Vector((0.0, 0.076, 0.020))    # grip_socket.json (0; 0,045; 0,012) × 1,82 м / 1,0788 (Unity → риг Blender), оси кости


def socket(rig, s):
    """Кольцо рукояти (левая) или опора на цепи (правая) — точка кисти, из которой выходит цепь в игре."""
    pb = rig.dst.pose.bones[M(s + "Hand")]
    R = (rig.dst.matrix_world @ pb.matrix).to_3x3().normalized()
    return rig.P(s + "Hand") + R @ SOCKET


def socket_offset_guess(rig, s):
    return socket(rig, s) - rig.P(s + "Hand")


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
    # 5–6. руки и клинок одним решением (at_arm.py): локоть шарниром, пронация, кисть (отклонение/сгибание).
    # Свободная левая кисть задаётся от левого плеча в осях груди (f — куда смотрит грудь): поворот корпуса
    # несёт руку с собой, рука не «отстаёт» от груди.
    A = rig.ARM
    res = {}
    Rd = bone_world_rot(d, d.pose.bones[M("Spine2")]).to_quaternion() @ B["chest_rot"].inverted()   # грудь от стойки
    # Бросок якоря: сначала ЛЕВАЯ (рукоять цепи), потом правая — правая может лежать на цепи впереди левой
    # (cR: кисть = левая + offR в осях корня; цепь из рукояти идёт вдоль Dir, голова якоря в метрах впереди).
    # haL/haR — вес абсолютной цели кисти habs (f, l, u от корня) против цели «от плеча» hands (как у Абордажа).
    HW = {}

    def arm_to(s, target):
        """Ключица, локоть, решение руки к цели target (мир). Ключица ставится от снимка до руки — повтор не копится."""
        sh = d.pose.bones[M(s + "Shoulder")]
        sh.matrix_basis = SH0[s].copy(); bpy.context.view_layer.update()
        v_eff = target - rig.P(s + "Arm")
        # ключица берёт часть хода руки (до CLAV_MAX): плечо подаётся за кистью, как у живого плеча
        qd = A[s]["ax0"].rotation_difference((Rd.inverted() @ v_eff).normalized())
        if qd.angle > 1e-4:
            qc = Quaternion(qd.axis, min(qd.angle * CLAV_K, math.radians(CLAV_MAX)))
            rotate_world(d, sh, Rd @ qc @ Rd.inverted(), rig.P(s + "Shoulder"))
        H = target
        # локоть: направление локтя стойки, повёрнутое кратчайшим поворотом оси плечо–кисть стойки к новой
        ax = H - rig.P(s + "Arm")
        if s == "Left":     # локоть свободной руки — в осях груди: поворот корпуса не крутит плечо
            pole = Rd @ (A[s]["ax0"].rotation_difference((Rd.inverted() @ ax).normalized()) @ A[s]["pole0"])
        else:
            pole = A[s]["ax0"].rotation_difference(ax.normalized()) @ A[s]["pole0"]
        wp = p.get("wp" + ("" if s == "Right" else "L"), 0.0) * W
        if wp > 1e-4:                             # явный локоть ключа
            pole = pole.normalized().lerp(fl(*p["poles"][s]).normalized(), min(1.0, wp))
        if s == "Right":
            D = fl(*p["blade"]).normalized()
            if p.get("fixed"):
                return at_arm.solve(rig, s, H, pole, D, p["tv"], p.get("wd", 0.0), p.get("wf", 0.0), p.get("sv", 0.0))
            sw_, tv_, wd_, wf_ = p["arm"]          # ключ с заданной рукой: (локоть sw, пронация tv, кисть wd, wf)
            return at_arm.solve(rig, s, H, pole, D, tv_, wd_, wf_, sw_)
        return at_arm.solve(rig, s, H, pole)

    SH0 = {s: d.pose.bones[M(s + "Shoulder")].matrix_basis.copy() for s in ("Left", "Right")}
    for s in ("Left", "Right"):
        v = Vector(B["hand_root" + s[0]]).lerp(Vector(p["hands"][s]), W)
        target = rig.P(s + "Arm") + fl(*v)
        ha = p.get("ha" + s[0], 0.0) * W
        if ha > 1e-6:
            target = target.lerp(fl(*p["habs"][s]), min(1.0, ha))
        c = p.get("cR", 0.0) * W if s == "Right" else 0.0
        if c <= 1e-6:
            res[s] = arm_to(s, target)
        else:
            # правая на цепи: точка опоры кисти (сокет grip_socket.json) — на прямой из кольца рукояти левой
            # вдоль Dir с наклоном offR[2] (вниз к голове), сдвиг вбок offR[1], вперёд на offR[0] (anchor-core §4.1, #11)
            along, bias, slope = p["offR"]
            G = socket(rig, "Left")
            dline = fl(1.0, 0.0, slope).normalized()
            want = G + fl(0.0, bias, 0.0) + dline * along
            goal = target.lerp(want, min(1.0, c))
            Ht = goal - (socket_offset_guess(rig, "Right"))
            for _ in range(4):
                res[s] = arm_to(s, Ht)
                err = goal - socket(rig, "Right")
                if err.length < 0.002: break
                Ht = Ht + err
        HW[s] = rig.P(s + "Hand").copy()
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
    # 7b. Бросок якоря: в натяге и тяге корпус откинут назад — голова опускается к груди, лицо держится к Dir
    #     (wP — вес, lookP — наклон лица от стойки, °); голову вверх к груди не поднимаем (проверка relP).
    wP = p.get("wP", 0.0) * W
    if wP > 1e-4:
        goalP = lerp(rig.head_m()["faceP"], B["head"]["faceP"] + p.get("lookP", 0.0), min(1.0, wP))
        for _ in range(10):
            e = goalP - rig.head_m()["faceP"]
            if abs(e) < .05: break
            cr = rig.hframe("Spine2")[2]
            f0 = rig.head_m()["faceP"]
            for n in ("Neck", "Head"):
                rotate_world(d, d.pose.bones[M(n)], Quaternion(cr, math.radians(e * .5)), rig.P(n))
            if (rig.head_m()["faceP"] - f0) * e < 0:      # ось смотрит не туда — повернуть обратно вдвое
                for n in ("Neck", "Head"):
                    rotate_world(d, d.pose.bones[M(n)], Quaternion(cr, math.radians(-e)), rig.P(n))
    # 8. пальцы: хват левой (рукоять цепи) и правой (якорь / ладонь / кулак) — at_fingers.py
    at_fingers.apply(rig, p.get("gL", 0.0) * W, p.get("gR", 0.0) * W)
    bpy.context.view_layer.update()
    return dict(wrist=wrist, wd=res["Right"]["wd"], wf=res["Right"]["wf"], swivel_twist=sw, head_yaw_fix=round(fix, 1),
                blade_miss=round(max(0.0, res_ang), 1))
