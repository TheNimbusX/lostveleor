"""Крушение v3, Swing2: опора пируэта — вертится или едет? blender -b -P v3s4_pivot_measure.py -- <clip> <frame0> <frame1> <out.json>
Поза — ключи клипа из его рабочего .blend; на каждом тике и через ¼ кадра (как играет Build) — вершины правой стопы ниже 12 мм:
центр пятна касания и его сдвиг (скольжение), поворот пятна вокруг вертикали (верчение на подушечке), медиана сдвига вершин
(то, что меряет wk_check: при верчении она = радиус пятна × угол, а не скольжение)."""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector, Quaternion
from s_lib import Body
from wk_rig import Rig
import wk_grip, wk_check

argv = sys.argv[sys.argv.index("--") + 1:]
CLIP, F0, F1, OUTF = argv[0], int(argv[1]), int(argv[2]), argv[3]
V3 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
rig = Rig(); wk_grip.prepare(rig); body = Body(rig.mesh)
blend = os.path.join(V3, "Pelag_Wreck2_%s_v3_Work.blend" % CLIP.replace("Pelag_AN_Wreck2_", ""))
with bpy.data.libraries.load(blend, link=False) as (src, dst):
    dst.actions = [CLIP]
act = bpy.data.actions[CLIP]
fcs = [fc for layer in act.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]


def snap_at(fr):
    base = {k: (v[0].copy(), v[1].copy()) for k, v in rig.snapshot().items()}
    vals = {(fc.data_path, fc.array_index): fc.evaluate(fr) for fc in fcs}
    out = {}
    for name in base:
        loc = Vector([vals.get(('pose.bones["%s"].location' % name, i), base[name][0][i]) for i in range(3)])
        q = Quaternion([vals.get(('pose.bones["%s"].rotation_quaternion' % name, i), base[name][1][i]) for i in range(4)]).normalized()
        out[name] = (loc, q)
    return out


keys = {f: snap_at(f) for f in range(F0, F1 + 1)}
ids = [i for i, p in enumerate(body.vpart) if p == "RightFoot"]
lows = {}
for k in range(int((F1 - F0) * 4) + 1):
    x = F0 + k / 4.0
    rig.restore(wk_check.interp([keys[f] for f in range(F0, F1 + 1)], x - F0))
    vs = body.verts()
    lows[x] = {i: vs[i].copy() for i in ids if vs[i].z < 0.012}


def compare(xa, xb):
    A, Bv = lows[xa], lows[xb]; common = [i for i in Bv if i in A]
    if len(common) < 4: return None
    ca = sum((A[i] for i in common), Vector()) / len(common); cb = sum((Bv[i] for i in common), Vector()) / len(common)
    angs = []
    for i in common:
        a = (A[i] - ca).xy; b = (Bv[i] - cb).xy
        if a.length > 0.008 and b.length > 0.008: angs.append(math.degrees(math.atan2(a.x * b.y - a.y * b.x, a.dot(b))))
    angs.sort(); med = sorted((Bv[i] - A[i]).xy.length for i in common)
    return dict(x=xb, step=xb - xa, n=len(common), centre_mm=round((cb - ca).xy.length * 1000 / 0.98881, 2),
                twist_deg=round(angs[len(angs) // 2], 1) if angs else 0.0, median_mm=round(med[len(med) // 2] * 1000 / 0.98881, 2))


xs = sorted(lows)
quarter = [r for r in (compare(a, b) for a, b in zip(xs, xs[1:])) if r]
tick = [r for r in (compare(float(f - 1), float(f)) for f in range(F0 + 1, F1 + 1)) if r]
json.dump(dict(tick=tick, quarter=quarter), open(OUTF, "w"), indent=1)
for r in tick: print("PIVOT tick %4.1f n %2d centre %5.1f mm twist %6.1f deg median %5.1f mm" % (r["x"], r["n"], r["centre_mm"], r["twist_deg"], r["median_mm"]))
print("PIVOT quarter: centre max %.1f mm, median max %.1f mm" % (max(r["centre_mm"] for r in quarter), max(r["median_mm"] for r in quarter)))
