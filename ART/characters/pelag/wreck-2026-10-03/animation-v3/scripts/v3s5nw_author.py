"""v3s5nw: копия v3s4_author + KM.LOOP (кадр N = кадр 0, Девятый вал ChargeLoop) и шов петли в seams.

Крушение v3, Swing2 (продолжение вращения) / Slam / Wait2 / Stow: копия v3s3_author с поправками для вращения корпуса на 360°:
  * рысканье таза/груди/взгляда — ошибка по кратчайшему углу (через ±180° спина не перекручивается);
  * наклон — в плоскости груди (ось — левый бок груди), замер наклона — по взгляду груди, а не по оси корня
    (у вращающегося героя «вперёд по корню» бывает спиной; запрет наклона назад — относительно груди);
  * KM.BODY_COPY {кадр: (клип, кадр)} — тело (всё, кроме рук) — ключи принятого клипа из его .blend, руки решаются заново
    по пути хвата; KM.SEAM — копия целиком (как раньше).
Окружение V3S4_* (как V3S3_*). Swing1, Wait1 и v3s3_author не трогаются.

Ниже — описание исходного v3s3_author.
Крушение v3, Slam / Stow: тело по модулю ключей, руки на путь хвата (как v3s2_author; Swing1 и Wait1 не трогаются).

blender -b --factory-startup -P v3s3_author.py -- <keys module> <plan.json | path.json> <out_dir>
  plan.json — сэмплы gripopt_s3 (frame, grip, ring): левое гнездо в grip, ось кулаков по цепи к кольцу запечённой головы;
  path.json — только {"frames": [...]}: ось — KM.handle_dir (живая голова, Stow).
  V3S3_CORR / V3S3_SOL / V3S3_NOEXPORT / V3S3_QUICK / V3S3_BODY — как V3S2_* (поправка переноса, решения рук, без FBX,
  без пересечений, кости тела в осях корня Unity для gripopt_s3).
KM.SEAM — кадры-копии из рабочих .blend принятых клипов; KM.FREE {кадр: доля} — кисти отпустили рукоять: руки — смесь
позы кадра KM.RELEASE_FROM и стойки серии сабли (локальные повороты, без перебора).
"""
import bpy, sys, os, json, math, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
from s_lib import Body
from wk_rig import Rig, SIDES
import wk_grip, wk_pose, wk_fingers, wk_check
from wk_sample import measure, big_bone_deltas
import v3_arms
import v3s4_patch
from v3s4_patch import RIG_

argv = sys.argv[sys.argv.index("--") + 1:]
KM = importlib.import_module(argv[0])
SRC, OUT = argv[1], argv[2]
V3 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
os.makedirs(OUT, exist_ok=True)
CLIP, N = KM.CLIP, KM.N
J = json.load(open(SRC, encoding="utf-8"))
if "samples" in J:
    S = {round(s["frame"] * 4): s for s in J["samples"]}
    G = [S[4 * f]["grip"] for f in range(N + 1)]; RING = [S[4 * f]["ring"] for f in range(N + 1)]
else:
    G = J["frames"]; RING = None
ENV = lambda k: os.environ.get("V3S4_" + k)
_CJ = json.load(open(ENV("CORR"))) if ENV("CORR") else {}
CORR = _CJ.get("corr", [[0.0, 0.0, 0.0]] * (N + 1)); CORR_H = _CJ.get("corr_half", {})
PREV = json.load(open(ENV("SOL"), encoding="utf-8"))["rows"] if ENV("SOL") and os.path.exists(ENV("SOL")) else None
FREE = getattr(KM, "FREE", {})

rig = Rig()
RIG_[0] = rig
if getattr(KM, "RSIGN", 1) < 0:
    def _place_hands(rig_, grip_u, ring_u, poles_, hints_, full=False, gaps=(0.12, 0.15, 0.18, 0.21), narrow=False):
        """v3_arms.place_hands, но правый кулак ПОД левым: G − ось·зазор (рукоять-«мачта», цепь от гнезда вбок)."""
        v3_arms.CLAV.clear(); v3_arms.NARROW[0] = narrow
        if narrow: gaps = (v3_arms.GAP,)
        g = v3_arms.from_unity(grip_u); ring_ = v3_arms.from_unity(ring_u)
        D = (ring_ - g).normalized()
        fist = g + (wk_grip.fist_center(rig_, "Left") - v3_arms.socket(rig_))
        out = {}; h = hints_["Left"]
        for it in range(4):
            solL, missL, axL = v3_arms.solve(rig_, "Left", fist, D, poles_["Left"], h, full=full and it == 0)
            err = g - v3_arms.socket(rig_)
            if err.length < 0.002: break
            fist = fist + err
            if it == 0: h = solL
        out["Left"] = (solL, (v3_arms.socket(rig_) - g).length, axL)
        G = wk_grip.fist_center(rig_, "Left"); e = wk_grip.fist_axis(rig_, "Left")
        if e.dot(D) < 0: e = -e
        axis = (e + D).normalized()
        best = None
        for gap in gaps:
            r = v3_arms.solve(rig_, "Right", G - axis * gap, D, poles_["Right"], hints_["Right"], full=full)
            score = r[1] * 100 + abs(gap - v3_arms.GAP) * 5
            if best is None or score < best[0]: best = (score, gap, r)
        _, gap, r = best
        out["Right"] = v3_arms.solve(rig_, "Right", G - axis * gap, D, poles_["Right"], hints_["Right"], full=full)
        out["gap"] = gap
        return out
    v3_arms.place_hands = _place_hands
print("FIST", wk_grip.prepare(rig))
body = Body(rig.mesh)
rig.reset(); wk_fingers.apply(rig, 0.0, 0.0); bpy.context.view_layer.update()
stance_snap = rig.snapshot()
ARM = [n for n in stance_snap if any(n.startswith("mixamorig:" + s + x) for s in SIDES for x in ("Shoulder", "Arm", "ForeArm", "Hand"))]


def action_snapshot(clip, frame):
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


def mix(a, b, w, names):
    out = dict(a)
    for n in names:
        (la, qa), (lb, qb) = a[n], b[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, w), qa.slerp(qb, w))
    return out


seam_snaps = {f: action_snapshot(*src) for f, src in KM.SEAM.items()}
copy_snaps = {f: action_snapshot(*src) for f, src in getattr(KM, "BODY_COPY", {}).items()}
hrows = json.load(open(os.path.join(V3, KM.HINT_FROM[0] + ".rows.json"), encoding="utf-8"))["rows"]
hints = {s: tuple(hrows[KM.HINT_FROM[1]]["sol"][s]) for s in SIDES}
snaps, rows = [], []
for f in range(N + 1):
    info = None; free = f in FREE
    if f in seam_snaps:
        rig.restore(seam_snaps[f])
    else:
        p = KM.body_at(f)
        if f in copy_snaps:     # тело принятого клипа (без рук), руки — заново по пути хвата
            cs = copy_snaps[f]
            rig.restore({k: (cs[k] if not any(k.startswith("mixamorig:" + s_ + x) for s_ in ("Left", "Right")
                                                for x in ("Shoulder", "Arm", "ForeArm", "Hand")) else stance_snap[k]) for k in stance_snap})
        else:
            wk_pose.body(rig, p)
        if hasattr(KM, "post_body"): KM.post_body(rig, p)
        if free:
            rig.restore(mix(rig.snapshot(), mix(snaps[KM.RELEASE_FROM], stance_snap, FREE[f], ARM), 1.0, ARM))
        else:
            target = [g - c for g, c in zip(G[f], CORR[f])]
            if RING:
                ring = RING[f]
                w_ = KM.handle_over(f) if hasattr(KM, "handle_over") else 0.0
                if w_ > 0:      # над головой: кулаки по оси рукояти (вверх-вперёд), цепь гнётся у кулака — правая не идёт к лицу
                    d_ = [r_ - t_ for r_, t_ in zip(ring, target)]; n_ = math.sqrt(sum(c * c for c in d_))
                    u_ = KM.UPFWD; nu = math.sqrt(sum(c * c for c in u_))
                    m_ = [a_ / n_ + (b_ / nu - a_ / n_) * w_ for a_, b_ in zip(d_, u_)]; nm = math.sqrt(sum(c * c for c in m_))
                    ring = [t_ + 1.6 * c / nm for t_, c in zip(target, m_)]
            else:
                rs = v3_arms.to_unity(rig.P("RightArm")); d_ = [r_ - t_ for r_, t_ in zip(rs, target)]; n_ = math.sqrt(sum(c * c for c in d_))
                hd = KM.handle_dir(f, target, [c / n_ for c in d_])
                ring = [t + 1.6 * c for t, c in zip(target, hd)]
            if hasattr(KM, "fist_axis"):    # v3s5nw: ось кулаков своя (рукоять-«мачта»), цепь вертится у гнезда
                d_ = KM.fist_axis(f, target, ring); ring = [t_ + 1.6 * c for t_, c in zip(target, d_)]
            v3_arms.AXW[0] = KM.axw(f) if hasattr(KM, "axw") else KM.AXW; v3_arms.GAP = KM.GAP
            if PREV and PREV[f].get("sol"):
                h = {s_: tuple(PREV[f]["sol"][s_]) for s_ in SIDES}
                info = v3_arms.place_hands(rig, target, ring, KM.poles(f, p), h, full=False, narrow=True)
            else:
                if f in getattr(KM, "HINT_AT", {}):      # решение рук из принятого клипа в той же позе (без перескока к пределу)
                    hc, hf = KM.HINT_AT[f]
                    hrr = json.load(open(os.path.join(V3, hc + ".rows.json"), encoding="utf-8"))["rows"][hf]["sol"]
                    hints = {s_: tuple(hrr[s_]) for s_ in SIDES}
                info = v3_arms.place_hands(rig, target, ring, KM.poles(f, p), hints, full=(f == 0 and getattr(KM, "FULL0", False)), narrow=False)
            hints = {s: info[s][0] for s in SIDES}
        if f in getattr(KM, "FREE_R", {}):     # правая уже отпустила цепь: смесь позы кадра 0 и стойки (локальные повороты)
            RA = [n for n in ARM if n.startswith("mixamorig:Right")]
            rig.restore(mix(rig.snapshot(), mix(snaps[0], stance_snap, KM.FREE_R[f], RA), 1.0, RA))
        wk_pose.head(rig, p)
        if not free: wk_fingers.apply(rig, p["gL"], p["gR"] * (1 - getattr(KM, "FREE_R", {}).get(f, 0.0)))
        else: wk_fingers.apply(rig, p["gL"] * (1 - FREE[f]), p["gR"] * (1 - FREE[f]))
        bpy.context.view_layer.update()
    snaps.append(rig.snapshot())
    row = measure(rig, body)
    sock = v3_arms.to_unity(v3_arms.socket(rig))
    on = not free and f not in getattr(KM, "OFF_GRIP", ()) and f not in getattr(KM, "FREE_R", {})
    row.update(frame=f, socket=[round(c, 4) for c in sock], on_grip=on, seam_copy=f in seam_snaps,
               socket_miss=round(math.dist(sock, [g - c for g, c in zip(G[f], CORR[f])]), 4))
    if info: row.update(sol={s: [round(x, 1) for x in info[s][0]] for s in SIDES}, fist_miss={s: round(info[s][1], 3) for s in SIDES},
                        axis_deg={s: round(info[s][2], 1) for s in SIDES})
    else: row.update(sol={s: list(hints[s]) for s in SIDES})
    rows.append(row)
    print("ROW %2d sockMiss %.3f lean %5.1f pY %6.1f cY %6.1f tw %5.1f gap %.3f fore %s relP %.1f feet L %.4f R %.4f %s" % (
        f, row["socket_miss"], row["lean"], row["pelvis_yaw"], row["chest_yaw"], row["twist"], row["hand_gap"], row["forearm_twist"],
        row["relP"], row["Left"]["toe_z"], row["Right"]["toe_z"], "seam copy" if f in seam_snaps else ("free %.2f" % FREE[f] if free else "sol %s" % row["sol"])))

def _close_loop(tag):
    """KM.LOOP: кадр N — копия кадра 0 (петля без перескока решения IK), строка замеров — заново."""
    if not getattr(KM, "LOOP", False): return
    snaps[N] = {k: (v[0].copy(), v[1].copy()) for k, v in snaps[0].items()}
    rig.restore(snaps[N])
    keep = {k: rows[0][k] for k in ("socket", "on_grip", "sol", "fist_miss", "axis_deg") if k in rows[0]}
    rows[N] = measure(rig, body); rows[N].update(keep); rows[N]["frame"] = N; rows[N]["seam_copy"] = True
    rows[N]["socket_miss"] = rows[0].get("socket_miss", 0.0)
    print("LOOP closed (%s): frame %d = frame 0" % (tag, N))


_close_loop("solve")

for side, (fa, fb, ws) in getattr(KM, "ARM_INTERP", {}).items():
    # рука между двумя решёнными кадрами — смесью локальных поворотов (без перескока решения IK на большом ходе руки)
    names = [n for n in ARM if n.startswith("mixamorig:" + side)] + [n for n in snaps[0] if n.startswith("mixamorig:" + side + "Hand")]
    for f, w in ws.items():
        snaps[f] = mix(snaps[f], mix(snaps[fa], snaps[fb], w, names), 1.0, names)
        rig.restore(snaps[f])
        keep = {k: rows[f][k] for k in ("frame", "on_grip", "seam_copy", "sol") if k in rows[f]}
        rows[f] = measure(rig, body); rows[f].update(keep)
        sock = v3_arms.to_unity(v3_arms.socket(rig)); rows[f]["socket"] = [round(c, 4) for c in sock]
        rows[f]["socket_miss"] = round(math.dist(sock, [g - c for g, c in zip(G[f], CORR[f])]), 4); rows[f]["on_grip"] = False
    print("ARM_INTERP", side, {f: rows[f]["socket_miss"] for f in ws})

SM = getattr(KM, "SMOOTH", None)
if SM:
    bones, passes, wsm = SM
    names = ["mixamorig:" + b for b in bones]
    lo = getattr(KM, "SMOOTH_FROM", 1)
    for _ in range(passes):
        new = [dict(x) for x in snaps]
        for f in range(lo, N):
            if f in seam_snaps or f in FREE: continue
            for n in names:
                qa, qb, qc = snaps[f - 1][n][1], snaps[f + 1][n][1].copy(), snaps[f][n][1].copy()
                if qa.dot(qb) < 0: qb.negate()
                mid = qa.slerp(qb, .5)
                if qc.dot(mid) < 0: qc.negate()
                new[f][n] = (snaps[f][n][0], qc.slerp(mid, wsm))
        snaps = new
    for f in range(lo, N):
        if f in seam_snaps or f in FREE: continue
        rig.restore(snaps[f])
        keep = {k: rows[f][k] for k in ("frame", "socket", "on_grip", "seam_copy", "socket_miss", "sol", "fist_miss", "axis_deg") if k in rows[f]}
        rows[f] = measure(rig, body); rows[f].update(keep)
    print("SMOOTH", bones, "gaps", [rows[f]["hand_gap"] for f in range(N + 1)])

_close_loop("smooth")

# ключи на полкадра (KM.HALF): тело и руки решаются в f + 0,5 (путь хвата — сэмпл плана на полкадре); проверки под-кадров
# (стопы под землёй / опора не едет) идут по этим ключам, скорость поворота костей — по тикам, как раньше
half = {}
for f in getattr(KM, "HALF", []):
    if f + 1 > N or f in FREE: continue
    t = f + 0.5
    p = KM.body_at(t)
    wk_pose.body(rig, p)
    if hasattr(KM, "post_body"): KM.post_body(rig, p)
    s4 = S[round(4 * t)] if "samples" in J else None
    gh = s4["grip"] if s4 else [(a + b) / 2 for a, b in zip(G[f], G[f + 1])]
    ch_ = CORR_H.get(str(f), [(c0 + c1) / 2 for c0, c1 in zip(CORR[f], CORR[f + 1])])
    target = [g - c for g, c in zip(gh, ch_)]
    ring = s4["ring"] if s4 else [(a + b) / 2 for a, b in zip(RING[f], RING[f + 1])]
    if hasattr(KM, "fist_axis"):
        d_ = KM.fist_axis(t, target, ring); ring = [t_ + 1.6 * c for t_, c in zip(target, d_)]
    v3_arms.AXW[0] = KM.axw(t) if hasattr(KM, "axw") else KM.AXW; v3_arms.GAP = KM.GAP
    h = {s_: tuple((a + b) / 2 for a, b in zip(rows[f]["sol"][s_], rows[f + 1]["sol"][s_])) for s_ in SIDES}
    info = v3_arms.place_hands(rig, target, ring, KM.poles(t, p), h, full=False, narrow=True)
    wk_pose.head(rig, p)
    wk_fingers.apply(rig, p["gL"], p["gR"])
    bpy.context.view_layer.update()
    half[f] = rig.snapshot()
    hr = measure(rig, body)
    print("HALF %4.1f sockMiss %.3f pY %6.1f cY %6.1f feet L %.4f R %.4f" % (t, math.dist(v3_arms.to_unity(v3_arms.socket(rig)), target),
                                                                         hr["pelvis_yaw"], hr["chest_yaw"], hr["Left"]["toe_z"], hr["Right"]["toe_z"]))
_interp0 = wk_check.interp


def _interp_half(sn, x):
    i = int(math.floor(x + 1e-9)); t = x - i
    if sn is snaps and i in half and t > 1e-6:
        a, b, u = (sn[i], half[i], t / 0.5) if t <= 0.5 else (half[i], sn[i + 1], (t - 0.5) / 0.5)
        out = {}
        for n, (la, qa) in a.items():
            lb, qb = b[n]
            if qa.dot(qb) < 0: qb = -qb
            out[n] = (la.lerp(lb, u), qa.slerp(qb, u))
        return out
    return _interp0(sn, x)


wk_check.interp = _interp_half

seams = {}
for f in seam_snaps:
    nb = f + 1 if f + 1 <= N else f - 1
    seams["f%d_vs_f%d" % (f, nb)] = big_bone_deltas(snaps[f], snaps[nb])
if getattr(KM, "LOOP", False):
    seams["f0_vs_f1"] = big_bone_deltas(snaps[0], snaps[1]); seams["f%d_vs_f%d" % (N, N - 1)] = big_bone_deltas(snaps[N], snaps[N - 1])
print("SEAM neighbours", {k: (round(v[0], 1), v[1]) for k, v in seams.items()})
if hasattr(KM, "STRIKE"): wk_check.STRIKE[CLIP] = KM.STRIKE
if ENV("DELTAS"):
    for f_ in range(N):
        d_ = sorted(wk_check.deltas(snaps[f_], snaps[f_ + 1]).items(), key=lambda kv: -kv[1])
        print("DELTA %2d→%2d " % (f_, f_ + 1) + " ".join("%s %.0f" % (k, v) for k, v in d_[:6] if not any(x in k for x in ("Thumb", "Index", "Middle", "Ring", "Pinky"))))
summary, viol = wk_check.check(rig, body, {CLIP: snaps, "_stance": stance_snap}, {CLIP: rows}, quick=bool(ENV("QUICK")))
wk_check.print_summary(summary, viol)
json.dump(dict(rows=rows, seams={k: [round(v[0], 2), v[1]] for k, v in seams.items()},
               summary={k: {kk: (str(vv) if not isinstance(vv, (int, float)) else vv) for kk, vv in v.items()} for k, v in summary.items()},
               violations=[[a, b, str(c)] for a, b, c in viol]), open(os.path.join(OUT, CLIP + ".rows.json"), "w", encoding="utf-8"), indent=1)

BODY_BONES = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'HeadTop_End',
              'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1', 'LeftHandIndex1',
              'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1',
              'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End',
              'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End']
if ENV("BODY"):
    per = []
    times = sorted([float(f) for f in range(N + 1)] + [f + 0.5 for f in half])
    for tt in times:
        rig.restore(snaps[int(tt)] if tt == int(tt) else half[int(tt)])
        per.append(dict(bones=[v3_arms.to_unity(rig.P(b)) for b in BODY_BONES], grip=v3_arms.to_unity(v3_arms.socket(rig)),
                        support=v3_arms.to_unity(v3_arms.socket(rig, "Right"))))
    sub, samples = 8, []
    lerp = lambda a, b, u: [x + (y - x) * u for x, y in zip(a, b)]
    for i in range(N * sub + 1):
        fr = i / sub; j = max(k for k in range(len(times) - 1) if times[k] <= fr + 1e-9) if fr < times[-1] else len(times) - 2
        u = (fr - times[j]) / (times[j + 1] - times[j]); a, b = per[j], per[j + 1]
        samples.append(dict(t=fr / 30, frame=fr, grip=lerp(a["grip"], b["grip"], u), gripQ=[0, 0, 0, 1], support=lerp(a["support"], b["support"], u),
                            supportQ=[0, 0, 0, 1], spine2Q=[0, 0, 0, 1], bones=[lerp(x, y, u) for x, y in zip(a["bones"], b["bones"])]))
    json.dump(dict(version=1, clip=CLIP, hand="Left", fps=30, sub=sub, frames=N,
                   source=dict(fbx="(blender author, before export)", sha256="", bind="", bindSha256=""),
                   units=dict(boneUnitMetres=1.82, restHeight=1.78), boneNames=BODY_BONES, samples=samples),
              open(ENV("BODY"), "w", encoding="utf-8"))
    print("BODY written", ENV("BODY"))

if not ENV("NOEXPORT"):
    dst = rig.dst
    dst.animation_data_create()
    act = bpy.data.actions.new(CLIP); act.use_fake_user = True
    dst.animation_data.action = act
    prev = {}
    keyed = sorted([(float(f), sn) for f, sn in enumerate(snaps)] + [(f + 0.5, half[f]) for f in half], key=lambda x: x[0])
    for f, snap in keyed:
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
                             bake_anim_step=0.5 if half else 1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_Wreck2_%s_v3_Work.blend" % CLIP.replace("Pelag_AN_Wreck2_", "")))
    print("exported", CLIP)
