"""Абордаж v2: перебор замаха (кадр 1 Throw) — кисть как можно выше и дальше назад у правого плеча при пределе
удара 70° на кость руки за тик и от стойки (кадр 0), и до выпуска (кадр 2). Только замер.
blender -b --factory-startup -P ab_search_windup.py"""
import bpy, sys, os, math, json, copy, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M
import ab_arm, ab_keys
from ab_pose import apply
from ab_check import deltas

rig = Rig(); ab_arm.prepare(rig)
B = rig.B
C = ab_keys.clips(B)["Pelag_AN_Abordage2_Throw"]
S = rig.snapshot()
T2 = copy.deepcopy(C[2]); apply(rig, T2); s2 = rig.snapshot()
base = C[1]
ARM = ("RightShoulder", "RightArm", "RightForeArm", "RightHand")
res = []
for f_, l_, u_ in itertools.product((-0.10, -0.04, 0.02, 0.08, 0.14), (-0.22, -0.14, -0.06, 0.02), (0.0, 0.08, 0.16, 0.24, 0.30)):
    d = (f_ * f_ + l_ * l_ + u_ * u_) ** .5
    if not 0.20 <= d <= 0.40: continue
    for pole in ((0.3, -0.5, -0.8), (0.0, -0.7, -0.7), (0.5, -0.8, 0.0), (-0.3, -0.8, -0.5), (0.6, -0.3, -0.7), (0.0, -1.0, 0.0), (0.4, -0.9, 0.2)):
        k = copy.deepcopy(base); k["hands"]["Right"] = (f_, l_, u_); k["poles"]["Right"] = pole; k.pop("fixed", None)
        apply(rig, k); s1 = rig.snapshot()
        a = deltas(S, s1); b = deltas(s1, s2)
        worst = max(max(a[n] for n in ARM), max(b[n] for n in ARM))
        hand = rig.P("RightHand"); sh = rig.P("RightArm")
        res.append(dict(v=(f_, l_, u_), pole=pole, worst=round(worst, 1), a=round(max(a[n] for n in ARM), 1), b=round(max(b[n] for n in ARM), 1),
                        hand_up=round(hand.z - sh.z, 3), hand_back=round(-(-hand.y + sh.y), 3), A={n: round(a[n]) for n in ARM}, Bb={n: round(b[n]) for n in ARM}))
ok = [r for r in res if r["worst"] <= 66]
ok.sort(key=lambda r: -(r["hand_up"] + 0.7 * r["hand_back"]))
print("CANDIDATES", len(res), "ok", len(ok))
for r in ok[:15]: print("OK", json.dumps(r))
res.sort(key=lambda r: r["worst"])
for r in res[:8]: print("LOW", json.dumps(r))
