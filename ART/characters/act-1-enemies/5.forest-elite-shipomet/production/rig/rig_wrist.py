"""Wrist weight blend (r03 verification fix).

The long arm spikes are rigid to LeftHand/RightHand, and the red spike component reached up into the wrist band,
so a spike vertex (Hand 1.0) could sit next to a bark vertex (ForeArm ~1.0): one edge carried the whole wrist bend
and stretched up to 16 cm in Burst/Death (a thin sliver at the wrist). Here the Hand share of the Hand+ForeArm
membership is re-solved as a harmonic (Laplacian) blend across the wrist band on the welded mesh graph:
  fixed Hand share = 1 beyond the spike root (s >= root_s along the spike axis from the wrist joint),
  fixed current share on the elbow side (s <= fore_s) and outside the zone,
  free in between. Other bones' weights are untouched, so limbs keep their membership and weight sums.
"""
import numpy as np


def weld_ids(co, tol=1e-5):
    keys = np.round(co / tol).astype(np.int64)
    _, inv = np.unique(keys, axis=0, return_inverse=True)
    return inv.reshape(-1)


def blend_wrist(co, edges, W, hand, fore, wrist, tip, fore_s=-0.10, root_s=0.08, zone_r=0.30, iters=600):
    """Return (W_new, info). co: (n,3) rest positions; edges: (m,2); W: (n,bones); hand/fore: column indices."""
    W = W.copy()
    axis = (tip - wrist) / np.linalg.norm(tip - wrist)
    s = (co - wrist) @ axis
    memb = W[:, hand] + W[:, fore]
    armv = (memb > 0.3) & (np.linalg.norm(co - wrist, axis=1) < zone_r)
    h = np.where(memb > 1e-9, W[:, hand] / np.maximum(memb, 1e-9), 0.0)
    free = armv & (s > fore_s) & (s < root_s)
    # welded graph restricted to arm vertices
    weld = weld_ids(co)
    nw = weld.max() + 1
    hw = np.zeros(nw)
    cnt = np.zeros(nw)
    np.add.at(hw, weld, h)
    np.add.at(cnt, weld, 1)
    hw /= np.maximum(cnt, 1)
    freew = np.zeros(nw, bool)
    freew[weld[free]] = True
    armw = np.zeros(nw, bool)
    armw[weld[armv]] = True
    a, b = weld[edges[:, 0]], weld[edges[:, 1]]
    keep = (a != b) & armw[a] & armw[b]
    a, b = a[keep], b[keep]
    deg = np.zeros(nw)
    np.add.at(deg, a, 1)
    np.add.at(deg, b, 1)
    for _ in range(iters):
        acc = np.zeros(nw)
        np.add.at(acc, a, hw[b])
        np.add.at(acc, b, hw[a])
        new = np.where(deg > 0, acc / np.maximum(deg, 1), hw)
        hw = np.where(freew, new, hw)
    hn = np.where(free, hw[weld], h)
    W[:, hand] = memb * hn
    W[:, fore] = memb * (1 - hn)
    jump_before = np.abs(h[edges[:, 0]] - h[edges[:, 1]])
    jump_after = np.abs(hn[edges[:, 0]] - hn[edges[:, 1]])
    info = {"free_vertices": int(free.sum()), "fore_s": fore_s, "root_s": root_s,
            "max_edge_share_jump_before": round(float(jump_before[armv[edges[:, 0]]].max()), 3),
            "max_edge_share_jump_after": round(float(jump_after[armv[edges[:, 0]]].max()), 3)}
    return W, info
