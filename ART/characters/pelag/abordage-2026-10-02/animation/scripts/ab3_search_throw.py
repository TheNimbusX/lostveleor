"""Абордаж v3: подбор кадра 1 (середина замаха) и кадра 3 (ВЫПУСК) нового Throw при принятых 2 (через плечо) и 9 (натяг).
Пределы: 0→1, 1→2 — каждая кость ≤ 35; 2→3 — правая рука ≤ 70, остальное ≤ 35; 3→9 одним тиком (A = 1) — правая ≤ 70
(путь через кадр 4 = середина, поэтому берём прямую разницу ≤ 64), остальное ≤ 33. Только замер.
blender -b --factory-startup -P ab3_search_throw.py"""
import bpy, sys, os, math, json, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M
import ab_arm, ab_fingers, ab_keys, ab3_keys
from ab_pose import apply
from ab_check import deltas
from ab3_arm import K3, chest_dir
from ab_keys import crot, SU, on_toe

ARM_R = ("RightArm", "RightForeArm", "RightHand")
rig = Rig(); ab_arm.prepare(rig); ab_fingers.prepare(rig)
B = rig.B
S = rig.snapshot()
C = ab3_keys.clips(B)["Pelag_AN_Abordage2_Throw"]
apply(rig, C[2]); s2 = rig.snapshot()
apply(rig, C[9]); s9 = rig.snapshot()
st = ab_keys.stance(B); sL, sR = st["feet"]["Left"], st["feet"]["Right"]
rUp = (sR[0] - 0.06, sR[1] - 0.02, sR[2] + 0.07, sR[3], sR[4] + 3)
rB2 = on_toe("Right", sR, 6, shift=(-0.10, -0.03))
u2 = chest_dir(-60, -20, 10)


def split(d):
    return max(d[n] for n in ARM_R), max(v for n, v in d.items() if n not in ARM_R)


best = []
for t, flex, sw, cy in itertools.product((0.4, 0.5, 0.6), (66, 72, 78), (15, 25, 35, 45), (-42, -46, -50)):
    u1 = ab3_keys.slerp_dir(crot(SU, cy - B["cyaw"]), u2, t)
    k = K3(B, -0.02, -55, cy, 0, sL, rUp, u1, flex, (0.32, 0.09, -0.32), 0.33, 0.0, wd=26.0, sw=sw)
    apply(rig, k); s1 = rig.snapshot()
    a = deltas(S, s1); b = deltas(s1, s2)
    w = max(max(a.values()), max(b.values()))
    best.append((round(w, 1), t, flex, sw, cy, round(max(a.values()), 1), round(max(b.values()), 1),
                 max(a, key=a.get), max(b, key=b.get)))
best.sort()
for r in best[:8]: print("W1", json.dumps(r))
res = []
for cy, az, el, flex, sw in itertools.product((-22, -28), (30, 45, 60, 75), (20, 35, 50), (40, 55, 70), (0, 15, 30, 45)):
    k = K3(B, -0.07, -46, cy, 14, sL, rB2, chest_dir(cy, az, el), flex, (0.34, -0.03, -0.24), 1.0, -1.0, wd=32.0, sw=sw)
    apply(rig, k); s3 = rig.snapshot()
    a23 = split(deltas(s2, s3)); a39 = split(deltas(s3, s9))
    if a23[0] > 68 or a23[1] > 34 or a39[0] > 64 or a39[1] > 33: continue
    h, sh = rig.P("RightHand"), rig.P("RightArm")
    res.append((round((h.z - sh.z) + 0.5 * (sh.y - h.y), 3), (cy, az, el, flex, sw), round(h.z - sh.z, 3), round(sh.y - h.y, 3),
                [round(x, 1) for x in a23], [round(x, 1) for x in a39]))
res.sort(key=lambda r: -r[0])
print("R3 ok", len(res))
for r in res[:15]: print("R3", json.dumps(r))
