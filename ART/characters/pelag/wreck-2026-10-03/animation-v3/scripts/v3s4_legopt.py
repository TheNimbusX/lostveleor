"""Крушение v3, Swing2: подбор пути левой стопы в воздухе (пируэт на правой) под предел 35°/тик для костей ноги.
blender -b --factory-startup -P v3s4_legopt.py -- <out params.json> [passes]   (V3S4_PATH — путь gripopt_s4 с PSI)
Тело ставится как в v3s4_author (wk_pose.body с поправками v3s4_patch, без рук); цель — наибольший поворот LeftUpLeg/LeftLeg/
LeftFoot за тик ≤ 31°, носок в воздухе ≥ 5 см, левая голень/стопа не ближе 0,15 м к правой голени. Покоординатный спуск по 12 узлам
(доли пути по углу, радиус, высота, наклон, разворот носка); результат — params.json, его читает v3s4_keys (LAIR)."""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from wk_rig import Rig, M
import wk_grip, wk_pose, wk_check
import v3s4_patch
from v3s4_patch import RIG_
import v3s4_keys as K
import v3_arms
PLAN = os.environ.get("V3S4_PLAN")
GRIP = None
if PLAN:
    _S = {round(x["frame"] * 4): x for x in json.load(open(PLAN))["samples"]}
    GRIP = {f: _S[4 * f]["grip"] for f in range(K.N + 1)}

argv = sys.argv[sys.argv.index("--") + 1:]
OUTP = argv[0]; PASSES = int(argv[1]) if len(argv) > 1 else 3
rig = Rig(); RIG_[0] = rig; wk_grip.prepare(rig)
LEG = ["mixamorig:Left" + b for b in ("UpLeg", "Leg", "Foot")] + ["mixamorig:Right" + b for b in ("UpLeg", "Leg", "Foot")]
X0 = dict(K.LAIR)
if len(argv) > 2: X0.update(json.load(open(argv[2])))
NAMES = sorted(X0)
F0, F1 = max(1, K.PIV - 1), min(K.N, K.LAND + 2)


def evaluate(X):
    K.LAIR.update(X)
    snaps, toes, near = {}, {}, {}
    evaluate.hand = {}
    for f in range(F0, F1 + 1):
        p = K.body_at(f)
        wk_pose.body(rig, p)
        bpy.context.view_layer.update()
        snaps[f] = {n: (Vector(), rig.dst.pose.bones[n].rotation_quaternion.copy()) for n in LEG}
        toes[f] = rig.toe_z("Left")
        a = rig.P("LeftFoot"); k = rig.P("LeftLeg"); r0, r1 = rig.P("RightLeg"), rig.P("RightFoot")
        ab = r1 - r0
        def seg(pt):
            t = max(0.0, min(1.0, (pt - r0).dot(ab) / max(1e-9, ab.length_squared)))
            return (pt - (r0 + ab * t)).length
        near[f] = min(seg(a), seg(k), (k - rig.P("RightLeg")).length)
        if GRIP:      # кисти (гнездо хвата плана) — не ближе 0,20 м к левому бедру/колену
            g = v3_arms.from_unity(GRIP[f]); u0, u1 = rig.P("LeftUpLeg"), k; uv = u1 - u0
            t = max(0.0, min(1.0, (g - u0).dot(uv) / max(1e-9, uv.length_squared)))
            evaluate.hand[f] = (g - (u0 + uv * t)).length
    cost, worst = 0.0, (0.0, "")
    for f in range(F0, F1):
        for n, d in wk_check.deltas(snaps[f], snaps[f + 1]).items():
            cost += 10 * max(0.0, d - 31.0) ** 2 + 0.01 * d * d
            if d > worst[0]: worst = (round(d, 1), "%d→%d %s" % (f, f + 1, n))
    for f in range(K.PIV + 1, K.LAND):
        cost += 2e5 * max(0.0, (0.07 if f < K.LAND - 1 else 0.035) - toes[f]) ** 2
    for f, d in near.items():
        cost += 2e4 * max(0.0, 0.26 - d) ** 2
    for f, d in evaluate.hand.items():
        cost += 2e4 * max(0.0, 0.31 - d) ** 2
    evaluate.near = near; evaluate.toes = toes
    return cost, worst, min(near.values())


best = dict(X0); bc, bw, bn = evaluate(best)
print("LEGOPT init cost %.2f worst %s near %.3f" % (bc, bw, bn))
STEP = {k: (0.06 if k.startswith(("rel", "pitch", "yaw")) and not k.startswith("pitch") else 0.03) for k in NAMES}
for k in NAMES:
    if k.startswith("pitch"): STEP[k] = 4.0
    if k.startswith("yaw"): STEP[k] = 8.0
for ps in range(PASSES):
    for k in NAMES:
        for sgn in (1, -1):
            for _ in range(6):
                trial = dict(best); trial[k] = best[k] + sgn * STEP[k]
                c, w, nn = evaluate(trial)
                if c < bc - 1e-6: best, bc, bw, bn = trial, c, w, nn
                else: break
    print("LEGOPT pass %d cost %.2f worst %s near %.3f" % (ps, bc, bw, bn))
    for k in STEP: STEP[k] *= 0.5
json.dump(best, open(OUTP, "w"), indent=1)
evaluate(best); print("LEGOPT near", {f: round(d, 3) for f, d in evaluate.near.items()}, "toes", {f: round(t, 3) for f, t in evaluate.toes.items()})
print("LEGOPT done", json.dumps({k: round(v, 3) for k, v in best.items()}))
