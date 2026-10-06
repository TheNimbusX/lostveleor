"""Последовательности кадров для v4_render: из выхода wreck4sim (якорь) и сабельная серия в родном темпе.
python v4_seq.py wreck <sim.json> <out.json> [every]      — кадры симуляции (60/с), every=2 → 30/с
python v4_seq.py sabre <out.json> [fps] [yaw2_deg] [pad_ticks]  — Sabre1 0–8, Sabre2 0–8, Sabre3 0–22 (выпад 0,6 м за тики 3–7)
"""
import json, math, sys
K = 0.98881
B = lambda u: [-u[0] / K, -u[2] / K, u[1] / K]


def wreck(sim, out, every=1):
    d = json.load(open(sim))
    fr = []
    for f in d["frames"][::every]:
        fr.append(dict(mode="wreck", clip=f["clip"], frame=f["frame"], root=B(f["root"]), yaw=-f["yaw"], p=f["p"], q=f["q"],
                       grip=f["grip"], ring=f["ring"], cable=f["cable"], span=f["span"], t=f["t"],
                       amode=f.get("mode", ""), **{k: f[k] for k in ("h0", "h1", "chain") if k in f}))
    json.dump(dict(frames=fr), open(out, "w"))
    print("seq", out, len(fr))


def sabre(out, fps=60, yaw2=0.0, pad=0):
    parts = [("Pelag_AN_Sabre1", 0, 8, 0.0), ("Pelag_AN_Sabre2", 0, 8, math.radians(yaw2)), ("Pelag_AN_Sabre3", 0, 22, math.radians(yaw2))]
    fr = []; t = 0.0; base = [0.0, 0.0]; yaw_prev = 0.0
    for clip, a, b, yaw in parts:
        n = int(round((b - a) * fps / 30))
        for i in range(n + (1 if clip.endswith("3") else 0)):
            f = a + i * 30 / fps
            u = min(1.0, (f - a) / 3.0); u = u * u * (3 - 2 * u)
            y = yaw_prev + (yaw - yaw_prev) * u
            fwd = 0.6 * min(1.0, max(0.0, (f - 3) / 4)) if clip.endswith("3") else 0.0
            rx, rz = base[0] + math.sin(yaw) * fwd, base[1] + math.cos(yaw) * fwd
            fr.append(dict(mode="sabre", clip=clip, frame=f, root=B([rx, 0, rz]), yaw=-y, t=t))
            t += 1 / fps
        yaw_prev = yaw
    for i in range(int(pad * fps / 30)): fr.append(dict(fr[-1], t=fr[-1]["t"] + 1 / fps))
    json.dump(dict(frames=fr), open(out, "w"))
    print("seq", out, len(fr))


if sys.argv[1] == "wreck": wreck(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 1)
else: sabre(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 60, float(sys.argv[4]) if len(sys.argv) > 4 else 0.0,
            int(sys.argv[5]) if len(sys.argv) > 5 else 0)
