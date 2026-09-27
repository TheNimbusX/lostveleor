"""Final skin weights for the Splitter from heat weights + shell labels (numpy only).

W is (verts, bones) with columns in `names` order. Shell verts become 100% their shell bone;
body verts within SHELL_BAND_M of a shell crease blend toward the shell (smoothstep), scaled by
how much of the vertex belongs to torso bones, so head/neck/legs keep their own chains.
"""
import numpy as np

import rig_spec as spec
import segment as seg


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def limit_normalize(W, limit=4):
    W = W.copy()
    if W.shape[1] > limit:
        idx = np.argsort(-W, axis=1)[:, limit:]
        np.put_along_axis(W, idx, 0.0, axis=1)
    W[W < 1e-3] = 0.0
    s = W.sum(1, keepdims=True)
    return W / np.maximum(s, 1e-12)


def harmonic_shell_band(co, edges, length, labels, W, col):
    """Harmonic (1/length Laplacian) interpolation of shell influence over the body verts near the
    creases. Dirichlet: shell verts = 1 for their own shell; anchors = 0 (verts farther than
    SHELL_BAND_M from both shells, or dominated by neck/head). Spreads the unavoidable stretch
    evenly across the V floor and the belly pockets instead of piling it onto the crease edges."""
    n = len(co)
    dL = seg.geodesic_from_boundary(n, edges, length, labels, seg.SHELL_L)
    dR = seg.geodesic_from_boundary(n, edges, length, labels, seg.SHELL_R)
    headish = sum(W[:, col[b]] for b in spec.HEAD_BONES)
    body = labels == seg.BODY
    free = body & (np.minimum(dL, dR) < spec.SHELL_BAND_M) & (headish < 0.5)
    a, b = edges[:, 0], edges[:, 1]
    c = 1.0 / np.maximum(length, 1e-4)
    deg = np.zeros(n)
    np.add.at(deg, a, c)
    np.add.at(deg, b, c)
    fm = free.astype(float)

    def lap(x):  # graph Laplacian (D - A) with 1/length weights
        return deg * x - (np.bincount(a, c * x[b], n) + np.bincount(b, c * x[a], n))

    out, iters = {}, {}
    for lab, bone in ((seg.SHELL_L, "shell_L"), (seg.SHELL_R, "shell_R")):
        hb = (labels == lab).astype(float) * (1 - fm)  # Dirichlet values
        rhs = -lap(hb) * fm
        x = np.zeros(n)
        r = rhs - lap(x) * fm
        p = r.copy()
        rr = r @ r
        for it in range(5000):  # conjugate gradient on the free block
            Ap = lap(p) * fm
            alpha = rr / max(p @ Ap, 1e-30)
            x += alpha * p
            r -= alpha * Ap
            rr_new = r @ r
            if rr_new < 1e-18:
                break
            p = r + (rr_new / rr) * p
            rr = rr_new
        h = np.clip(hb + x * fm, 0.0, 1.0)
        h[labels != seg.BODY] = 0.0
        out[bone] = h
        iters[bone] = it + 1
    total = out["shell_L"] + out["shell_R"]
    over = total > 1.0
    for bone in out:
        out[bone][over] /= total[over]
    return out, {"free_verts": int(free.sum()), "iterations": iters, "radius_m": spec.SHELL_BAND_M}


def final_weights(co, tris, heat, names, crease_gain=12.0):
    col = {n: i for i, n in enumerate(names)}
    labels, _, edges, length = seg.segment(co, tris, crease_gain)
    labels = seg.clean_labels(labels, edges)
    W = heat.copy()
    W[:, col["shell_L"]] = 0.0
    W[:, col["shell_R"]] = 0.0
    s = W.sum(1, keepdims=True)
    W = np.where(s > 1e-9, W / np.maximum(s, 1e-12), 0.0)
    # vertices the heat solver missed: nearest body bone by index of spine column
    W[s[:, 0] <= 1e-9, col["body"]] = 1.0
    band, band_info = harmonic_shell_band(co, edges, length, labels, W, col)
    total = band["shell_L"] + band["shell_R"]
    W = W * (1 - total)[:, None]
    for b, w in band.items():
        W[:, col[b]] += w
    # rigid shells
    for lab, bone in ((seg.SHELL_L, "shell_L"), (seg.SHELL_R, "shell_R")):
        m = labels == lab
        W[m] = 0.0
        W[m, col[bone]] = 1.0
    W = limit_normalize(W, 4)
    stats = {
        "labels": {"body": int((labels == 0).sum()), "shell_L": int((labels == 1).sum()), "shell_R": int((labels == 2).sum())},
        "band_verts": {b: int((w > 1e-3).sum()) for b, w in band.items()},
        "band": band_info,
    }
    return W, labels, stats
