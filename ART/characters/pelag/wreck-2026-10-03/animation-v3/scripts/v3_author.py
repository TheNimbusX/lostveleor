"""Крушение v3: клип Pelag_AN_Wreck2_Swing1 (12 кадров, контакт 7) на риге v6 по пути хвата запечки.

blender -b --factory-startup -P v3_author.py -- <plan.json> <out_dir>
  V3_NOEXPORT=1 — без FBX; V3_QUICK=1 — без пересечений сетки; V3_BODY=<file> — кости тела по кадрам в осях корня Unity
  (формат grip.json, для gripopt: ход рук проверяется от настоящих плеч, капсулы — от настоящего тела).
Тело — v3_keys (слои wk_pose поверх стойки серии сабли), руки — v3_arms (левое гнездо хвата точно в путь, правая на цепи),
пределы — wk_check (как Шквал/Абордаж), выгрузка — как wk_export (привязка стоя, масштаб .01, кадр = тик, без корня).
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from wk_rig import Rig, M, SIDES
from wk_clips import rot
import wk_grip, wk_pose, wk_fingers, wk_check
from wk_sample import measure, big_bone_deltas
import v3_keys, v3_arms

argv = sys.argv[sys.argv.index("--") + 1:]
PLAN, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
CLIP = "Pelag_AN_Wreck2_Swing1"
plan = json.load(open(PLAN, encoding="utf-8"))
S = {round(s["frame"] * 4): s for s in plan["samples"]}
P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
# поправка цели гнезда: перенос Build (Unity) кладёт кисть иначе, чем поза в Blender (замер: экспорт хвата − цель)
CORR = json.load(open(os.environ["V3_CORR"]))["corr"] if os.environ.get("V3_CORR") else [[0.0, 0.0, 0.0]] * 13

rig = Rig()
print("FIST", wk_grip.prepare(rig))
body = Body(rig.mesh)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance_snap = rig.snapshot()
print("STANCE socket", [round(c, 4) for c in v3_arms.to_unity(v3_arms.socket(rig))], "plan f0", S[0]["grip"])
snaps, rows = [], []
hints = {"Left": (0.0, 0.0, 0.0, 0.0), "Right": (0.0, 0.0, 0.0, 0.0)}
# круг поправки: решения рук прошлого круга — подсказка с узким окном (решение не перескакивает от сдвига цели на мм)
PREV = json.load(open(os.environ["V3_SOL"], encoding="utf-8"))["rows"] if os.environ.get("V3_SOL") and os.path.exists(os.environ["V3_SOL"]) else None
for f in range(13):
    if f < 0:
        rig.restore(stance_snap); info = None
    else:
        p = v3_keys.body_at(f)
        wk_pose.body(rig, p)
        poles = {"Left": rot(P_FRONT[0], p["cyaw"]), "Right": rot(P_FRONT[1], p["cyaw"])}
        if f >= 9:     # проводка: левая у бедра — локоть наружу-назад, предплечье не входит в живот
            w_ = min(1.0, (f - 8) / 2.0)
            poles["Left"] = rot(tuple(a + (b - a) * w_ for a, b in zip(P_FRONT[0], (-0.45, 0.80, -0.40))), p["cyaw"])
        target = [g - c for g, c in zip(S[4 * f]["grip"], CORR[f])]
        # после контакта цепь обегает кулак (голова уходит за спину влево) — ось кулака за ней не гонится, рука без перескоков
        v3_arms.AXW[0] = 0.30 if f <= 8 else 0.05
        if PREV and PREV[f].get("sol"):
            hints = {s_: tuple(PREV[f]["sol"][s_]) for s_ in SIDES}
            info = v3_arms.place_hands(rig, target, S[4 * f]["ring"], poles, hints, full=False, narrow=True)
        else:
            info = v3_arms.place_hands(rig, target, S[4 * f]["ring"], poles, hints, full=f == 0)
        hints = {s: info[s][0] for s in SIDES}
        hints["Right"] = info["Right"][0]
        wk_pose.head(rig, p)
        wk_fingers.apply(rig, p["gL"], p["gR"])
        bpy.context.view_layer.update()
    snaps.append(rig.snapshot())
    row = measure(rig, body)
    sock = v3_arms.to_unity(v3_arms.socket(rig))
    row.update(frame=f, socket=[round(c, 4) for c in sock], socket_miss=round(math.dist(sock, [g - c for g, c in zip(S[4 * f]["grip"], CORR[f])]), 4),
               ring=S[4 * f]["ring"], on_grip=True)
    if info: row.update(sol={s: [round(x, 1) for x in info[s][0]] for s in SIDES}, fist_miss={s: round(info[s][1], 3) for s in SIDES},
                        axis_deg={s: round(info[s][2], 1) for s in SIDES})
    rows.append(row)
    print("ROW %2d sockMiss %.3f lean %5.1f pY %6.1f cY %6.1f tw %5.1f gap %.3f fore %s relP %.1f feet L %.3f R %.3f %s" % (
        f, row["socket_miss"], row["lean"], row["pelvis_yaw"], row["chest_yaw"], row["twist"], row["hand_gap"], row["forearm_twist"],
        row["relP"], row["Left"]["toe_z"], row["Right"]["toe_z"], "" if not info else "fist %s ax %s sol %s" % (row["fist_miss"], row["axis_deg"], row["sol"])))

# кадр 1 (снятие): правая ещё идёт от стойки к цепи — половина пути между стойкой и кадром 2 (рука ≤ 70° за тик)
RIGHT_ARM = [n for n in snaps[1] if n.startswith("mixamorig:Right") and not ("UpLeg" in n or "Leg" in n or "Foot" in n or "Toe" in n)]
for n in (RIGHT_ARM if os.environ.get("V3_BLEND1") else []):
    (la, qa), (lb, qb) = stance_snap[n], snaps[2][n]
    if qa.dot(qb) < 0: qb = -qb
    snaps[1][n] = (la.lerp(lb, .5), qa.slerp(qb, .5))
if os.environ.get("V3_BLEND1"): rig.restore(snaps[1]); rows[1].update(measure(rig, body)); rows[1]["on_grip"] = False
w = big_bone_deltas(stance_snap, snaps[0])
print("SEAM stance -> Swing1@0: %.2f° %s" % w)
summary, viol = wk_check.check(rig, body, {CLIP: snaps, "_stance": stance_snap}, {CLIP: rows}, quick=bool(os.environ.get("V3_QUICK")))
wk_check.print_summary(summary, viol)
json.dump(dict(seam_stance_f0=dict(max_bone_deg=round(w[0], 2), bone=w[1]), rows=rows, summary={k: {kk: (str(vv) if not isinstance(vv, (int, float)) else vv) for kk, vv in v.items()} for k, v in summary.items()},
               violations=[[a, b, str(c)] for a, b, c in viol]), open(os.path.join(OUT, CLIP + ".rows.json"), "w", encoding="utf-8"), indent=1)

BODY_BONES = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'HeadTop_End',
              'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1', 'LeftHandIndex1',
              'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1',
              'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End',
              'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End']
if os.environ.get("V3_BODY"):
    per = []
    for snap in snaps:
        rig.restore(snap)
        per.append(dict(bones=[v3_arms.to_unity(rig.P(b)) for b in BODY_BONES], grip=v3_arms.to_unity(v3_arms.socket(rig)),
                        support=v3_arms.to_unity(v3_arms.socket(rig, "Right"))))
    sub = 8; samples = []
    lerp = lambda a, b, u: [x + (y - x) * u for x, y in zip(a, b)]
    for i in range(12 * sub + 1):
        fr = i / sub; f = min(int(fr), 11); u = fr - f
        a, b = per[f], per[f + 1]
        samples.append(dict(t=fr / 30, frame=fr, grip=lerp(a["grip"], b["grip"], u), gripQ=[0, 0, 0, 1], support=lerp(a["support"], b["support"], u),
                            supportQ=[0, 0, 0, 1], spine2Q=[0, 0, 0, 1], bones=[lerp(x, y, u) for x, y in zip(a["bones"], b["bones"])]))
    json.dump(dict(version=1, clip=CLIP, hand="Left", fps=30, sub=sub, frames=12,
                   source=dict(fbx="(blender author, before export)", sha256="", bind="", bindSha256=""),
                   units=dict(boneUnitMetres=1.82, restHeight=1.78), boneNames=BODY_BONES, samples=samples),
              open(os.environ["V3_BODY"], "w", encoding="utf-8"))
    print("BODY written", os.environ["V3_BODY"])

if not os.environ.get("V3_NOEXPORT"):
    dst = rig.dst
    dst.animation_data_create()
    act = bpy.data.actions.new(CLIP); act.use_fake_user = True
    dst.animation_data.action = act
    prev = {}
    for f, snap in enumerate(snaps):
        for pb in dst.pose.bones:
            loc, q = snap[pb.name]; q = q.copy()
            if pb.name in prev and prev[pb.name].dot(q) < 0: q.negate()
            prev[pb.name] = q.copy()
            pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert("location", frame=f, group=pb.name)
            pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    bpy.ops.object.select_all(action='DESELECT')
    dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = rig.export_scale
    bpy.context.view_layer.update()
    sc = bpy.context.scene; sc.frame_start, sc.frame_end = 0, 12
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, CLIP + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.animation_data.action = None
    for pb in dst.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Pelag_AN_Wreck2Bind.fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=False, armature_nodetype='NULL')
    dst.animation_data.action = act
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_Wreck2_Swing1_v3_Work.blend"))
    print("exported", CLIP)
