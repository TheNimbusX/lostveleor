"""Take-specific contract measurements (walk plants, bite contact, death release, pop hop). numpy only."""
import numpy as np

import check_metrics as cm

LEGS = ("front_L", "front_R", "hind_L", "hind_R")
GROUND_EPS = 0.005   # a sole this close to z=0 counts as on the ground


def foot_sets(rest_S, dom):
    out = {}
    for leg in LEGS:
        idx = np.where(dom == "leg_%s_foot" % leg)[0]
        z = rest_S[idx, 2]
        out[leg] = {"all": idx, "sole": idx[z <= z.min() + 0.015]}
    return out


def soles(tk, feet):
    """Per leg: sole centroid (n,3) and lowest foot z (n,)."""
    S = tk["S"]
    return {leg: (S[:, f["sole"]].mean(axis=1), S[:, f["all"], 2].min(axis=1)) for leg, f in feet.items()}


def per_frame(tk, arr, nd=3):
    idx = np.where(np.isclose(tk["t"] % 1.0, 0.0))[0]
    return [round(float(arr[i]), nd) for i in idx]


def shell_angles(tk, rest):
    def rel(Bsp, Bsh):
        a = Bsp[:3, :3] / np.linalg.norm(Bsp[:3, :3], axis=0)
        b = Bsh[:3, :3] / np.linalg.norm(Bsh[:3, :3], axis=0)
        return a.T @ b
    out = {}
    for side in ("shell_L", "shell_R"):
        r0 = rel(rest["B"]["spine"], rest["B"][side])
        ang = [cm.rot_deg(r0, rel(tk["B"]["spine"][i], tk["B"][side][i])) for i in range(len(tk["t"]))]
        out[side] = np.array(ang)
    return out


def shell_span(tk, dom):
    S = tk["S"]
    L, R = np.where(dom == "shell_L")[0], np.where(dom == "shell_R")[0]
    return S[:, L, 0].mean(axis=1) - S[:, R, 0].mean(axis=1)


def grounded_slide(tk, sol):
    """Per leg: lift, and the largest XY drift of the sole inside one ground contact (per run, not across steps)."""
    out = {}
    for leg, (P, zl) in sol.items():
        on = zl <= zl.min() + GROUND_EPS
        worst, runs, cur = 0.0, 0, []
        for i in list(range(len(zl))) + [None]:
            if i is not None and on[i]:
                cur.append(i)
                continue
            if cur:
                runs += 1
                d = np.linalg.norm(P[cur, :2] - P[cur[0], :2], axis=1)
                worst = max(worst, float(d.max()))
            cur = []
        out[leg] = {"min_z_mm": round(float(zl.min()) * 1000, 1), "max_lift_mm": round(float(zl.max()) * 1000, 1),
                    "contacts": runs, "slide_within_contact_mm": round(worst * 1000, 2)}
    return out


def walk(tk, feet, spec):
    n, v = spec["frames"][1], spec["speed_mps"]
    sol = soles(tk, feet)
    t = tk["t"][:-1]
    res, speeds, slides = {}, [], []
    for leg, (P, zl) in sol.items():
        runs = cm.planted_runs(zl[:-1], zl.min() + 0.004)
        plants = cm.plant_slide(t, P[:-1], runs, v, n)
        A = tk["B"]["leg_%s_foot" % leg][:, :3, 3]
        aruns = cm.planted_runs(A[:-1, 2], A[:, 2].min() + 0.002)
        ank = cm.plant_slide(t, A[:-1], aruns, v, n)
        res[leg] = {"sole_plants": plants, "ankle_plants": ank, "sole_min_z_mm": round(float(zl.min()) * 1000, 2)}
        speeds += [p["speed_mps"] for p in plants + ank if np.isfinite(p["speed_mps"])]
        slides += [p["slide_vs_target_mm"] for p in plants + ank]
    return {"walk": res, "walk_speed_measured_mps": [round(min(speeds), 3), round(max(speeds), 3)],
            "walk_speed_target_mps": v, "walk_max_slide_mm": max(slides)}


def bite(tk, rest, feet, dom, spec):
    head = np.where(dom == "head")[0]
    Y = tk["S"][:, head, 1]
    fwd = Y.min(axis=1)
    i = int(fwd.argmin())
    tip = head[int(Y[i].argmin())]
    c = spec["contact_frame"]
    ic = int(np.argmin(np.abs(tk["t"] - c)))
    P = tk["S"][ic, tip]
    sol = soles(tk, feet)
    sh = shell_angles(tk, rest)
    span = shell_span(tk, dom)
    body_y = tk["B"]["body"][:, 1, 3]
    fl = sol["front_L"][1]
    return {"bite": {
        "beak_forwardmost_frame": float(tk["t"][i]),
        "beak_tip_at_contact_m": {"forward": round(float(-P[1]), 3), "up": round(float(P[2]), 3),
                                  "right": round(float(-P[0]), 3)},
        "beak_tip_rest_forward_m": round(float(-rest["S"][tip, 1]), 3),
        "head_forwardmost_per_frame_m": per_frame(tk, -fwd),
        "beak_tip_z_per_frame_m": per_frame(tk, tk["S"][:, tip, 2]),
        "shell_L_deg_per_frame": per_frame(tk, sh["shell_L"], 1),
        "shell_R_deg_per_frame": per_frame(tk, sh["shell_R"], 1),
        "shell_span_per_frame_m": per_frame(tk, span),
        "shell_span_min_frame_after_windup": float(tk["t"][int(np.argmin(np.where(tk["t"] >= 8, span, 9)))]),
        "front_L_sole_z_per_frame_mm": per_frame(tk, fl * 1000, 1),
        "body_forward_per_frame_m": per_frame(tk, -body_y),
        "body_forwardmost_frame": float(tk["t"][int(body_y.argmin())]),
    }, "feet": grounded_slide(tk, sol)}


def death(tk, rest, feet, dom, spec):
    sol = soles(tk, feet)
    sh = shell_angles(tk, rest)
    return {"death": {
        "release_frame": spec.get("release_frame"), "last_frame": float(tk["t"][-1]),
        "shell_L_deg_per_frame": per_frame(tk, sh["shell_L"], 1),
        "shell_R_deg_per_frame": per_frame(tk, sh["shell_R"], 1),
        "shell_span_per_frame_m": per_frame(tk, shell_span(tk, dom)),
        "body_z_per_frame_m": per_frame(tk, tk["B"]["body"][:, 2, 3]),
        "end_sole_z_mm": {leg: round(float(zl[-1]) * 1000, 1) for leg, (P, zl) in sol.items()},
        "end_skin_min_z_mm": round(float(tk["S"][-1, :, 2].min()) * 1000, 2),
    }, "feet": grounded_slide(tk, sol)}


def pop(tk, feet, spec):
    sol = soles(tk, feet)
    low = np.max([zl for P, zl in sol.values()], axis=0)    # highest foot
    high = np.min([zl for P, zl in sol.values()], axis=0)   # lowest foot
    frames = per_frame(tk, high * 1000, 1)
    air = [f for f, z in enumerate(frames) if z > GROUND_EPS * 1000]
    return {"pop": {
        "lowest_foot_z_per_frame_mm": frames, "highest_foot_z_per_frame_mm": per_frame(tk, low * 1000, 1),
        "airborne_frames": air, "takeoff_frame_first_airborne": air[0] if air else None,
        "landing_frame_first_grounded": (air[-1] + 1) if air else None,
        "contract": {"takeoff": spec.get("takeoff_frame"), "landing": spec.get("contact_frame")},
    }, "feet": grounded_slide(tk, sol)}


def specific(take, tk, rest, feet, dom, spec):
    if take.endswith("_Walk"):
        return walk(tk, feet, spec)
    if take.endswith("_Bite"):
        return bite(tk, rest, feet, dom, spec)
    if take.endswith("_Death"):
        return death(tk, rest, feet, dom, spec)
    if take.endswith("_Pop"):
        return pop(tk, feet, spec)
    return {"feet": grounded_slide(tk, soles(tk, feet))}
