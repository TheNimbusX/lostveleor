"""Бросок якоря: перебор посадки кистей на цепи (поза-ключ AT_KEY из at_keys) без пересечений рук.
blender -b --factory-startup -P at_search.py -- <clip> <frame>
Перебирает: поперечное место цепи (l левой), вынос правой (off), сдвиг правой вбок от цепи (dl), локоть правой."""
import bpy, sys, os, math, copy, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from at_rig import Rig
import at_arm, at_fingers, at_check
from at_pose import apply
from at_keys import clips as clip_keys

argv = sys.argv[sys.argv.index("--") + 1:]
clip, frame = "Pelag_AN_AnchorThrow_" + argv[0], int(argv[1])
rig = Rig(); at_arm.prepare(rig); at_fingers.prepare(rig)
body = Body(rig.mesh)
mesh = at_check.Mesh(rig, body)
K = clip_keys(rig.B)[clip][frame]
PAIRS = [(("RightHand",), "LeftHand"), (("RightForeArm",), "LeftHand"), (("RightHand", "RightForeArm"), "LeftForeArm"),
         (("LeftHand", "LeftForeArm"), "torso"), (("RightHand", "RightForeArm"), "torso")]
POLES = {"out": (-0.05, -0.75, -0.65), "outup": (0.0, -0.85, -0.2), "down": (-0.1, -0.5, -0.85), "auto": None}
base = K["habs"]["Left"]; off0 = K["offR"][0]
dls = [float(x) for x in os.environ.get("AT_DL", "0,-0.03,-0.05").split(",")]
ls = [float(x) for x in os.environ.get("AT_LL", "-0.14,-0.10,-0.06,-0.02").split(",")]
offs = [float(x) for x in os.environ.get("AT_OFF", "0.14,0.18,0.22").split(",")]
fs = [float(x) for x in os.environ.get("AT_LF", str(base[0])).split(",")]
us = [float(x) for x in os.environ.get("AT_LU", str(base[2])).split(",")]
res = []
for lu, lf, ll, off, dl, pn in itertools.product(us, fs, ls, offs, dls, POLES):
    p = copy.deepcopy(K); p.pop("fixed", None)
    p["habs"]["Left"] = (lf, ll, lu)
    p["offR"] = (off, dl, K["offR"][2])
    if os.environ.get("AT_LEAN"): p["lean"] = float(os.environ["AT_LEAN"])
    if POLES[pn] is None: p["wp"] = 0.0
    else: p["poles"]["Right"] = POLES[pn]; p["wp"] = 1.0
    apply(rig, p)
    m = at_check.pose_metrics(rig)
    vs = body.verts()
    hits = []
    for a, b in PAIRS:
        tri, deep, worst, _ = body.contact(list(a), b, vs)
        hits.append((tri, deep, round(worst, 3)))
    score = sum(h[0] + 3 * h[1] for h in hits[1:]) + int(300 * max(0.0, hits[0][2] - 0.015)) + (200 if m["reachR"] > 0.985 else 0) + (200 if m["reachL"] > 0.97 else 0)
    res.append((score, lu, lf, ll, off, dl, pn, m["reachR"], m["reachL"], hits))
res.sort(key=lambda r: r[0])
for r in res[:25]:
    print("SRCH score %4d lu %.2f lf %.2f ll %+.2f off %.2f dl %+.2f pole %-5s reachR %.2f L %.2f %s" % r)
