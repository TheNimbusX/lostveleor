"""Абордаж v3: совместный перебор кадра 2 (через плечо) и кадра 3 (ВЫПУСК) Throw.
Пределы: стойка→2 — каждая кость ≤ 2×35 (два обычных тика замаха); 2→3 и 3→4 (T3 принятого) — правая рука ≤ 70,
остальное ≤ 35. Счёт: кисть в кадре 2 выше и дальше за плечом; в кадре 3 — выше плеча и впереди. Только замер.
blender -b --factory-startup -P ab3_search_release.py"""
import bpy, sys, os, math, json, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M
import ab_arm, ab_fingers, ab_keys
from ab_pose import apply
from ab_check import deltas
from ab3_arm import K3, chest_dir

ARM_R = ("RightArm", "RightForeArm", "RightHand")
rig = Rig(); ab_arm.prepare(rig); ab_fingers.prepare(rig)
B = rig.B
S = rig.snapshot()
st = ab_keys.stance(B)
sL, sR = st["feet"]["Left"], st["feet"]["Right"]
rUp = (sR[0] - 0.06, sR[1] - 0.02, sR[2] + 0.07, sR[3], sR[4] + 3)
rB2 = ab_keys.on_toe("Right", sR, 6, shift=(-0.10, -0.03))
T3 = ab_keys.clips(B)["Pelag_AN_Abordage2_Throw"][3]
apply(rig, T3); sT3 = rig.snapshot()


def lim_ok(d, arm_lim, body_lim):
    wa = max(d[n] for n in ARM_R); wb = max(v for n, v in d.items() if n not in ARM_R)
    return wa <= arm_lim and wb <= body_lim, round(wa, 1), round(wb, 1), max(((v, n) for n, v in d.items() if n not in ARM_R))[1]


F2 = []
for az, el, flex, sw in itertools.product((-30, -20, -10, 0, 10), (0, 10, 20), (95, 105), (45, 60, 75)):
    k = K3(B, -0.04, -58, -60, 0, sL, rUp, chest_dir(-60, az, el), flex, (0.33, 0.07, -0.27), 0.66, 0.0, wd=20.0, sw=sw)
    apply(rig, k); s = rig.snapshot()
    ok, wa, wb, bb = lim_ok(deltas(S, s), 68, 68)
    if not ok: continue
    h, sh = rig.P("RightHand"), rig.P("RightArm")
    F2.append(dict(p=(az, el, flex, sw), s=s, up=h.z - sh.z, back=h.y - sh.y, wa=wa))
print("F2 ok", len(F2))
F3 = []
for cy, az, el, flex, sw in itertools.product((-18, -28), (20, 40, 60, 80), (10, 30, 50), (40, 60, 80), (0, 25, 50)):
    k = K3(B, -0.07, -46, cy, 14, sL, rB2, chest_dir(cy, az, el), flex, (0.34, -0.03, -0.24), 1.0, -1.0, wd=32.0, sw=sw)
    apply(rig, k); s = rig.snapshot()
    ok, wa, wb, bb = lim_ok(deltas(s, sT3), 68, 34)
    if not ok: continue
    h, sh = rig.P("RightHand"), rig.P("RightArm")
    F3.append(dict(p=(cy, az, el, flex, sw), s=s, up=h.z - sh.z, fwd=-(h.y - sh.y), wa=wa))
print("F3 ok", len(F3))
res = []
for a in F2:
    for b in F3:
        ok, wa, wb, bb = lim_ok(deltas(a["s"], b["s"]), 68, 34)
        if not ok: continue
        sc = a["up"] + 0.8 * a["back"] + 0.6 * max(0.0, b["up"]) + 0.3 * b["fwd"]
        res.append((sc, a["p"], round(a["up"], 3), round(a["back"], 3), b["p"], round(b["up"], 3), round(b["fwd"], 3), wa, wb, bb))
res.sort(key=lambda r: -r[0])
print("PAIRS", len(res))
for r in res[:30]: print("PAIR", json.dumps(r))
