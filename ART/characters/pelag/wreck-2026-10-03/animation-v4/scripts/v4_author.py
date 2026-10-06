"""Крушение v4: Pelag_AN_Wreck4_Swing1 / Swing2 / Lunge — тело сабельной серии, обе кисти на рукояти якоря.

blender -b --factory-startup -P v4_author.py -- <out_dir>
  1. тело — Pelag_AN_Sabre1–3 в тики v4_lib.warp (перенос на v6 как в Unity), таз ниже на постоянную (стопы стойки
     на земле), наклон назад и скрутка груди > 44° сняты позвоночником (v4_body.fix_body);
  2. кулаки — правый на пути правого кулака сабли, сдвинутом ровно настолько, чтобы обе руки доставали и кулаки не
     были в корпусе/бёдрах; ось рукояти (левый у кисточки → правый на цепи) — по клинку; сглажено по кадрам;
  3. стопы — подушечка опорной стопы стоит весь пролёт (в мире, с ходом корня выпада), лодыжка — IK;
  4. голова за грудью, кулаки сжаты; проверки v4_check; FBX (кадр = тик), Pelag_Wreck4_Work.blend,
     <clip>.track.json (точка выхода цепи из правого кулака + кости, оси корня Unity, 8 сэмплов на тик).
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
import v4_lib as L
from wk_rig import Rig, SIDES
import wk_grip, wk_fingers, wk_check, v3_arms
import v4_body as VB, v4_check as VC, v4_drawstow as DS
from wk_sample import measure, big_bone_deltas
from s_lib import Body, translate_world

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True)
PREFIX = "Pelag_AN_Wreck4_"
SERIES = ["Swing1", "Swing2", "Lunge"]
EXIT = 0.05

rig = Rig()
print("FIST", wk_grip.prepare(rig))
VB.v3s4_patch.RIG_[0] = rig
VC.install_root(rig)
body = Body(rig.mesh); mesh = wk_check.Mesh(rig, body)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance = dict(rig.snapshot()); base_relP = rig.head_m()["relP"]
ank0 = min(rig.P(s + "Foot").z for s in SIDES)
ball_z = {s: rig.P(s + "ToeBase").z for s in SIDES}
src = L.SabreSource(rig.dst)
src.apply("Pelag_AN_Sabre1", 0.0)
DZ = max(-0.095, ank0 - max(rig.P(s + "Foot").z for s in SIDES))   # обе стопы к земле (нижняя — IK колена), таз ≤ 0,5 корпуса
print("GROUND dz %.3f" % DZ)


def root_of(name, t):
    return (Vector((0, -L.lunge_root(name, t), 0)), Quaternion())


def body_pose(name, t):
    rig.dst.location = (0, 0, 0)          # перенос сабли считает таз в мире: корень выпада ставится ПОСЛЕ
    rig.reset()
    src.apply(L.TIMING[name]["sabre"], L.warp(name, t))
    translate_world(rig.dst, rig.dst.pose.bones["mixamorig:Hips"], Vector((0, 0, DZ)))
    VB.fix_body(rig)


def side_pole(side):
    """Локти двуручного хвата — наружу и вниз от корпуса (как у кувалды): предплечья идут перед грудью, не сквозь неё."""
    _, r, u, f = VB.chest_frame(rig)
    sg = 1.0 if side == "Right" else -1.0
    sh = rig.P(side + "Arm"); fist = wk_grip.fist_center(rig, side)
    h = max(0.0, min(1.0, (fist.z - sh.z + 0.15) / 0.6))      # кулаки над плечом — локти наружу и вперёд, не вниз
    m = (r * sg * POLE_OUT - u * POLE_DOWN * (1 - h) + f * (0.2 + 0.6 * h)).normalized()
    return (-m.y, m.x, m.z)


POLE_OUT, POLE_DOWN = 1.0, 0.45


def smooth_poles(poles, passes=2):
    out = [dict(p) for p in poles]
    for s in SIDES:
        v = [Vector(p[s]).normalized() for p in poles]
        for i in range(1, len(v)):
            if v[i].dot(v[i - 1]) < 0.2: v[i] = (v[i - 1] * 0.7 + v[i] * 0.3).normalized()   # без переворота локтя
        for _ in range(passes):
            v = [v[0]] + [(v[i - 1] + 2 * v[i] + v[i + 1]).normalized() for i in range(1, len(v) - 1)] + [v[-1]]
        for i, p in enumerate(out): p[s] = tuple(v[i])
    return out


JOINT_SPAN = {"Lunge": [(3.5, 6.5)]}   # руки над головой → рубящий: кисти проходят над локтем


def _mix_names(base, a, b, w, names):
    out = dict(base)
    for n in names:
        (la, qa), (lb, qb) = a[n], b[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    return out


def mix(a, b, w):
    out = dict(b)
    for n, (lb, qb) in b.items():
        if n.startswith("_"): continue
        la, qa = a[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    return out


def chain_point():
    fr = wk_grip.fist_center(rig, "Right"); fl_ = wk_grip.fist_center(rig, "Left")
    return fr + (fr - fl_).normalized() * EXIT


v3_arms.AXW[0] = 0.30; v3_arms.GAP = VB.GAP
STRIKE = {"Swing1": (0, 8), "Swing2": (0, 8), "Lunge": (0, 16)}   # окно удара (≤ 70°/тик): мах до контакт+3; выпад — до конца с рывком цепи
HK = 2                                                       # ключи на полтика: перенос, руки, стопы — в t = k / 2
clips = {}
hints = {s: (0.0, 0.0, 0.0, 0.0) for s in SIDES}
for name in SERIES:
    T = L.TIMING[name]; N = T["frames"]; K = N * HK
    bodies, Qs, Ds, poles, pull = [], [], [], [], []
    for k in range(K + 1):                                   # 1. тело и цели кулаков
        t = k / HK
        body_pose(name, t)
        rig.dst.location = root_of(name, t)[0]; bpy.context.view_layer.update()
        bodies.append(dict(rig.snapshot(), _root=root_of(name, t)))
        Q, D, p = VB.grip_target(rig); Qs.append(Q); Ds.append(D); pull.append(p)
        poles.append({"Right": side_pole("Right"), "Left": side_pole("Left")})
    Qs, Ds = VB.smooth_targets(Qs, Ds, passes=3, keep=(T["contact"] * HK,))
    poles = smooth_poles(poles, passes=3)
    snaps, info_all = [], []
    for k in range(K + 1):                                   # 2. руки
        rig.restore(bodies[k])
        g = Qs[k] - Ds[k] * VB.GAP
        info = v3_arms.place_hands(rig, v3_arms.to_unity(g), v3_arms.to_unity(g + Ds[k] * 1.6), poles[k], hints,
                                   full=(name == SERIES[0] and k == 0))
        hints = {s: info[s][0] for s in SIDES}
        if k == T["end"] * HK: end_hints = dict(hints)
        wk_fingers.apply(rig, 1.0, 1.0)
        L.head_follow(rig, base_relP)
        bpy.context.view_layer.update()
        snaps.append(dict(rig.snapshot(), _root=root_of(name, k / HK))); info_all.append(info)
    ARMS = [n for n in snaps[0] if any(n.startswith("mixamorig:" + sd + x) for sd in SIDES for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]
    for a_, b_ in JOINT_SPAN.get(name, []):                  # руки между ключами — поворотами суставов (IK там перекидывает локоть)
        ka, kb = int(a_ * HK), int(b_ * HK)
        for k in range(ka + 1, kb):
            snaps[k] = dict(mix(snaps[k], mix(snaps[ka], snaps[kb], (k - ka) / (kb - ka)), 1.0) if False else
                            _mix_names(snaps[k], snaps[ka], snaps[kb], (k - ka) / (kb - ka), ARMS))
    if name != SERIES[0]:                                    # стык: удар начинается ровно с конца прошлого
        prev = clips[SERIES[SERIES.index(name) - 1]]
        pe = prev["snaps"][L.TIMING[prev["name"]]["end"] * HK]
        for k, w in enumerate((0.0, 0.3, 0.55, 0.78, 0.93)):
            snaps[k] = dict(mix(pe, snaps[k], w), _root=snaps[k]["_root"])
    hints = end_hints                                        # следующий удар начинается с конца этого
    clips[name] = dict(snaps=snaps, info=info_all, pull=pull, Ds=Ds, name=name)

anch = None                                                  # 3. стопы по серии: опора продолжается через стык
for name in SERIES:
    T = L.TIMING[name]; c = clips[name]
    anch = VB.lock_feet(rig, mesh, c["snaps"], name, T["end"] * HK, anch, ball_z, kscale=HK)

dk, dch = DS.draw(rig, stance, clips["Swing1"]["snaps"], HK, side_pole)        # снятие и уборка — первые версии
clips["Draw"] = dict(snaps=dk, chain=dch, name="Draw")
sk, sch = DS.stow(rig, stance, clips["Lunge"]["snaps"], HK, L.TIMING["Lunge"]["end"], side_pole)
clips["Stow"] = dict(snaps=sk, chain=sch, name="Stow")
ALL = ["Draw"] + SERIES + ["Stow"]
STRIKE.update(Draw=(0, 8), Stow=(0, 6))

summary, viol = {}, []
for name in ALL:                                             # 4. замер и проверки (по тикам; между тиками — по полтика)
    T = L.TIMING[name]; c = clips[name]; rows = []
    if "info" not in c:
        ticks = c["snaps"][::HK]
        for t, snap in enumerate(ticks):
            rig.restore(snap); row = measure(rig, body); row.update(frame=t, on_grip=False); rows.append(row)
        c["rows"] = rows
        s_, v_ = VC.check(rig, body, PREFIX + name, ticks, rows, STRIKE[name], stance, sub_snaps=c["snaps"])
        summary[name] = s_; viol += v_
        continue
    ticks = c["snaps"][::HK]
    for t, snap in enumerate(ticks):
        rig.restore(snap); row = measure(rig, body); info = c["info"][t * HK]
        row.update(frame=t, on_grip=True, sabre_tick=round(L.warp(name, t), 3), pull=round(c["pull"][t * HK], 3),
                   fist_miss={s: round(info[s][1], 3) for s in SIDES}, axis_deg={s: round(info[s][2], 1) for s in SIDES},
                   sol={s: [round(x, 1) for x in info[s][0]] for s in SIDES})
        rows.append(row)
        print("ROW %s %2d s%5.2f pull %.2f miss L %.3f R %.3f ax %s gap %.3f fore %s tw %.0f lean %.0f" % (
            name, t, row["sabre_tick"], c["pull"][t * HK], info["Left"][1], info["Right"][1], row["axis_deg"], row["hand_gap"],
            row["forearm_twist"], row["twist"], row["lean"]))
    c["rows"] = rows
    s_, v_ = VC.check(rig, body, PREFIX + name, ticks, rows, STRIKE[name], stance, sub_snaps=c["snaps"])
    summary[name] = s_; viol += v_
print("VIOLATIONS", len(viol))
for v in viol: print("  V", v[0][len(PREFIX):], v[1], v[2])
for a_, b_ in zip(ALL, ALL[1:]):
    ea = L.TIMING[a_]["end"] * HK if a_ != "Draw" else len(clips[a_]["snaps"]) - 1
    print("SEAM %s@%d -> %s@0: %.1f° %s" % (a_, ea // HK, b_, *big_bone_deltas(clips[a_]["snaps"][ea], clips[b_]["snaps"][0])))

SUB = 8
for name in ALL:
    T = L.TIMING[name]; c = clips[name]; N = T["frames"]; ex = []
    for k_, snap in enumerate(c["snaps"]):
        rt = snap.get("_root", (Vector((0, 0, 0)),))[0]
        rig.restore(dict(snap, _root=(Vector((0, 0, 0)), Quaternion())))      # оси клипа: ход корня — отдельно (root_fwd)
        cp = (c["chain"][k_] - rt) if "chain" in c else chain_point()
        ex.append(dict(chain=v3_arms.to_unity(cp), lsock=v3_arms.to_unity(v3_arms.socket(rig)),
                       rfist=v3_arms.to_unity(wk_grip.fist_center(rig, "Right")), lfist=v3_arms.to_unity(wk_grip.fist_center(rig, "Left")),
                       bones=[v3_arms.to_unity(rig.P(b)) for b in L.BODY_BONES]))
    lerp = lambda a, b, u: [x + (y - x) * u for x, y in zip(a, b)]
    def cr(p0, p1, p2, p3, u):                               # Катмулл–Ром по ключам: хват без изломов скорости
        return [0.5 * (2 * b + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (-a + 3 * b - 3 * c + d) * u ** 3)
                for a, b, c, d in zip(p0, p1, p2, p3)]
    samples = []
    for i in range(N * SUB + 1):
        fr = i / SUB; x = fr * HK; j = min(int(x), len(ex) - 2); u = x - j
        a, b = ex[j], ex[j + 1]; a0, b1 = ex[max(0, j - 1)], ex[min(len(ex) - 1, j + 2)]
        samples.append(dict(t=fr / 30, frame=fr, grip=cr(a0["chain"], a["chain"], b["chain"], b1["chain"], u), gripQ=[0, 0, 0, 1],
                            support=cr(a0["chain"], a["chain"], b["chain"], b1["chain"], u), lsock=lerp(a["lsock"], b["lsock"], u),
                            rfist=lerp(a["rfist"], b["rfist"], u), lfist=lerp(a["lfist"], b["lfist"], u),
                            spine2Q=[0, 0, 0, 1], bones=[lerp(x_, y_, u) for x_, y_ in zip(a["bones"], b["bones"])],
                            root_fwd=L.lunge_root(name, fr)))
    json.dump(dict(version=1, clip=PREFIX + name, hand="Right", fps=30, sub=SUB, frames=N, timing=T,
                   source=dict(fbx=PREFIX + name + ".fbx", sha256="", bind="Pelag_AN_Wreck4Bind", bindSha256=""),
                   units=dict(boneUnitMetres=1.82, restHeight=1.78), boneNames=L.BODY_BONES, samples=samples),
              open(os.path.join(OUT, PREFIX + name + ".track.json"), "w", encoding="utf-8"))
    json.dump(dict(rows=c["rows"], summary={k: str(v) for k, v in summary[name].items()},
                   violations=[[a, b, str(x)] for a, b, x in viol if a == PREFIX + name]),
              open(os.path.join(OUT, PREFIX + name + ".rows.json"), "w", encoding="utf-8"), indent=1)

dst = rig.dst; dst.animation_data_create(); dst.location = (0, 0, 0)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE' and o is not dst: bpy.data.objects.remove(o, do_unlink=True)
sc = bpy.context.scene
for name in ALL:
    act = bpy.data.actions.new(PREFIX + name); act.use_fake_user = True
    dst.animation_data.action = act; prev = {}
    for k, snap in enumerate(clips[name]["snaps"]):
        f = k / HK
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
    bpy.ops.object.select_all(action='DESELECT'); dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = rig.export_scale; bpy.context.view_layer.update()
    sc.frame_start, sc.frame_end = 0, L.TIMING[name]["frames"]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, PREFIX + name + ".fbx"), use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_step=0.5, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    dst.scale = (1, 1, 1); bpy.context.view_layer.update()
dst.animation_data.action = None
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_Wreck4_Work.blend"))
print("AUTHORED", ALL, "violations", len(viol))
