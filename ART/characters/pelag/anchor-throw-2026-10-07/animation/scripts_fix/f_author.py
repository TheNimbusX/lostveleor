"""Бросок якоря, правка анимации (animfix): Pelag_AN_AnchorThrow2_{Throw, Fly, Yank, Haul, Catch} заново по f_lib.

blender -b --factory-startup -P f_author.py -- <out_dir> <measure.json>
  measure.json — замер Unity (artifacts/throw-0710/animfix/measure_before.json): позы KnifeIdle (вход) и MX_Idle (выход).
  1. тело: сабля 1 (замах, выпуск) → стойка серии + кривые веса; вход — из KnifeIdle, выход — в «готов» (MX_Idle);
  2. руки — решатель v3_arms; пальцы; голова — взгляд в мире на линию;
  3. стопы: левая стоит весь бросок, правая — шаг назад на выпуске и подшаг к левой после ловли;
  4. ключи 4 на тик, FBX 120 к/с (длина клипа в секундах та же), замер и проверки, Work.blend.
"""
import bpy, sys, os, json, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import t_lib as T
import f_lib as F
import f_unitypose as UP
from mathutils import Vector, Quaternion, Matrix
import v4_lib as L
from wk_rig import Rig, SIDES, M, fl
import wk_grip, wk_fingers, wk_check, v3_arms
import v4_body as VB, v4_check as VC
from wk_sample import measure
from s_lib import Body, translate_world, rotate_world, two_bone, sagittal_lean

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True)
MEAS = argv[1]
PREFIX = "Pelag_AN_AnchorThrow2_"
HK = F.HK

rig = Rig()
print("FIST", wk_grip.prepare(rig))
VB.v3s4_patch.RIG_[0] = rig
VC.install_root(rig)
body = Body(rig.mesh); mesh = wk_check.Mesh(rig, body)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance0 = dict(rig.snapshot()); base_relP = rig.head_m()["relP"]
ank0 = min(rig.P(s + "Foot").z for s in SIDES)
ball_z = {s: rig.P(s + "ToeBase").z for s in SIDES}
src = L.SabreSource(rig.dst)
src.apply(T.SRC, 0.0)
DZ = max(-0.095, ank0 - max(rig.P(s + "Foot").z for s in SIDES))
print("GROUND dz %.3f" % DZ)


def source_pose(tick):
    rig.dst.location = (0, 0, 0)
    rig.reset()
    src.apply(T.SRC, tick)
    translate_world(rig.dst, rig.dst.pose.bones[M("Hips")], Vector((0, 0, DZ)))
    wk_fingers.apply(rig, 0.0, 0.0)
    return dict(rig.snapshot())


B = source_pose(0.0)
_tmp = [dict(B)]
VB.lock_feet(rig, mesh, _tmp, "AnchorThrow2", 0, None, ball_z, kscale=1)
B = _tmp[0]
rig.restore(B)
B_HIPS = rig.P("Hips").copy()
AR, AL = rig.P("RightFoot").copy(), rig.P("LeftFoot").copy()
DIR_W = Vector((AL.x - AR.x, AL.y - AR.y, 0)).normalized()
DIRF = fl(1, 0, 0)
ARM = {s: rig.ARM[s]["L1"] + rig.ARM[s]["L2"] for s in SIDES}
TOE_B = {s: Vector((rig.P(s + "ToeBase").x, rig.P(s + "ToeBase").y, ball_z[s])) for s in SIDES}
FROT_B = {s: (rig.dst.matrix_world @ rig.dst.pose.bones[M(s + "Foot")].matrix).to_3x3().normalized() for s in SIDES}
GROUND = min(ball_z.values())

# ---------------------------------------------------------------- позы игры: вход и выход ---
data = UP.load(MEAS)
ARM_NAMES = [n for n in rig.dst.pose.bones.keys() if any(n.startswith(M(s + x)) for s in SIDES for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]
FING = [n for n in ARM_NAMES if any(f in n for f in ("Thumb", "Index", "Middle", "Ring", "Pinky"))]
BODY_NAMES = [n for n in rig.dst.pose.bones.keys() if n not in ARM_NAMES]


def game_pose(name, place):
    """Поза Unity → риг; place(rig) двигает таз по горизонтали (стопы ставит проход стоп)."""
    rig.reset()
    r = data["ref"][name]
    UP.apply(rig, data, r["wq"], r["p"], ground_z=GROUND)
    place()
    bpy.context.view_layer.update()
    toe = {s: Vector((rig.P(s + "ToeBase").x, rig.P(s + "ToeBase").y, ball_z[s])) for s in SIDES}
    frot = {s: (rig.dst.matrix_world @ rig.dst.pose.bones[M(s + "Foot")].matrix).to_3x3().normalized() for s in SIDES}
    return dict(rig.snapshot()), toe, frot


def place_knife():
    d = TOE_B["Left"] - rig.P("LeftToeBase"); d.z = 0
    translate_world(rig.dst, rig.dst.pose.bones[M("Hips")], d)


READY_RT_W = TOE_B["Left"] + fl(F.READY_RT[0], F.READY_RT[1], 0.0)
READY_RT_W.z = ball_z["Right"]


def place_ready():
    mid_game = (rig.P("LeftToeBase") + rig.P("RightToeBase")) * 0.5
    mid_want = (TOE_B["Left"] + READY_RT_W) * 0.5
    d = mid_want - mid_game; d.z = -0.012        # колени чуть согнуты: прямая нога на решателе стопы щёлкает
    translate_world(rig.dst, rig.dst.pose.bones[M("Hips")], d)


KNIFE, TOE_K, FROT_K = game_pose("KnifeIdle", place_knife)
_rel = wk_grip.fist_center(rig, "Left") - rig.P("Hips")
F.L_HIP = [(0.0, (-_rel.y, _rel.x, _rel.z))] + F.L_HIP0
print("KNIFE left fist from hips (f, l, u)", tuple(round(x, 3) for x in F.L_HIP[0][1]))
READY, TOE_R, FROT_R = game_pose("MXIdle", place_ready)


def _root_axes(v):
    return (-v.y, v.x, v.z)


def _fill(knots, val):
    return [(k, val if v is None else v) for k, v in knots]


# Конец: кулаки и оси кулаков позы «готов» (MX_Idle) — от плечевых суставов в осях корня.
_rf = _root_axes(wk_grip.fist_center(rig, "Right") - rig.P("RightArm")); _ra = _root_axes(wk_grip.fist_axis(rig, "Right"))
_lf = _root_axes(wk_grip.fist_center(rig, "Left") - rig.P("LeftArm")); _la = _root_axes(wk_grip.fist_axis(rig, "Left"))
F.R_SH = _fill(F.R_SH, _rf); F.R_AXIS = _fill(F.R_AXIS, _ra); F.L_SH = _fill(F.L_SH, _lf); F.L_AXIS = _fill(F.L_AXIS, _la)
print("READY fists R", tuple(round(x, 3) for x in _rf), tuple(round(x, 2) for x in _ra), "L", tuple(round(x, 3) for x in _lf), tuple(round(x, 2) for x in _la))
print("KNIFE hips", tuple(round(x, 3) for x in KNIFE[M("Hips")][0]), "toeR", tuple(round(x, 3) for x in TOE_K["Right"]),
      "READY toeR(game)", tuple(round(x, 3) for x in TOE_R["Right"]), "want", tuple(round(x, 3) for x in READY_RT_W))


def mix_names(a, b, w, names):
    if w <= 0.0: return dict(a)
    out = dict(a)
    for n in names:
        (la, qa), (lb, qb) = a[n], b[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w)) if w < 1.0 else (lb.copy(), qb.copy())
    return out


def blend_names(base, other, w, names):
    """base, где кости names — к other с весом w (остальное от base)."""
    return mix_names(base, other, w, names)


def body_pose(t):
    w = T.ch(F.SRC_W, t)
    snap = T.mix(B, source_pose(T.ch(F.SRC_WARP, min(t, F.SRC_WARP[-1][0]))), w) if w > 0 else dict(B)
    rig.restore(snap)
    hips = rig.dst.pose.bones[M("Hips")]
    want = B_HIPS + DIR_W * T.ch(F.HIP_S, t) + Vector((0, 0, T.ch(F.HIP_DZ, t)))
    translate_world(rig.dst, hips, want - rig.P("Hips"))
    c = 1.0 - w
    if c > 0:
        T.set_pelvis_yaw(rig, T.ch(F.PYAW, t), c)
        T.set_chest_yaw(rig, T.ch(F.CYAW, t), c)
        T.set_lean(rig, T.ch(F.LEAN, t), c)
    snap = dict(rig.snapshot())
    e = F.sstep(*F.ENTRY, t); r = F.sstep(*F.READY, t)
    if e < 1.0: snap = blend_names(snap, KNIFE, 1.0 - e, BODY_NAMES)
    if r > 0.0: snap = mix_names(snap, READY, r, BODY_NAMES)
    rig.restore(snap)


def reach(s, p):
    sh = rig.P(s + "Arm"); v = p - sh
    lim = 0.97 * ARM[s] + Vector(wk_grip.FIST[s]["off"]).length * 0.9
    return sh + v.normalized() * lim if v.length > lim else p


def pole_for(s, target, up=0.0):
    sg = 1.0 if s == "Left" else -1.0
    d = (target - rig.P(s + "Arm")).normalized()
    def perp(v):
        v = Vector(v).normalized(); return v - d * v.dot(d)
    a_, b_ = perp(fl(0.0, 0.5 * sg, -0.85)), perp(fl(-0.85, 0.45 * sg, 0.0))
    wa = a_.length ** 2
    p = a_ * wa + b_ * (1.0 - wa)
    if up > 0:
        u_ = perp(fl(-0.2, 0.9 * sg, 0.45))         # закидка за плечо: локоть вверх-наружу
        if u_.length > 1e-4: p = p.normalized().lerp(u_.normalized(), up)
    if p.length < 1e-4: p = perp(fl(0.0, sg, 0.0))
    p.normalize()
    return (-p.y, p.x, p.z)


hints = {s: (0.0, 0.0, 0.0, 0.0) for s in SIDES}
first = {s: True for s in SIDES}


def arms_pose(t):
    fk = dict(rig.snapshot())
    info = {}
    on = T.ch(F.L_ON, t)
    tgt = (rig.P("LeftArm") + fl(*T.ch(F.L_SH, t))).lerp(rig.P("Hips") + fl(*T.ch(F.L_HIP, t)), on)
    tgt = reach("Left", tgt)
    D = DIRF if on > 0.5 else Vector(fl(*T.ch(F.L_AXIS, t))).normalized()
    sol, miss, ax = v3_arms.solve(rig, "Left", tgt, D, pole_for("Left", tgt), hints["Left"], full=first["Left"])
    first["Left"] = False; hints["Left"] = sol; info["Left"] = (sol, miss, ax)
    q = T.ch(F.R_CHAIN, t)
    p_sh = rig.P("RightArm") + fl(*T.ch(F.R_SH, t))
    axis = Vector(fl(*T.ch(F.R_AXIS, t))).normalized()
    if q > 0:
        G = wk_grip.fist_center(rig, "Left")
        p_ch = G + DIRF * T.ch(F.CHAIN_GAP, t)
        p = p_sh.lerp(p_ch, q); axis = axis.lerp(DIRF, q).normalized()
    else:
        p = p_sh
    p = reach("Right", p)
    # Якорь в кулаке лежит вдоль оси «мизинец → указательный» венцом по ней: знак оси важен на замахе (венец вниз) и с
    # ловли (венец вверх, через плечо) — обратный хват (большой палец вниз) дорогой; на цепи знак не важен.
    wk_grip.REVERSE = 200.0 if (t >= 15.0 or t < 2.0) else 6.0
    sol, miss, ax = v3_arms.solve(rig, "Right", p, axis, pole_for("Right", p, T.ch(F.R_POLE_UP, t)), hints["Right"], full=first["Right"])
    first["Right"] = False; hints["Right"] = sol; info["Right"] = (sol, miss, ax)
    wk_grip.REVERSE = 6.0
    ik = dict(rig.snapshot())
    for s, win in (("Right", F.ENTRY_ARMS), ("Left", F.ENTRY_ARM_L)):
        ea = F.sstep(*win, t)
        if ea < 1.0: ik = blend_names(ik, KNIFE, 1.0 - ea, [n for n in ARM_NAMES if s in n and n not in FING])
    for s, win in (("Right", F.READY_ARMS), ("Left", F.READY_ARM_L)):
        names = [n for n in ARM_NAMES if s in n and n not in FING]
        if t < win[0]: continue
        if s not in FROZEN: FROZEN[s] = {n: (ik[n][0].copy(), ik[n][1].copy()) for n in names}
        ik = blend_names(ik, FROZEN[s], 1.0, names)               # рука из позы в миг отпускания (локально к плечу) —
        ik = mix_names(ik, READY, F.sstep(*win, t), names)        # FK-дугой в «готов», без решателя (без перекрута оси)
    rig.restore(ik)
    return info


FROZEN = {}


def fingers(t):
    wk_fingers.apply(rig, T.ch(F.G_L, t), T.ch(F.G_R, t))
    bpy.context.view_layer.update()
    snap = dict(rig.snapshot())
    for s, win in (("Right", F.ENTRY_ARMS), ("Left", F.ENTRY_ARM_L)):
        ea = F.sstep(*win, t)
        if ea < 1.0: snap = blend_names(snap, KNIFE, 1.0 - ea, [n for n in FING if s in n])
    for s, win in (("Right", F.READY_ARMS), ("Left", F.READY_ARM_L)):
        ra = F.sstep(*win, t)
        if ra > 0: snap = mix_names(snap, READY, ra, [n for n in FING if s in n])
    rig.restore(snap)


def _frame_matrix(f, u, r):
    return Matrix((f, u, r)).transposed()          # столбцы — вперёд, вверх, вправо


def head_gaze(t):
    """Голова в МИРЕ: лицо на линию броска (рысканье к 0 с весом HEAD_YAW_W, наклон лица FACE_P), макушка — к вертикали
    (без завала набок), шея берёт 45 % поворота, голова — остальное; от «головы, как её несёт корпус» — не больше
    HEAD_MAX_TURN. Вес — 1 внутри броска, 0 на концах (там голова позы игры)."""
    g = F.sstep(*F.ENTRY, t) * (1.0 - F.sstep(*F.READY, t))
    if g <= 0: return
    d = rig.dst
    hf, hu, hr = rig.hframe("Head")
    yaw_now = math.degrees(math.atan2(hf.x, -hf.y))             # 0 — вперёд корня (−Y), «+» — к левому боку
    yaw = yaw_now * (1.0 - T.ch(F.HEAD_YAW_W, t))
    pitch = math.radians(T.ch(F.FACE_P, t))
    a = math.radians(yaw)
    f = Vector((math.sin(a) * math.cos(pitch), -math.cos(a) * math.cos(pitch), math.sin(pitch)))
    u = Vector((0, 0, 1)); u = (u - f * u.dot(f)).normalized()
    r = f.cross(u).normalized()
    Q = (_frame_matrix(f, u, r) @ _frame_matrix(hf, hu, hr).transposed()).to_quaternion()
    ang = math.degrees(Q.angle)
    lim = F.HEAD_MAX_TURN
    w = g * (min(1.0, lim / ang) if ang > 1e-3 else 1.0)
    full = Quaternion().slerp(Q, w)
    neck = Quaternion().slerp(full, 0.45)
    rotate_world(d, d.pose.bones[M("Neck")], neck, rig.P("Neck"))
    bpy.context.view_layer.update()
    hf, hu, hr = rig.hframe("Head")
    Q2 = (_frame_matrix(f, u, r) @ _frame_matrix(hf, hu, hr).transposed()).to_quaternion()
    rest = full @ neck.inverted()
    # голова доводит до цели не дальше остатка предела
    a2 = math.degrees(Q2.angle); ar = math.degrees(rest.angle)
    q_head = Quaternion().slerp(Q2, min(1.0, ar / a2) if a2 > 1e-3 else 1.0)
    rotate_world(d, d.pose.bones[M("Head")], q_head, rig.P("Head"))
    bpy.context.view_layer.update()


snaps, infos = [], []
K = F.N_TICKS * HK
for k in range(K + 1):
    t = k / HK
    body_pose(t)
    info = arms_pose(t)
    fingers(t)
    head_gaze(t)
    bpy.context.view_layer.update()
    snaps.append(dict(rig.snapshot())); infos.append(info)
    if k % HK == 0:
        hm = rig.head_m()
        _hf, _hu, _hr = rig.hframe("Head")
        print("KEY t%5.1f py %6.1f cy %6.1f lean %5.1f faceP %5.1f relP %5.1f headUpZ %.2f hips %s miss %s" % (
            t, rig.pelvis_yaw(), rig.chest_yaw(), sagittal_lean(rig.dst), hm["faceP"], hm["relP"], _hu.z,
            tuple(round(x, 3) for x in rig.P("Hips")), {s: round(v[1], 3) for s, v in info.items()}))

# ---------------------------------------------------------------- сглаживание рук ---
# Решатель руки ищет в окне вокруг прошлого кадра: на перехвате (отпустил цепь → ловля, закидка) локоть иногда
# переходит на соседнее решение за ключ — пик скорости кости. Ядро 1-2-1 по ключам 120 к/с, два прохода; хлёст
# выпуска (1,2–2,3) не трогается — там резкость нужна.
SMOOTH_ARMS = [n for n in ARM_NAMES if n not in FING]
for _ in range(2):
    new = [dict(sn) for sn in snaps]
    for k in range(1, K):
        t = k / HK
        if 1.2 <= t <= 2.3: continue
        for n in SMOOTH_ARMS:
            q0, q1, q2 = snaps[k - 1][n][1], snaps[k][n][1], snaps[k + 1][n][1]
            a = q0 if q0.dot(q1) >= 0 else -q0
            c = q2 if q2.dot(q1) >= 0 else -q2
            q = Quaternion((0.25 * a.w + 0.5 * q1.w + 0.25 * c.w, 0.25 * a.x + 0.5 * q1.x + 0.25 * c.x,
                            0.25 * a.y + 0.5 * q1.y + 0.25 * c.y, 0.25 * a.z + 0.5 * q1.z + 0.25 * c.z)).normalized()
            new[k][n] = (snaps[k][n][0], q)
    snaps = new

# ---------------------------------------------------------------- стопы ---
BACK = -DIR_W * F.R_STEP_BACK


def qslerp(a, b, u):
    qa, qb = a.to_quaternion(), b.to_quaternion()
    if qa.dot(qb) < 0: qb = -qb
    return qa.slerp(qb, u).to_matrix()


def foot_target(x, t):
    """Подушечка (ToeBase) и поворот стопы в мире на тик t."""
    if x == "Left":
        rot = qslerp(FROT_K["Left"], FROT_B["Left"], F.sstep(0.0, 1.2, t))
        return TOE_B["Left"], rot
    a, b = F.STEP_BACK; c, d = F.STEP_IN
    p0 = Vector((TOE_K["Right"].x, TOE_K["Right"].y, ball_z["Right"]))
    p1 = TOE_B["Right"] + BACK
    if t <= b:
        u = F.sstep(a, b, t)
        p = p0.lerp(p1, u); rot = qslerp(FROT_K["Right"], FROT_B["Right"], u)
        if a < t < b: p = p + Vector((0, 0, (0.05 + 0.1 * (p1 - p0).length) * math.sin(math.pi * (t - a) / (b - a))))
        return p, rot
    u = F.sstep(c, d, t)
    p = p1.lerp(READY_RT_W, u); rot = qslerp(FROT_B["Right"], FROT_R["Right"], u)
    if c < t < d: p = p + Vector((0, 0, (0.05 + 0.1 * (READY_RT_W - p1).length) * math.sin(math.pi * (t - c) / (d - c))))
    return p, rot


for k in range(K + 1):
    t = k / HK
    rig.restore(snaps[k])
    for x in SIDES:
        tgt, rot = foot_target(x, t)
        fb = rig.dst.pose.bones[M(x + "Foot")]
        hd = (rig.dst.matrix_world @ fb.matrix).translation.copy()
        fb.matrix = rig.dst.matrix_world.inverted() @ (Matrix.Translation(hd) @ rot.to_4x4())
        bpy.context.view_layer.update()
        ankle = rig.P(x + "Foot") + (tgt - rig.P(x + "ToeBase"))
        for _ in range(2):
            two_bone(rig.dst, [M(x + "UpLeg"), M(x + "Leg"), M(x + "Foot")], ankle, keep_end=True)
            ankle = ankle + (tgt - rig.P(x + "ToeBase"))
    snaps[k] = dict(rig.snapshot())
    vs = mesh.at(snaps[k]); low = mesh.feet_low(vs)
    for x in SIDES:
        if low[x] < 0.0:
            two_bone(rig.dst, [M(x + "UpLeg"), M(x + "Leg"), M(x + "Foot")], rig.P(x + "Foot") + Vector((0, 0, -low[x] + 0.002)), keep_end=True)
    snaps[k] = dict(rig.snapshot())

# ---------------------------------------------------------------- замер и проверки ---
rows = []
for k in range(0, K + 1, HK):
    rig.restore(snaps[k]); row = measure(rig, body); row.update(tick=k // HK); hm = rig.head_m()
    row.update(faceP=hm["faceP"]); rows.append(row)
ticks = snaps[::HK]
summary, viol = VC.check(rig, body, PREFIX + "Master", ticks, rows, (1, 4), stance0, sub_snaps=snaps)
viol = [v for v in viol if not v[1].startswith("наклон назад") and not v[1].startswith("голова не за грудью")]
print("VIOLATIONS", len(viol))
for v in viol: print("  V", v[1], v[2])

# ---------------------------------------------------------------- выгрузка ---
json.dump(dict(rows=rows, summary={k: str(v) for k, v in summary.items()}, violations=[[a, b, str(x)] for a, b, x in viol]),
          open(os.path.join(OUT, "AnchorThrow2.rows.json"), "w", encoding="utf-8"), indent=1)
dst = rig.dst; dst.animation_data_create(); dst.location = (0, 0, 0)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE' and o is not dst: bpy.data.objects.remove(o, do_unlink=True)
sc = bpy.context.scene
sc.render.fps = 30 * HK


def key_action(name, ks):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    dst.animation_data.action = act; prev = {}
    for i, k in enumerate(ks):
        snap = snaps[k]
        for pb in dst.pose.bones:
            loc, q = snap[pb.name]; q = q.copy()
            if pb.name in prev and prev[pb.name].dot(q) < 0: q.negate()
            prev[pb.name] = q.copy(); pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert("location", frame=i, group=pb.name); pb.keyframe_insert("rotation_quaternion", frame=i, group=pb.name)
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    return act


key_action("AnchorThrow2_Master", list(range(K + 1)))
for name, a, b in F.CLIPS:
    act = key_action(PREFIX + name, list(range(a * HK, b * HK + 1)))
    bpy.ops.object.select_all(action='DESELECT'); dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = rig.export_scale; bpy.context.view_layer.update()
    sc.frame_start, sc.frame_end = 0, (b - a) * HK
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, PREFIX + name + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.scale = (1, 1, 1); bpy.context.view_layer.update()
    print("EXPORTED", name, a, b)
dst.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_AnchorThrow2_Work.blend"))
import shutil
BIND_SRC = os.path.join(T.PEL, "wreck-2026-10-03", "animation-v4", "clips", "Pelag_AN_Wreck4Bind.fbx")
shutil.copyfile(BIND_SRC, os.path.join(OUT, "Pelag_AN_AnchorThrow2Bind.fbx"))
print("AUTHORED violations", len(viol))
