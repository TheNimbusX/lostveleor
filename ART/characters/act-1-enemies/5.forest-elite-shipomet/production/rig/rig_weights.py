"""Weight operations on a dense matrix W (verts x bones, bone order = rig_skeleton.ORDER)."""
import numpy as np
from mathutils import kdtree


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def transfer_by_position(dst_co, src_co, src_w):
    """Copy weights from coincident source vertices (Meshy mesh == candidate positions)."""
    kd = kdtree.KDTree(len(src_co))
    for i, p in enumerate(src_co):
        kd.insert(p, i)
    kd.balance()
    out = np.zeros((len(dst_co), src_w.shape[1]))
    exact = 0
    for i, p in enumerate(dst_co):
        hits = kd.find_n(p, 4)
        if hits[0][2] < 1e-6:
            same = [h[1] for h in hits if h[2] < 1e-6]
            out[i] = src_w[same].mean(0)
            exact += 1
        else:
            ws = np.array([1.0 / max(h[2], 1e-4) for h in hits])
            out[i] = (src_w[[h[1] for h in hits]] * ws[:, None]).sum(0) / ws.sum()
    return out, exact


def chain_param(co, pts):
    """Arc-length parameter of the closest point on the polyline pts for every vertex."""
    pts = np.asarray(pts, dtype=np.float64)
    seg = pts[1:] - pts[:-1]
    lens = np.linalg.norm(seg, axis=1)
    starts = np.concatenate([[0.0], np.cumsum(lens)])
    best_d = np.full(len(co), np.inf)
    best_s = np.zeros(len(co))
    for k in range(len(seg)):
        t = np.clip(((co - pts[k]) @ seg[k]) / (lens[k] ** 2), 0.0, 1.0)
        # let the first and last segments extend past their ends
        if k == 0:
            t = np.minimum(((co - pts[k]) @ seg[k]) / (lens[k] ** 2), 1.0)
        if k == len(seg) - 1:
            t = np.maximum(((co - pts[k]) @ seg[k]) / (lens[k] ** 2), 0.0)
        proj = pts[k] + t[:, None] * seg[k]
        d = np.linalg.norm(co - proj, axis=1)
        s = starts[k] + t * lens[k]
        better = d < best_d
        best_d[better] = d[better]
        best_s[better] = s[better]
    return best_s, starts, best_d


def chain_weights(s, joint_s, half_widths):
    """Partition of unity along the chain; joint_s are the interior joint positions."""
    n = len(joint_s) + 1
    C = np.zeros((len(s), n))
    acc = np.ones(len(s))
    for k in range(n - 1):
        f = smoothstep(joint_s[k] - half_widths[k], joint_s[k] + half_widths[k], s)
        C[:, k] = acc * (1 - f)
        acc = acc * f
    C[:, n - 1] = acc
    return C


def redistribute(W, cols, pts, half_widths, co):
    """Keep the limb's total Meshy membership, re-split it along the new joint chain."""
    memb = W[:, cols].sum(1)
    s, starts, _ = chain_param(co, pts)
    C = chain_weights(s, starts[1:-1], half_widths)
    W[:, cols] = memb[:, None] * C
    return memb, s


def blend_to_bone(W, amount, col):
    amount = np.clip(amount, 0, 1)[:, None]
    target = np.zeros_like(W)
    target[:, col] = 1.0
    W[:] = (1 - amount) * W + amount * target


def weld_average(W, weld):
    sums = np.zeros((weld.max() + 1, W.shape[1]))
    np.add.at(sums, weld, W)
    cnt = np.bincount(weld)
    return sums[weld] / cnt[weld][:, None]


def cleanup(W, limit=4, min_w=0.01):
    W = np.where(W < min_w, 0.0, W)
    if W.shape[1] > limit:
        order = np.argsort(-W, axis=1)
        drop = order[:, limit:]
        np.put_along_axis(W, drop, 0.0, axis=1)
    tot = W.sum(1, keepdims=True)
    return W / np.maximum(tot, 1e-12), int((tot[:, 0] < 1e-9).sum())
