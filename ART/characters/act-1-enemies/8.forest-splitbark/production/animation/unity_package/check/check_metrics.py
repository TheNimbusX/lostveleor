"""Pure numpy measurements on sampled takes (no bpy).

A sampled take is a dict: t (n,) frames, B {bone: (n,4,4) world}, S (n,V,3) skin, float.
"""
import numpy as np

FPS = 30.0


def rot_deg(Ra, Rb):
    """Angle between two 3x3 rotations (columns normalised first), degrees."""
    def norm(R):
        return R / np.linalg.norm(R, axis=0, keepdims=True)
    R = norm(Ra).T @ norm(Rb)
    c = np.clip((np.trace(R) - 1) / 2, -1.0, 1.0)
    return float(np.degrees(np.arccos(c)))


def seam(tk, i0=0, i1=-1):
    """Pose difference between two samples: bone heads (m), bone rotations (deg), skin (m)."""
    pos = max(float(np.linalg.norm(M[i1, :3, 3] - M[i0, :3, 3])) for M in tk["B"].values())
    rot = max(rot_deg(M[i0, :3, :3], M[i1, :3, :3]) for M in tk["B"].values())
    skin = float(np.linalg.norm(tk["S"][i1] - tk["S"][i0], axis=1).max())
    return {"bone_pos_mm": round(pos * 1000, 3), "bone_rot_deg": round(rot, 3), "skin_mm": round(skin * 1000, 3)}


def velocity_seam(tk, dt_samples=1):
    """Loop C1 check: skin velocity entering the end vs leaving the start (m/s)."""
    S, t = tk["S"], tk["t"]
    h = (t[dt_samples] - t[0]) / FPS
    v0 = (S[dt_samples] - S[0]) / h
    v1 = (S[-1] - S[-1 - dt_samples]) / h
    seam_jump = float(np.linalg.norm(v1 - v0, axis=1).max())
    V = (S[1:] - S[:-1]) / h                      # interior: the same jump anywhere inside the cycle
    inner = max(float(np.linalg.norm(V[k + 1] - V[k], axis=1).max()) for k in range(len(V) - 1))
    return {"seam_mps": round(seam_jump, 3), "worst_inside_cycle_mps": round(inner, 3)}


def root_motion(tk):
    R = tk["B"]["root"]
    d = R[:, :3, 3] - R[0, :3, 3]
    rot = max(rot_deg(R[0, :3, :3], R[i, :3, :3]) for i in range(len(R)))
    return {"root_head_start": [round(float(x), 5) for x in R[0, :3, 3]],
            "root_travel_xy_mm": round(float(np.linalg.norm(d[:, :2], axis=1).max()) * 1000, 4),
            "root_travel_z_mm": round(float(np.abs(d[:, 2]).max()) * 1000, 4),
            "root_rot_deg": round(rot, 4)}


def scale_dev(tk):
    worst, who = 0.0, None
    for n, M in tk["B"].items():
        s = np.linalg.norm(M[:, :3, :3], axis=1)
        d = float(np.abs(s - 1.0).max())
        if d > worst:
            worst, who = d, n
    return {"max_bone_scale_dev": round(worst, 6), "bone": who}


def body_drift(tk):
    b = tk["B"]["body"][:, :3, 3]
    return {"body_xy_start_end_mm": round(float(np.linalg.norm(b[-1, :2] - b[0, :2])) * 1000, 2),
            "body_xy_max_from_start_mm": round(float(np.linalg.norm(b[:, :2] - b[0, :2], axis=1).max()) * 1000, 2)}


def ground(tk):
    z = tk["S"][:, :, 2]
    mins = z.min(axis=1)
    i = int(mins.argmin())
    return {"skin_min_z_mm": round(float(mins[i]) * 1000, 2), "at_frame": float(tk["t"][i]),
            "verts_below_minus_1cm": int((z < -0.01).any(axis=0).sum()),
            "end_min_z_mm": round(float(mins[-1]) * 1000, 2)}


def edge_stretch(tk, E, rest_len, dom):
    worst = {"ratio_max": 1.0, "ratio_min": 1.0}
    top = []
    for i in range(len(tk["t"])):
        S = tk["S"][i]
        L = np.linalg.norm(S[E[:, 0]] - S[E[:, 1]], axis=1)
        r = L / np.maximum(rest_len, 1e-6)
        j, k = int(r.argmax()), int(r.argmin())
        if r[j] > worst["ratio_max"]:
            worst["ratio_max"] = float(r[j])
            worst["max_at"] = [float(tk["t"][i]), str(dom[E[j, 0]]), str(dom[E[j, 1]]),
                               round(float(rest_len[j]) * 1000, 1)]
        if r[k] < worst["ratio_min"]:
            worst["ratio_min"] = float(r[k])
            worst["min_at"] = [float(tk["t"][i]), str(dom[E[k, 0]]), str(dom[E[k, 1]])]
        top.append(float(np.percentile(r, 99.9)))
    worst["p999_max"] = round(max(top), 3)
    worst["ratio_max"] = round(worst["ratio_max"], 3)
    worst.update(elongation(tk, E, rest_len, dom))
    worst["ratio_min"] = round(worst["ratio_min"], 3)
    return worst


def rigid_dev(tk, E, rest_len):
    """Edges whose two vertices are 100% on one bone must keep their length (rigid shells, beak, claws)."""
    worst = 0.0
    for i in range(len(tk["t"])):
        S = tk["S"][i]
        L = np.linalg.norm(S[E[:, 0]] - S[E[:, 1]], axis=1)
        worst = max(worst, float(np.abs(L - rest_len).max()))
    return round(worst * 1000, 4)


def elongation(tk, E, rest_len, dom):
    """Absolute edge growth (mm): the real 'stretched sliver' measure (ratios blow up on tiny edges)."""
    grow = np.zeros(len(E))
    at = np.zeros(len(E))
    for i in range(len(tk["t"])):
        S = tk["S"][i]
        g = np.linalg.norm(S[E[:, 0]] - S[E[:, 1]], axis=1) - rest_len
        m = g > grow
        grow[m] = g[m]
        at[m] = tk["t"][i]
    order = np.argsort(-grow)
    pairs, pair_max = {}, {}
    for j in order:
        if grow[j] < 0.02:
            break
        key = "-".join(sorted((str(dom[E[j, 0]]), str(dom[E[j, 1]]))))
        pairs[key] = pairs.get(key, 0) + 1
        pair_max.setdefault(key, [round(float(grow[j]) * 1000, 1), float(at[j])])
    j = int(order[0])
    return {"grow_max_mm": round(float(grow[j]) * 1000, 1), "grow_max_at": float(at[j]),
            "grow_max_pair": [str(dom[E[j, 0]]), str(dom[E[j, 1]])],
            "edges_grow_over_2cm": int((grow > 0.02).sum()), "edges_grow_over_5cm": int((grow > 0.05).sum()),
            "pairs_over_2cm": pairs, "pair_max_mm_at": pair_max}


def planted_runs(z, thresh):
    """Contiguous (cyclic) index runs where z <= thresh. Returns list of index arrays."""
    low = z <= thresh
    n = len(z)
    if low.all():
        return [np.arange(n)]
    start = int(np.argmin(low))  # a lifted sample: runs never straddle it
    order = [(start + k) % n for k in range(n)]
    runs, cur = [], []
    for i in order:
        if low[i]:
            cur.append(i)
        elif cur:
            runs.append(np.array(cur))
            cur = []
    if cur:
        runs.append(np.array(cur))
    return runs


def plant_slide(t, P, runs, v_target, n_frames):
    """For each plant: fitted ground speed (+Y = backward) and slide vs ideal motion at v_target."""
    out = []
    for run in runs:
        tt = t[run].astype(float).copy()
        for k in range(1, len(tt)):          # unwrap the cycle
            while tt[k] < tt[k - 1]:
                tt[k] += n_frames
        sec = (tt - tt[0]) / FPS
        p = P[run]
        if len(run) >= 3:
            speed = float(np.polyfit(sec, p[:, 1], 1)[0])
        else:
            speed = float("nan")
        ideal = p[0] + np.outer(sec, [0.0, v_target, 0.0])
        slide = float(np.linalg.norm((p - ideal)[:, :2], axis=1).max())
        lateral = float(np.abs(p[:, 0] - p[0, 0]).max())
        out.append({"frames": [round(float(tt[0]), 2), round(float(tt[-1]) % n_frames, 2)],
                    "len_frames": round(float(tt[-1] - tt[0]), 2), "speed_mps": round(speed, 3),
                    "slide_vs_target_mm": round(slide * 1000, 2), "lateral_mm": round(lateral * 1000, 2)})
    return out
