"""Поправка цели гнезда (как v3s2_corr.py; путь — из plan.json (сэмплы frame/grip) или path.json (frames)).
python v3s3_corr.py <grip.json> <plan|path.json> <out corr.json> [<corr.json, с которым собрано тело>]  → код 0, если ≤ 3 мм везде."""
import json, math, os, sys
grip, path, out = sys.argv[1:4]
g = json.load(open(grip)); J = json.load(open(path))
n = g["frames"]
P = [s["grip"] for s in J["samples"] if abs(s["frame"] - round(s["frame"])) < 1e-6] if "samples" in J else J["frames"]
used = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] else None
old = json.load(open(used))["corr"] if used and os.path.exists(used) else [[0.0] * 3 for _ in range(n + 1)]
res = [[a - b for a, b in zip(g["samples"][f * g["sub"]]["grip"], P[f])] for f in range(n + 1)]
worst = max(math.sqrt(sum(c * c for c in r)) for r in res)
json.dump(dict(corr=[[o + r for o, r in zip(oc, rc)] for oc, rc in zip(old, res)],
               residual=[[round(c, 4) for c in r] for r in res], worst=worst), open(out, "w"), indent=1)
print("CORR worst residual %.4f m" % worst)
sys.exit(0 if worst <= 0.003 else 1)
