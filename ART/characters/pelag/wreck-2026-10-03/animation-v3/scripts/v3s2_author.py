"""Крушение v3, клипы после Swing1 (Wait1; Swing2 — когда будет решение по тикам): тело по модулю ключей, руки на путь хвата.

blender -b --factory-startup -P v3s2_author.py -- <keys module> <path.json> <out_dir>
  path.json — {"frames": [[x,y,z] ...]} путь гнезда хвата в осях корня Unity (gripopt_s2), по кадру клипа.
  V3S2_CORR=<corr.json> — поправка цели гнезда (экспорт хвата − план, круги v3s2_corr.py); V3S2_SOL=<rows.json> — решения
  рук прошлого круга (узкое окно); V3S2_NOEXPORT=1 — без FBX; V3S2_QUICK=1 — без пересечений сетки.
Кадры-копии (стык, замыкание петли) берутся ключами из рабочего .blend принятого клипа (Swing1 не трогается);
привязка Pelag_AN_Wreck2Bind.fbx не перезаписывается (та же, что у Swing1). Пределы — wk_check, как у Swing1.
"""
import bpy, sys, os, json, math, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
from s_lib import Body
from wk_rig import Rig, SIDES
import wk_grip, wk_pose, wk_fingers, wk_check
from wk_sample import measure, big_bone_deltas
import v3_arms

argv = sys.argv[sys.argv.index("--") + 1:]
KM = importlib.import_module(argv[0])
PATH, OUT = argv[1], argv[2]
V3 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
os.makedirs(OUT, exist_ok=True)
CLIP, N = KM.CLIP, KM.N
G = json.load(open(PATH, encoding="utf-8"))["frames"]
CORR = json.load(open(os.environ["V3S2_CORR"]))["corr"] if os.environ.get("V3S2_CORR") else [[0.0, 0.0, 0.0]] * (N + 1)
PREV = None
if os.environ.get("V3S2_SOL") and os.path.exists(os.environ["V3S2_SOL"]):
    PREV = json.load(open(os.environ["V3S2_SOL"], encoding="utf-8"))["rows"]

rig = Rig()
print("FIST", wk_grip.prepare(rig))
body = Body(rig.mesh)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance_snap = rig.snapshot()


def action_snapshot(clip, frame):
    """Поза кадра принятого клипа: ключи из его рабочего .blend (те же кости рига, линейные ключи — точный кадр)."""
    blend = os.path.join(V3, "Pelag_Wreck2_%s_v3_Work.blend" % clip.replace("Pelag_AN_Wreck2_", ""))
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        dst.actions = [clip]
    act = bpy.data.actions[clip]
    snap = {k: (v[0].copy(), v[1].copy()) for k, v in rig.snapshot().items()}
    vals = {}
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    vals[(fc.data_path, fc.array_index)] = fc.evaluate(frame)
    for name in snap:
        loc = Vector([vals.get(('pose.bones["%s"].location' % name, i), snap[name][0][i]) for i in range(3)])
        q = Quaternion([vals.get(('pose.bones["%s"].rotation_quaternion' % name, i), snap[name][1][i]) for i in range(4)]).normalized()
        snap[name] = (loc, q)
    bpy.data.actions.remove(act)
    return snap


seam_snaps = {f: action_snapshot(*src) for f, src in KM.SEAM.items()}
hrows = json.load(open(os.path.join(V3, KM.HINT_FROM[0] + ".rows.json"), encoding="utf-8"))["rows"]
hints = {s: tuple(hrows[KM.HINT_FROM[1]]["sol"][s]) for s in SIDES}
# ось рукояти на стыке: из кадра-копии (левый кулак → правый кулак), в осях Unity
rig.restore(seam_snaps[min(KM.SEAM)])
fL, fR = wk_grip.fist_center(rig, "Left"), wk_grip.fist_center(rig, "Right")
BASE_AX = v3_arms.to_unity(rig.P("Hips") + (fR - fL).normalized()) ; o = v3_arms.to_unity(rig.P("Hips"))
BASE_AX = [a - b for a, b in zip(BASE_AX, o)]
print("seam handle axis (Unity)", [round(c, 3) for c in BASE_AX], "gap %.3f" % (fR - fL).length)

snaps, rows = [], []
for f in range(N + 1):
    info = None
    if f in seam_snaps:
        rig.restore(seam_snaps[f])
    else:
        p = KM.body_at(f)
        wk_pose.body(rig, p)
        if hasattr(KM, "post_body"): KM.post_body(rig, p)
        target = [g - c for g, c in zip(G[f], CORR[f])]
        rs = v3_arms.to_unity(rig.P("RightArm")); d_ = [r_ - t_ for r_, t_ in zip(rs, target)]; n_ = math.sqrt(sum(c * c for c in d_))
        nb = math.sqrt(sum(c * c for c in BASE_AX))
        hd = KM.handle_dir(f, [c / nb for c in BASE_AX], [c / n_ for c in d_])
        ring = [t + 1.6 * c for t, c in zip(target, hd)]          # «кольцо» по оси рукояти: ось кулаков, а не живой цепи
        v3_arms.AXW[0] = KM.AXW; v3_arms.GAP = KM.GAP
        if PREV and PREV[f].get("sol"):
            h = {s_: tuple(PREV[f]["sol"][s_]) for s_ in SIDES}
            info = v3_arms.place_hands(rig, target, ring, KM.poles(f, p), h, full=False, narrow=True)
        else:
            info = v3_arms.place_hands(rig, target, ring, KM.poles(f, p), hints, full=False, narrow=False)
        hints = {s: info[s][0] for s in SIDES}
        wk_pose.head(rig, p)
        wk_fingers.apply(rig, p["gL"], p["gR"])
        bpy.context.view_layer.update()
    snaps.append(rig.snapshot())
    row = measure(rig, body)
    sock = v3_arms.to_unity(v3_arms.socket(rig))
    row.update(frame=f, socket=[round(c, 4) for c in sock], on_grip=True, seam_copy=f in seam_snaps,
               socket_miss=round(math.dist(sock, [g - c for g, c in zip(G[f], CORR[f])]), 4))
    if info: row.update(sol={s: [round(x, 1) for x in info[s][0]] for s in SIDES}, fist_miss={s: round(info[s][1], 3) for s in SIDES},
                        axis_deg={s: round(info[s][2], 1) for s in SIDES})
    else: row.update(sol={s: list(hints[s]) for s in SIDES})
    rows.append(row)
    print("ROW %2d sockMiss %.3f lean %5.1f pY %6.1f cY %6.1f tw %5.1f gap %.3f fore %s relP %.1f feet L %.4f R %.4f %s" % (
        f, row["socket_miss"], row["lean"], row["pelvis_yaw"], row["chest_yaw"], row["twist"], row["hand_gap"], row["forearm_twist"],
        row["relP"], row["Left"]["toe_z"], row["Right"]["toe_z"], "seam copy" if f in seam_snaps else "sol %s" % row["sol"]))

SM = getattr(KM, "SMOOTH", None)
if SM:   # правая рука на рукояти: сглаживание по времени (петля и стыки без рывков предплечья), зазор кистей — перемер
    bones, passes, wsm = SM
    names = ["mixamorig:" + b for b in bones]
    for _ in range(passes):
        new = [dict(x) for x in snaps]
        for f in range(1, N):
            if f in seam_snaps: continue
            for n in names:
                qa, qb, qc = snaps[f - 1][n][1], snaps[f + 1][n][1].copy(), snaps[f][n][1].copy()
                if qa.dot(qb) < 0: qb.negate()
                mid = qa.slerp(qb, .5)
                if qc.dot(mid) < 0: qc.negate()
                new[f][n] = (snaps[f][n][0], qc.slerp(mid, wsm))
        snaps = new
    for f in range(1, N):
        if f in seam_snaps: continue
        rig.restore(snaps[f])
        keep = {k: rows[f][k] for k in ("frame", "socket", "on_grip", "seam_copy", "socket_miss", "sol", "fist_miss", "axis_deg") if k in rows[f]}
        rows[f] = measure(rig, body); rows[f].update(keep)
    print("SMOOTH", bones, "gaps", [rows[f]["hand_gap"] for f in range(N + 1)])

seams = {}
for f in seam_snaps:
    nb = f + 1 if f + 1 <= N else f - 1
    seams["f%d_vs_f%d" % (f, nb)] = big_bone_deltas(snaps[f], snaps[nb])
print("SEAM neighbours", {k: (round(v[0], 1), v[1]) for k, v in seams.items()})
summary, viol = wk_check.check(rig, body, {CLIP: snaps, "_stance": stance_snap}, {CLIP: rows}, quick=bool(os.environ.get("V3S2_QUICK")))
wk_check.print_summary(summary, viol)
json.dump(dict(rows=rows, seams={k: [round(v[0], 2), v[1]] for k, v in seams.items()},
               summary={k: {kk: (str(vv) if not isinstance(vv, (int, float)) else vv) for kk, vv in v.items()} for k, v in summary.items()},
               violations=[[a, b, str(c)] for a, b, c in viol]), open(os.path.join(OUT, CLIP + ".rows.json"), "w", encoding="utf-8"), indent=1)

BODY_BONES = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'HeadTop_End',
              'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1', 'LeftHandIndex1',
              'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1',
              'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End',
              'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End']
if os.environ.get("V3S2_BODY"):     # кости по кадрам в осях корня Unity (формат grip.json) — тело для gripopt_s2
    per = []
    for snap in snaps:
        rig.restore(snap)
        per.append(dict(bones=[v3_arms.to_unity(rig.P(b)) for b in BODY_BONES], grip=v3_arms.to_unity(v3_arms.socket(rig)),
                        support=v3_arms.to_unity(v3_arms.socket(rig, "Right"))))
    sub, samples = 8, []
    lerp = lambda a, b, u: [x + (y - x) * u for x, y in zip(a, b)]
    for i in range(N * sub + 1):
        fr = i / sub; f = min(int(fr), N - 1); u = fr - f
        a, b = per[f], per[f + 1]
        samples.append(dict(t=fr / 30, frame=fr, grip=lerp(a["grip"], b["grip"], u), gripQ=[0, 0, 0, 1], support=lerp(a["support"], b["support"], u),
                            supportQ=[0, 0, 0, 1], spine2Q=[0, 0, 0, 1], bones=[lerp(x, y, u) for x, y in zip(a["bones"], b["bones"])]))
    json.dump(dict(version=1, clip=CLIP, hand="Left", fps=30, sub=sub, frames=N,
                   source=dict(fbx="(blender author, before export)", sha256="", bind="", bindSha256=""),
                   units=dict(boneUnitMetres=1.82, restHeight=1.78), boneNames=BODY_BONES, samples=samples),
              open(os.environ["V3S2_BODY"], "w", encoding="utf-8"))
    print("BODY written", os.environ["V3S2_BODY"])

if not os.environ.get("V3S2_NOEXPORT"):
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
    sc = bpy.context.scene; sc.frame_start, sc.frame_end = 0, N
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, CLIP + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_Wreck2_%s_v3_Work.blend" % CLIP.replace("Pelag_AN_Wreck2_", "")))
    print("exported", CLIP)
