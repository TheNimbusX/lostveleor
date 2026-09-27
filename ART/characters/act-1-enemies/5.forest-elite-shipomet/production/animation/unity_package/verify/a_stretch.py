"""Offline (system python): edge stretch per take with absolute thresholds, and per-frame vertex speed peaks.

python a_stretch.py [take]
"""
import json
import sys
from pathlib import Path

import numpy as np

O = Path(__file__).resolve().parent / "out"
E = np.load(O / "edges.npy")
rest = np.load(O / "rest.npy")
dom = np.load(O / "dominant.npy", allow_pickle=True)
W = np.load(O / "weights.npy")
G = json.loads((O / "groups.json").read_text())["groups"]
L0 = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
takes = [sys.argv[1]] if len(sys.argv) > 1 else ["Idle", "Walk", "LineCast", "Burst", "Shot", "Hit", "Death"]
res = {}
for t in takes:
    co = np.load(O / f"co_ForestThorncaster_{t}.npy")
    L = np.linalg.norm(co[:, E[:, 0]] - co[:, E[:, 1]], axis=2)
    d = L - L0
    big = L0 > 0.004
    r = np.where(big, L / np.maximum(L0, 1e-9), 1)
    fi, ei = np.unravel_index(np.argmax(np.where(big, r, 0)), r.shape)
    fj, ej = np.unravel_index(np.argmax(np.abs(d)), d.shape)
    grow = d.max(0)
    n2 = int((grow > 0.02).sum())
    n5 = int((grow > 0.05).sum())
    shr = d.min(0)
    top = np.argsort(-np.abs(d).max(0))[:6]
    res[t] = {"stretch_ratio_max_L0>4mm": [round(float(r[fi, ei]), 2), int(fi), str(dom[E[ei, 0]]), round(float(L0[ei]) * 1000, 1)],
              "abs_len_change_max_cm": [round(float(d[fj, ej]) * 100, 2), int(fj), str(dom[E[ej, 0]]), str(dom[E[ej, 1]]), round(float(L0[ej]) * 100, 2)],
              "edges_grow_gt_2cm": n2, "edges_grow_gt_5cm": n5, "edges_shrink_gt_2cm": int((shr < -0.02).sum()),
              "top_edges": [[int(e), round(float(np.abs(d[:, e]).max()) * 100, 2), int(np.abs(d[:, e]).argmax()), str(dom[E[e, 0]]),
                             [G[k] + f":{W[E[e, 0], k]:.2f}" for k in np.nonzero(W[E[e, 0]] > 0.05)[0]],
                             [round(float(x), 3) for x in rest[E[e, 0]]]] for e in top]}
    print(t, json.dumps(res[t], indent=None))
(O / "stretch.json").write_text(json.dumps(res, indent=1))
