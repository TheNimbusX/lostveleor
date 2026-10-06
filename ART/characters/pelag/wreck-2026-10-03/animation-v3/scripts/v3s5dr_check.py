"""Крушение v3, Slam_Drag: протяжка по запечке — голова на земле (нижняя точка оболочки 68 точек над полом), цепь натянута
(кольцо–хват ≥ L − 1 см), скорость головы; кадры from..to (по умолчанию 16..22 — удержание и протяжка, Sim держит героя).
python v3s5dr_check.py <anchorbake.json> [from] [to] [--json out]"""
import json, math, re, sys
A = sys.argv[1]; lo = float(sys.argv[2]) if len(sys.argv) > 2 else 16.0; hi = float(sys.argv[3]) if len(sys.argv) > 3 else 22.0
out = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
HULL = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Weapons\Pelag\AnchorDemo\AnchorHeadShape.asset"
b = json.load(open(A, encoding="utf-8")); rig = b["rig"]; S = rig["headSize"]; L = rig["chainLength"]
pts = [(float(x) * S, float(y) * S, float(z) * S) for x, y, z in re.findall(r"\{x:\s*(-?[0-9.eE+-]+),\s*y:\s*(-?[0-9.eE+-]+),\s*z:\s*(-?[0-9.eE+-]+)\}", open(HULL).read())]
eye = rig["eyeLocal"]


def rot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + y * tz - z * ty, vy + w * ty + z * tx - x * tz, vz + w * tz + x * ty - y * tx)


rows = []
for s in b["samples"]:
    if not lo - 1e-6 <= s["t"] <= hi + 1e-6: continue
    low = min(s["p"][1] + rot(s["q"], p)[1] for p in pts) - rig.get("ground", 0.0)
    ring = [a + c for a, c in zip(s["p"], rot(s["q"], eye))]
    span = math.dist(ring, s["grip"])
    rows.append(dict(t=s["t"], low=round(low, 4), span=round(span, 4), taut=s["taut"], speed=round(math.sqrt(sum(c * c for c in s["v"])), 2),
                     head=[round(c, 3) for c in s["p"]]))
worst = max(r["low"] for r in rows); slack = min(r["span"] for r in rows) - L
moved = math.dist(rows[0]["head"], rows[-1]["head"])
print("DRAGCHK %.2f–%.2f: голова над полом макс %.3f м (нижняя точка), цепь кольцо–хват − L мин %+.3f м, натянута %d/%d сэмплов, голова прошла %.2f м" % (
    lo, hi, worst, slack, sum(r["taut"] for r in rows), len(rows), moved))
for r in rows[::2]: print("  t %5.2f низ %.3f провис %+.3f taut %d v %.2f" % (r["t"], r["low"], r["span"] - L, r["taut"], r["speed"]))
if out: json.dump(dict(range=[lo, hi], max_low=worst, min_span_minus_L=round(slack, 4), moved=round(moved, 3), rows=rows), open(out, "w"), indent=1)
