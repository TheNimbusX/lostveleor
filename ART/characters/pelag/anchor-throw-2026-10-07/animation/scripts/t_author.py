"""Бросок якоря 07.10: Pelag_AN_AnchorThrow2_{Throw, Fly, Yank, Haul, Catch} по листу поз (t_lib — тайминг и кривые).

blender -b --factory-startup -P t_author.py -- <out_dir>
  1. тело: сабля 1 (замах, выпуск) → стойка + кривые веса и корпуса; руки — решатель v3_arms; пальцы; голова;
  2. стопы стоят весь бросок (v4_body.lock_feet без окон шага: опора — точки стойки в тик 0);
  3. замер и проверки (v4_check), FBX по клипам (кадр = тик, ключи на полтика), Pelag_AnchorThrow2_Work.blend,
     <clip>.rows.json, keys.json (тики поз листа).
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t_lib as T
from mathutils import Vector, Quaternion
import v4_lib as L
from wk_rig import Rig, SIDES, M, fl
import wk_grip, wk_fingers, wk_check, v3_arms
import v4_body as VB, v4_check as VC
from wk_sample import measure, big_bone_deltas
from s_lib import Body, translate_world

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True)
PREFIX = "Pelag_AN_AnchorThrow2_"
HK = T.HK

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


# База — сабля 1 в тик 0 (стойка серии, перенос как в Unity), стопы на земле.
B = source_pose(0.0)
_tmp = [dict(B)]
VB.lock_feet(rig, mesh, _tmp, "AnchorThrow2", 0, None, ball_z, kscale=1)
B = _tmp[0]
rig.restore(B)
B_HIPS = rig.P("Hips").copy()
AR, AL = rig.P("RightFoot").copy(), rig.P("LeftFoot").copy()
DIR_W = Vector((AL.x - AR.x, AL.y - AR.y, 0)).normalized()          # вес: от правой (задней) к левой (передней)
DIRF = fl(1, 0, 0)                                                   # бросок — вперёд корня
ARM = {s: rig.ARM[s]["L1"] + rig.ARM[s]["L2"] for s in SIDES}
FIST0 = {s: wk_grip.fist_center(rig, s).copy() for s in SIDES}              # кулаки стойки (концы клипа)
AXIS0 = {s: wk_grip.fist_axis(rig, s).copy() for s in SIDES}
print("BASE hips", tuple(round(x, 3) for x in B_HIPS), "dirw", tuple(round(x, 3) for x in DIR_W), "arm", ARM)


def body_pose(t):
    w = T.ch(T.SRC_W, t)
    snap = T.mix(B, source_pose(T.ch(T.SRC_WARP, min(t, T.SRC_WARP[-1][0]))), w) if w > 0 else dict(B)
    rig.restore(snap)
    hips = rig.dst.pose.bones[M("Hips")]
    want = B_HIPS + DIR_W * T.ch(T.HIP_S, t) + Vector((0, 0, T.ch(T.HIP_DZ, t)))
    translate_world(rig.dst, hips, want - rig.P("Hips"))
    c = 1.0 - w
    if c > 0:
        T.set_pelvis_yaw(rig, T.ch(T.PYAW, t), c)
        T.set_chest_yaw(rig, T.ch(T.CYAW, t), c)
        T.set_lean(rig, T.ch(T.LEAN, t), c)


def reach(s, p):
    """Цель кулака (центр обхвата) не дальше почти прямой руки: 0,97 (плечо + предплечье) + кисть до центра кулака."""
    sh = rig.P(s + "Arm"); v = p - sh
    lim = 0.97 * ARM[s] + Vector(wk_grip.FIST[s]["off"]).length * 0.9
    return sh + v.normalized() * lim if v.length > lim else p


def pole_for(s, target):
    """Локоть: вниз-наружу, а для руки, опущенной вдоль тела (цель под плечом), — назад-наружу; плавно, без скачка
    оси. Возвращает (f, l, u) корня для v3_arms.solve."""
    sg = 1.0 if s == "Left" else -1.0
    d = (target - rig.P(s + "Arm")).normalized()
    def perp(v):
        v = Vector(v).normalized(); return v - d * v.dot(d)
    a_, b_ = perp(fl(0.0, 0.5 * sg, -0.85)), perp(fl(-0.85, 0.45 * sg, 0.0))
    wa = a_.length ** 2
    p = a_ * wa + b_ * (1.0 - wa)
    if p.length < 1e-4: p = perp(fl(0.0, sg, 0.0))
    p.normalize()
    return (-p.y, p.x, p.z)


def arm_bones():
    return [n for n in rig.dst.pose.bones.keys() if any(n.startswith(M(s + x)) for s in SIDES
                                                        for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]


def blend_bones(fk, ik, w, names):
    out = dict(ik)
    for n in names:
        (la, qa), (lb, qb) = fk[n], ik[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    return out


hints = {s: (0.0, 0.0, 0.0, 0.0) for s in SIDES}
first = {s: True for s in SIDES}


def arms_pose(t):
    global hints
    names = arm_bones()
    fk = dict(rig.snapshot())
    info = {}
    lw = T.ch(T.L_IK, t)
    sw = T.ch(T.STANCE_W, t)
    if lw > 0:
        on = T.ch(T.L_ON, t)
        tgt = (rig.P("LeftArm") + fl(*T.ch(T.L_SH, t))).lerp(rig.P("Hips") + fl(*T.ch(T.L_HIP, t)), on)
        tgt = reach("Left", tgt)
        D = DIRF if t > 6 else fl(0.5, 0.0, -0.85).normalized()
        tgt = tgt.lerp(FIST0["Left"], sw); D = D.lerp(AXIS0["Left"] if AXIS0["Left"].dot(D) >= 0 else -AXIS0["Left"], sw).normalized()
        sol, miss, ax = v3_arms.solve(rig, "Left", tgt, D, pole_for("Left", tgt), hints["Left"], full=first["Left"])
        first["Left"] = False; hints["Left"] = sol; info["Left"] = (sol, miss, ax)
    rw = T.ch(T.R_IK, t)
    if rw > 0:
        q = T.ch(T.R_CHAIN, t)
        sh = rig.P("RightArm")
        p_sh = sh + fl(*T.ch(T.R_SH, t))
        axis = Vector(fl(*T.ch(T.R_AXIS, t))).normalized()
        if q > 0:
            G = wk_grip.fist_center(rig, "Left")
            p_ch = G + DIRF * 0.17
            p = p_sh.lerp(p_ch, q); axis = axis.lerp(DIRF, q).normalized()
        else:
            p = p_sh
        p = reach("Right", p).lerp(FIST0["Right"], sw)
        axis = axis.lerp(AXIS0["Right"] if AXIS0["Right"].dot(axis) >= 0 else -AXIS0["Right"], sw).normalized()
        sol, miss, ax = v3_arms.solve(rig, "Right", p, axis, pole_for("Right", p), hints["Right"], full=first["Right"])
        first["Right"] = False; hints["Right"] = sol; info["Right"] = (sol, miss, ax)
    ik = dict(rig.snapshot())
    out = dict(ik)
    for n in names:
        w = lw if "Left" in n else rw
        (la, qa), (lb, qb) = fk[n], ik[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    rig.restore(out)
    return info


snaps, infos = [], []
K = T.N_TICKS * HK
for k in range(K + 1):
    t = k / HK
    body_pose(t)
    info = arms_pose(t)
    wk_fingers.apply(rig, T.ch(T.G_L, t), T.ch(T.G_R, t))
    T.head_look(rig, T.ch(T.HEAD_YAW_W, t), T.ch(T.HEAD_PITCH, t), base_relP)
    bpy.context.view_layer.update()
    snaps.append(dict(rig.snapshot())); infos.append(info)
    if k % HK == 0:
        print("KEY t%5.1f py %6.1f cy %6.1f lean %5.1f hips %s miss %s" % (
            t, rig.pelvis_yaw(), rig.chest_yaw(), T.sagittal_lean(rig.dst), tuple(round(x, 3) for x in rig.P("Hips")),
            {s: round(v[1], 3) for s, v in info.items()}))

# Стопы: подушечка (ToeBase) в своей точке, поворот стопы — как в стойке, лодыжка — IK ноги; правая — шаг назад и обратно.
from s_lib import two_bone
from mathutils import Matrix
rig.restore(snaps[0])
TOE0 = {x: Vector((rig.P(x + "ToeBase").x, rig.P(x + "ToeBase").y, ball_z[x])) for x in SIDES}
FROT0 = {x: (rig.dst.matrix_world @ rig.dst.pose.bones[M(x + "Foot")].matrix).to_3x3().normalized() for x in SIDES}
BACK = -DIR_W * T.R_STEP_BACK


def toe_target(x, t):
    if x == "Left": return TOE0["Left"]
    u = T.ch(T.R_STEP, t)
    p = TOE0["Right"] + BACK * u
    for a, b in T.R_AIR:
        if a < t < b:
            v = (t - a) / (b - a)
            p = p + Vector((0, 0, (0.05 + 0.1 * T.R_STEP_BACK) * math.sin(math.pi * v)))
    return p


for k in range(K + 1):
    t = k / HK
    rig.restore(snaps[k])
    for x in SIDES:
        fb = rig.dst.pose.bones[M(x + "Foot")]
        hd = (rig.dst.matrix_world @ fb.matrix).translation.copy()
        fb.matrix = rig.dst.matrix_world.inverted() @ (Matrix.Translation(hd) @ FROT0[x].to_4x4())
        bpy.context.view_layer.update()
        tgt = toe_target(x, t)
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
    rig.restore(snaps[k]); row = measure(rig, body); row.update(tick=k // HK); rows.append(row)
STRIKE = (1, 4)
ticks = snaps[::HK]
summary, viol = VC.check(rig, body, PREFIX + "Master", ticks, rows, STRIKE, stance0, sub_snaps=snaps)
viol = [v for v in viol if not v[1].startswith("наклон назад")]      # рывок — откидка назад по листу (поза 5)
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


def key_action(name, ks):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    dst.animation_data.action = act; prev = {}
    for i, k in enumerate(ks):
        f = i / HK; snap = snaps[k]
        for pb in dst.pose.bones:
            loc, q = snap[pb.name]; q = q.copy()
            if pb.name in prev and prev[pb.name].dot(q) < 0: q.negate()
            prev[pb.name] = q.copy(); pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert("location", frame=f, group=pb.name); pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    return act


key_action("AnchorThrow2_Master", list(range(K + 1)))
for name, a, b in T.CLIPS:
    act = key_action(PREFIX + name, list(range(a * HK, b * HK + 1)))
    bpy.ops.object.select_all(action='DESELECT'); dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = rig.export_scale; bpy.context.view_layer.update()
    sc.frame_start, sc.frame_end = 0, b - a
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, PREFIX + name + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=0.5, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.scale = (1, 1, 1); bpy.context.view_layer.update()
    print("EXPORTED", name, a, b)
dst.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_AnchorThrow2_Work.blend"))
import shutil
BIND_SRC = os.path.join(T.PEL, "wreck-2026-10-03", "animation-v4", "clips", "Pelag_AN_Wreck4Bind.fbx")
shutil.copyfile(BIND_SRC, os.path.join(OUT, "Pelag_AN_AnchorThrow2Bind.fbx"))       # тот же риг v6 стоя (Rig() как у Крушения v4)
print("AUTHORED violations", len(viol))
