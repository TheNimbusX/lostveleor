"""Крушение v3, формы: хват braced-клипа против хвата базы (оба — выгрузка anchor_grip_export, оси корня Unity).
python v3s5_corr.py <braced.grip.json> <base.grip.json> <out corr.json> [<corr.json, с которым собрано тело>] [кадры без поправки]
→ поправка += остаток в целых кадрах; печать: худший остаток в кадрах и по всем сэмплам (8 на кадр); код 0, если кадры ≤ 2 мм."""
import json, math, os, sys
gb, gs, out = sys.argv[1:4]
used = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] else None
skip = {int(x) for x in sys.argv[5].split(",")} if len(sys.argv) > 5 and sys.argv[5] else set()
b = json.load(open(gb)); s = json.load(open(gs))
n, sub = b["frames"], b["sub"]
assert n == s["frames"] and sub == s["sub"], "кадры/сэмплы не совпадают"
old = json.load(open(used))["corr"] if used and os.path.exists(used) else [[0.0] * 3 for _ in range(n + 1)]
res = [[x - y for x, y in zip(b["samples"][f * sub]["grip"], s["samples"][f * sub]["grip"])] for f in range(n + 1)]
L = lambda v: math.sqrt(sum(c * c for c in v))
allres = [L([x - y for x, y in zip(p["grip"], q["grip"])]) for p, q in zip(b["samples"], s["samples"])]
sup = [L([x - y for x, y in zip(p["support"], q["support"])]) for p, q in zip(b["samples"], s["samples"])]
corr = [list(o) if f in skip else [o_ + r_ for o_, r_ in zip(o, r)] for f, (o, r) in enumerate(zip(old, res))]
worst = max(L(r) for f, r in enumerate(res) if f not in skip)
k = max(range(len(allres)), key=lambda i: allres[i])
json.dump(dict(corr=corr, residual=[[round(c, 4) for c in r] for r in res], worst_frames=worst, worst_samples=allres[k],
               worst_sample_at=k / sub, support_worst=max(sup), skip=sorted(skip)), open(out, "w"), indent=1)
print("GRIPDIFF кадры %.4f м, все сэмплы %.4f м (кадр %.3f), опорная кисть %.4f м" % (worst, allres[k], k / sub, max(sup)))
sys.exit(0 if worst <= 0.002 else 1)
