"""Покадрово: python v5_dump.py <sim.json> [t0] [t1] — угловая скорость (полная / веретено / крен), скорость головы, режим."""
import json, math, sys
sys.path.insert(0, __file__.rsplit("\\", 1)[0].rsplit("/", 1)[0])
from v5_metrics import ang, twist_y, qrot
d = json.load(open(sys.argv[1], encoding="utf-8")); fr = d["frames"]
t0 = float(sys.argv[2]) if len(sys.argv) > 2 else 0; t1 = float(sys.argv[3]) if len(sys.argv) > 3 else 99
for i in range(1, len(fr)):
    a, b = fr[i - 1], fr[i]
    if not (t0 <= b["t"] <= t1): continue
    dt = b["t"] - a["t"]; ya, yb = qrot(a["q"], (0, 1, 0)), qrot(b["q"], (0, 1, 0))
    sw = math.acos(max(-1, min(1, sum(x * y for x, y in zip(ya, yb))))) / dt
    print("%.3f %-6s %-6s f%5.2f  w %5.1f  shank %5.1f  roll %+6.1f  v %5.1f  p %s  span %.2f/%.2f pen %.3f" % (
        b["t"], b["mode"], b["clip"][-6:], b["frame"], ang(a["q"], b["q"]) / dt, sw, twist_y(a["q"], b["q"]) / dt, b["v"],
        [round(x, 2) for x in b["p"]], b["span"], b["cable"], b.get("pen", 0)))
