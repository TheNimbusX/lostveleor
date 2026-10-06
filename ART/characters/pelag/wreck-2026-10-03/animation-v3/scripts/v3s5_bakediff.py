"""Крушение v3, формы: запечка braced против запечки базы — наибольшее расхождение центра головы (м) и скорости в контакте.
python v3s5_bakediff.py <braced.anchorbake.json> <base.anchorbake.json> [до кадра]"""
import json, math, sys
a = json.load(open(sys.argv[1], encoding="utf-8")); b = json.load(open(sys.argv[2], encoding="utf-8"))
upto = float(sys.argv[3]) if len(sys.argv) > 3 else 1e9
sa = {round(s["t"], 3): s for s in a["samples"]}; sb = {round(s["t"], 3): s for s in b["samples"]}
common = sorted(t for t in sa if t in sb and t <= upto + 1e-9)
d = [(math.dist(sa[t]["p"], sb[t]["p"]), t) for t in common]
w = max(d)
va, vb = a["validation"], b["validation"]
print("BAKEDIFF голова: макс %.4f м (кадр %.2f), среднее %.4f м по %d сэмплам; контакт %s / база %s; скачок %.3f / %.3f; pass %s" % (
    w[0], w[1], sum(x for x, _ in d) / len(d), len(d), a.get("contacts"), b.get("contacts"), va["maxJump"], vb["maxJump"], va["pass"]))
