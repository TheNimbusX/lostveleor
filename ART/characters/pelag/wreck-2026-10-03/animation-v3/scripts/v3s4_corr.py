"""Поправка цели гнезда (как v3s3_corr.py) + полукадры: остаток экспорта хвата и плана в f и f + 0,5 (ключи на полкадра).
python v3s4_corr.py <grip.json> <plan.json> <out corr.json> [<corr.json, с которым собрано тело>] [кадры без IK] [кадры с ключом f+0,5]
→ код 0, если ≤ 3 мм везде, кроме кадров без IK (ARM_INTERP/FREE: кисть сознательно не на пути)."""
import json, math, os, sys
grip, path, out = sys.argv[1:4]
used = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] else None
skip = {int(x) for x in sys.argv[5].split(",")} if len(sys.argv) > 5 and sys.argv[5] else set()
halfs = {int(x) for x in sys.argv[6].split(",")} if len(sys.argv) > 6 and sys.argv[6] else set()
g = json.load(open(grip)); J = json.load(open(path))
n, sub = g["frames"], g["sub"]
S = {round(s["frame"] * 4): s["grip"] for s in J["samples"]} if "samples" in J else {4 * f: g_ for f, g_ in enumerate(J["frames"])}
old = json.load(open(used)) if used and os.path.exists(used) else {}
oc = old.get("corr", [[0.0] * 3 for _ in range(n + 1)]); oh = old.get("corr_half", {})
res = [[a - b for a, b in zip(g["samples"][f * sub]["grip"], S[4 * f])] for f in range(n + 1)]
resh = {str(f): [a - b for a, b in zip(g["samples"][f * sub + sub // 2]["grip"], S[4 * f + 2])] for f in range(n) if f in halfs}
corr = [[o + r for o, r in zip(c, rr)] for c, rr in zip(oc, res)]
ch = {k: [o + r for o, r in zip(oh.get(k, [(a + b) / 2 for a, b in zip(oc[int(k)], oc[int(k) + 1])]), rr)] for k, rr in resh.items()}
L = lambda v: math.sqrt(sum(c * c for c in v))
worst = max(L(r) for f, r in enumerate(res) if f not in skip)
worsth = max([L(r) for k, r in resh.items() if int(k) not in skip and int(k) + 1 not in skip] or [0.0])
json.dump(dict(corr=corr, corr_half=ch, residual=[[round(c, 4) for c in r] for r in res],
               residual_half={k: [round(c, 4) for c in r] for k, r in resh.items()}, worst=worst, worst_half=worsth), open(out, "w"), indent=1)
print("CORR worst residual %.4f m (полукадры %.4f m, без кадров %s)" % (worst, worsth, sorted(skip)))
sys.exit(0 if max(worst, worsth) <= 0.003 else 1)
