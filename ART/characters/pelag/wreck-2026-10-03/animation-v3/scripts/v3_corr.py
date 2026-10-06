"""Поправка цели гнезда: corr_new = corr_used + (хват из выгруженного FBX − план). Код выхода 0 — уже ≤ 3 мм везде.
python v3_corr.py <grip.json> <plan.json> <out corr.json> [<corr.json, с которым собрано тело>]"""
import json, math, os, sys
grip, plan, out = sys.argv[1:4]
g = json.load(open(grip)); p = json.load(open(plan))
P = {round(s["frame"] * 4): s for s in p["samples"]}
used = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] else None
old = json.load(open(used))["corr"] if used and os.path.exists(used) else [[0.0] * 3 for _ in range(13)]
res = [[a - b for a, b in zip(g["samples"][f * g["sub"]]["grip"], P[4 * f]["grip"])] for f in range(13)]
worst = max(math.sqrt(sum(c * c for c in r)) for r in res)
json.dump(dict(corr=[[o + r for o, r in zip(oc, rc)] for f, (oc, rc) in enumerate(zip(old, res))],
               residual=[[round(c, 4) for c in r] for r in res], worst=worst), open(out, "w"), indent=1)
print("CORR worst residual %.4f m (frames 1..12)" % worst)
sys.exit(0 if worst <= 0.003 else 1)
