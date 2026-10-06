"""Крушение v3, Slam_Drag: путь хвата (план для v3s4_author) — кадры 0–16 и 25 = выгрузка хвата Slam (копии ключей),
17–24 — протяжка: кисти низко впереди справа, тянут цепь назад на ~0,17 м (17–20), выход — к рукояти стойки (= Slam 24).
Кольцо (ось кулака по цепи) — к голове якоря запечки Slam, сдвинутой к хвату на величину протяжки (цепь натянута).
python v3s5dr_plan.py <animation-v3 dir> <out plan.json>"""
import json, math, os, sys
V3, OUT = sys.argv[1], sys.argv[2]
g = json.load(open(os.path.join(V3, "bake", "Pelag_AN_Wreck2_Slam.grip.json")))
bk = json.load(open(os.path.join(V3, "bake", "Pelag_AN_Wreck2_Slam_w13.anchorbake.json")))["samples"]
EX = lambda f: g["samples"][f * g["sub"]]["grip"]
PATH = {17: (0.290, 0.655, 0.420), 18: (0.250, 0.680, 0.385), 19: (0.215, 0.705, 0.345), 20: (0.200, 0.720, 0.320),
        21: (0.215, 0.745, 0.320), 22: (0.255, 0.820, 0.345), 23: (0.290, 0.910, 0.370), 24: (0.318, 1.000, 0.392)}
head = lambda f: min(bk, key=lambda s: abs(s["t"] - f))["p"]
samples = []
for f in range(26):
    gr = list(PATH[f]) if f in PATH else EX(f if f <= 16 else 24)
    h = head(min(f, 24)); d = [a - b for a, b in zip(h, gr)]; n = math.sqrt(sum(c * c for c in d))
    pull = max(0.0, EX(16)[2] - gr[2]) if f in PATH else 0.0
    ring = [b + 1.6 * c / n for b, c in zip(gr, d)]
    samples.append(dict(frame=float(f), grip=[round(c, 5) for c in gr], ring=[round(c, 5) for c in ring], pull=round(pull, 3)))
json.dump(dict(clip="Pelag_AN_Wreck2_Slam_Drag", note=__doc__.splitlines()[0], samples=samples), open(OUT, "w"), indent=1)
print("plan", OUT, len(samples))
