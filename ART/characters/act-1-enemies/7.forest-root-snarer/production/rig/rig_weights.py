"""Weight composition rules (pure python on per-vertex dicts).

A = heat with every deform bone, B = heat with torso bones only.
- limbs take their weights from A, but clavicle / upper-arm / thigh are masked to the
  shoulder / hip junction so they cannot drag the mossy dome and flank mushrooms;
- everything the limbs do not own goes to the torso-only solve B (smooth along the spine);
- the bark slab incl. the fist is one rigid piece on arm_lower (hand bone = unskinned socket);
- jaw is weighted by rule on the chin (closed mouth, small chin drop only);
- non-rigid weights are Laplacian-smoothed, then max 4 influences, normalised.
"""
import math

TORSO = ("pelvis", "spine_01", "spine_02", "neck", "head")


def smoothstep(e0, e1, x):
    if e0 == e1:
        return 1.0 if x >= e1 else 0.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def dist(a, b):
    return math.sqrt(sum((a[k] - b[k]) ** 2 for k in range(3)))


def axis_param(p, head, tail):
    d = [tail[k] - head[k] for k in range(3)]
    L = math.sqrt(sum(c * c for c in d))
    return sum((p[k] - head[k]) * d[k] for k in range(3)) / L, L


def compose(co, A, B, bones, cfg):
    """co: list of (x,y,z); A,B: list of dicts; bones: {name: (head, tail)}. Returns (weights, rigid_mask, stats)."""
    out, rigid = [], []
    stats = {"rigid_slab": {"L": 0, "R": 0}, "jaw": 0}
    for i, p in enumerate(co):
        a, b = A[i], B[i]
        x, y, z = p
        w = {}
        limb = 0.0
        for side, sgn in (("L", 1), ("R", -1)):
            sx = sgn * x
            clav_t = bones[f"{side}_clavicle"][1]
            m_clav = smoothstep(cfg["clav_r1"], cfg["clav_r0"], dist(p, clav_t)) * smoothstep(0.92, 0.80, z)
            m_upper = smoothstep(0.22, 0.34, sx) * smoothstep(0.92, 0.82, z)
            m_thigh = smoothstep(cfg["thigh_z1"], cfg["thigh_z0"], z) * smoothstep(0.26, 0.36, sx)
            parts = {
                f"{side}_clavicle": a.get(f"{side}_clavicle", 0) * m_clav,
                f"{side}_arm_upper": a.get(f"{side}_arm_upper", 0) * m_upper,
                f"{side}_arm_lower": a.get(f"{side}_arm_lower", 0),
                f"{side}_hand": a.get(f"{side}_hand", 0),
                f"{side}_leg_upper": a.get(f"{side}_leg_upper", 0) * m_thigh,
                f"{side}_leg_lower": a.get(f"{side}_leg_lower", 0),
                f"{side}_foot": a.get(f"{side}_foot", 0),
            }
            for n, v in parts.items():
                if v > 1e-5:
                    w[n] = v
                    limb += v
        if limb > 1.0:
            for n in w:
                w[n] /= limb
            limb = 1.0
        bt = sum(b.values()) or 1.0
        for n, v in b.items():
            w[n] = w.get(n, 0) + (1.0 - limb) * v / bt
        if not b and limb < 1.0:  # torso solve missed this vertex: give remainder to nearest torso bone
            n = min(TORSO, key=lambda t: dist(p, mid(bones[t])))
            w[n] = w.get(n, 0) + 1.0 - limb

        # bark slab incl. the fist: one rigid piece on arm_lower (hand = unskinned contact socket)
        is_rigid = False
        for side, sgn in (("L", 1), ("R", -1)):
            sx = sgn * x
            lo, hd = f"{side}_arm_lower", f"{side}_hand"
            if hd in w:
                w[lo] = w.get(lo, 0) + w.pop(hd)
            own = w.get(lo, 0)
            gate = max(smoothstep(cfg["slab_x0"], cfg["slab_x1"], sx), 1.0 if (z < 0.30 and sx > 0.30 and y < -0.35) else 0.0)
            r = smoothstep(0.30, 0.60, own) * gate
            if r <= 0:
                continue
            w = {n: (1 - r) * v for n, v in w.items()}
            w[lo] = w.get(lo, 0) + r
            if r > 0.999:
                is_rigid = True
                stats["rigid_slab"][side] += 1

        # jaw by rule, taken out of head
        hw = w.get("head", 0)
        if hw > 0:
            j = (smoothstep(cfg["jaw_z1"], cfg["jaw_z0"], z) * smoothstep(-0.50, -0.58, y)
                 * smoothstep(0.24, 0.15, abs(x)))
            if j > 0:
                w["jaw"] = hw * j
                w["head"] = hw * (1 - j)
                stats["jaw"] += 1
        out.append({n: v for n, v in w.items() if v > 1e-4})
        rigid.append(is_rigid)
    return out, rigid, stats


def mid(ht):
    return tuple((ht[0][k] + ht[1][k]) * 0.5 for k in range(3))


def smooth(weights, adjacency, locked, iterations, factor=0.5):
    for _ in range(iterations):
        new = []
        for i, wd in enumerate(weights):
            if locked[i] or not adjacency[i]:
                new.append(wd)
                continue
            acc = {}
            for j in adjacency[i]:
                for n, v in weights[j].items():
                    acc[n] = acc.get(n, 0) + v
            k = len(adjacency[i])
            keys = set(acc) | set(wd)
            new.append({n: (1 - factor) * wd.get(n, 0) + factor * acc.get(n, 0) / k for n in keys})
        weights = new
    return weights


def limit_normalize(weights, limit=4, floor=0.01):
    out = []
    for wd in weights:
        items = sorted(((v, n) for n, v in wd.items() if v >= floor), reverse=True)[:limit]
        if not items:
            items = sorted(((v, n) for n, v in wd.items()), reverse=True)[:1]
        s = sum(v for v, _ in items)
        out.append({n: v / s for v, n in items})
    return out
