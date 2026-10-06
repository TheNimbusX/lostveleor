"""Абордаж v3: перебор позы 10 (через плечо) для кадра 2 Throw — кисть выше и дальше назад у правого плеча,
при этом каждая кость от стойки (кадр 0) не дальше LIM (два обычных тика замаха по 35°). Только замер.
blender -b --factory-startup -P ab3_search_windup.py -- <lim> <cyaw,..> <lean,..>"""
import bpy, sys, os, math, json, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M
import ab_arm, ab_fingers, ab_keys
from ab_pose import apply
from ab_check import deltas
from ab3_arm import K3, chest_dir

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIM = float(argv[0]) if argv else 66.0
CYS = [float(x) for x in argv[1].split(",")] if len(argv) > 1 else [-50.0, -60.0]
LEANS = [float(x) for x in argv[2].split(",")] if len(argv) > 2 else [0.0, -4.0]
rig = Rig(); ab_arm.prepare(rig); ab_fingers.prepare(rig)
B = rig.B
S = rig.snapshot()
st = ab_keys.stance(B)
sL, sR = st["feet"]["Left"], st["feet"]["Right"]
rUp = (sR[0] - 0.06, sR[1] - 0.02, sR[2] + 0.07, sR[3], sR[4] + 3)
res = []
for cy, ln in itertools.product(CYS, LEANS):
    for az, el, flex, sw in itertools.product((-50, -40, -30, -20), (0, 8, 16, 24), (95, 105, 115), (45, 60, 75, 90)):
        u = chest_dir(cy, az, el)
        k = K3(B, -0.04, -58, cy, ln, sL, rUp, u, flex, (0.33, 0.07, -0.27), 0.68, 0.0, wd=20.0, sw=sw)
        apply(rig, k); s2 = rig.snapshot()
        a = deltas(S, s2)
        worst = max(((v, n) for n, v in a.items() if not n.startswith('Left') or n in ('LeftUpLeg','LeftLeg','LeftFoot')), key=lambda kv: kv[0])[::-1]
        wl = max(a[n] for n in ('LeftShoulder','LeftArm','LeftForeArm','LeftHand'))
        hand = rig.P("RightHand"); sh = rig.P("RightArm"); el_ = rig.P("RightForeArm"); hd = rig.P("Head")
        res.append(dict(cy=cy, lean=ln, az=az, el=el, flex=flex, sw=sw, worst=(round(worst[1], 1), worst[0]),
                        arm={n.replace("Right", "R"): round(a[n]) for n in ("RightShoulder", "RightArm", "RightForeArm", "RightHand")},
                        hand_up=round(hand.z - sh.z, 3), hand_back=round(hand.y - sh.y, 3), elbow_up=round(el_.z - sh.z, 3),
                        hand_head=round((hand - hd).length, 3), left=round(wl, 1)))
ok = [r for r in res if r["worst"][0] <= LIM]
ok.sort(key=lambda r: -(r["hand_up"] + 0.8 * r["hand_back"] + 0.3 * r["elbow_up"]))
print("CANDIDATES", len(res), "ok", len(ok))
for r in ok[:25]: print("OK", json.dumps(r))
res.sort(key=lambda r: -(r["hand_up"] + 0.8 * r["hand_back"]))
for r in res[:5]: print("TOP", json.dumps(r))
