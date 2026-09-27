"""Per-take measurements: walk contacts/speed, slam timing, hit peak, death landing."""
import numpy as np
import check_lib as L

CONTACT = 0.01   # a vertex within 1 cm of the ground counts as touching
IDLE = "ForestRootSnarer_Idle"


def _pose0(data, name, i):
    V, B = data[name]
    return V[i], {k: v[i] for k, v in B.items()}


def walk(V, B, reg, n=16):
    """Contacts of the in-place walk. A vertex 'touches' below th (3 mm = on the ground, 1 cm =
    near it). Slip: per-frame motion of touching vertices against the ground moving +V. Slide:
    drift of one vertex over an unbroken run of frames in which it touches (entity motion removed)."""
    out = {}
    seq = [f % n for f in range(2 * n + 1)]          # two cycles, frame 16 == frame 0
    for r in ("slab_L", "slab_R", "hind_L", "hind_R"):
        idx = reg[r]
        Z = np.stack([V[f][idx, 2] for f in seq])
        P = np.stack([V[f][idx, :2] for f in seq])
        planted = Z.min(1) < 0.005
        runs, i = [], 0
        while i < len(seq):
            if planted[i]:
                j = i
                while j + 1 < len(seq) and planted[j + 1]:
                    j += 1
                runs.append((i, j))
                i = j + 1
            else:
                i += 1
        o = {"plant_runs_frames": [[a, b] for a, b in runs if a < n]}
        for th, tag in ((0.003, "3mm"), (CONTACT, "1cm")):
            touching = (Z < th) & planted[:, None]
            dys, slips, slide = [], [], 0.0
            for i in range(len(seq) - 1):
                both = touching[i] & touching[i + 1]
                if both.any():
                    d = P[i + 1, both] - P[i, both]
                    dys.append(float(d[:, 1].mean()))
                    slips.append(float(np.sqrt((d[:, 1] - L.V_WALK) ** 2 + d[:, 0] ** 2).max()))
            for k in range(Z.shape[1]):
                i = 0
                while i < len(seq):
                    if not touching[i, k]:
                        i += 1
                        continue
                    j = i
                    while j + 1 < len(seq) and touching[j + 1, k]:
                        j += 1
                    if j > i:
                        comp = P[i:j + 1, k] - np.array([[0.0, L.V_WALK * (m - i)] for m in range(i, j + 1)])
                        slide = max(slide, float(np.linalg.norm(comp - comp[0], axis=1).max()))
                    i = j + 1
            o[f"ground_speed_mps_{tag}"] = round(float(np.mean(dys)) * 30, 3) if dys else None
            o[f"max_slip_per_frame_cm_{tag}"] = round(max(slips) * 100, 2) if slips else None
            o[f"max_slide_during_plant_cm_{tag}"] = round(slide * 100, 2)
        o["min_z_mm"] = round(float(Z.min()) * 1000, 1)
        out[r] = o
    for s in "LR":
        h = B[f"{s}_hand"][:, :3, 3]
        run = out[f"slab_{s}"]["plant_runs_frames"]
        if run:
            a, b = run[0]
            ys = [h[f % n, 1] for f in range(a, b + 1)]
            out[f"slab_{s}"]["socket_dy_per_frame_m"] = [round(float(y1 - y0), 4) for y0, y1 in zip(ys, ys[1:])]
    return out


def slam(V, B, reg):
    zL, zR = V[:, reg["slab_L"], 2].min(1), V[:, reg["slab_R"], 2].min(1)
    hz = np.maximum(B["L_hand"][:, 2, 3], B["R_hand"][:, 2, 3])
    both = np.maximum(zL, zR)
    peak = int(hz[:40].argmax())                     # slabs overhead, then the slam
    contact = next(i for i in range(peak, len(both)) if both[i] <= CONTACT)
    first_any = next(i for i in range(peak, len(both)) if min(zL[i], zR[i]) <= CONTACT)
    pin = {s: B[f"{s}_hand"][contact, :3, 3] for s in "LR"}
    drift = [max(float(np.linalg.norm(B[f"{s}_hand"][i, :3, 3] - pin[s])) for s in "LR") for i in range(len(both))]
    moved = next((i for i in range(contact, len(both)) if drift[i] > 0.005), None)
    lifted = next((i for i in range(contact + 1, len(both)) if min(zL[i], zR[i]) > 0.0), None)
    pz = B["pelvis"][:, 2, 3]
    body = np.concatenate([reg["body"], reg["head"]])
    win = range(24, 52)
    heave = max(win, key=lambda i: pz[i])
    return {"contact_frame_both_slabs": contact, "first_slab_touch": first_any,
            "slab_min_z_cm_at": {f: [round(float(zL[f]) * 100, 1), round(float(zR[f]) * 100, 1)]
                                 for f in range(peak, contact + 2)},
            "overhead_peak_frame": int(hz[:contact + 1].argmax()),
            "overhead_frames_within_5cm_of_peak": [int(i) for i in np.nonzero(hz[:contact + 1] > hz.max() - 0.05)[0]],
            "socket_max_height_m": round(float(hz.max()), 3),
            "pinned_socket_drift_mm_until_60": round(max(drift[contact:61]) * 1000, 2),
            "pinned_socket_drift_mm_16_60": round(max(max(float(np.linalg.norm(B[f"{s}_hand"][i, :3, 3] - B[f"{s}_hand"][contact + 1, :3, 3]))
                                                          for s in "LR") for i in range(contact + 1, 61)) * 1000, 2),
            "first_frame_socket_moves_5mm": moved, "first_frame_slabs_fully_above_ground": lifted,
            "slab_depth_cm_pinned": [round(float(min(zL[contact:61].min(), zR[contact:61].min())) * 100, 1),
                                     round(float(max(zL[contact:61].max(), zR[contact:61].max())) * 100, 1)],
            "pelvis_heave_peak_frame_24_52": int(heave),
            "pelvis_z_cm_30_40": [round(float(pz[i]) * 100, 1) for i in range(30, 41)],
            "body_head_min_z_mm": round(float(V[:, body, 2].min()) * 1000, 1),
            "body_head_min_z_frame": int(V[:, body, 2].min(1).argmin())}


def mend(V, B, reg):
    """Heal «Волна из корней»: stab contact, pinned channel, release heave, pull-out, Idle 0 at the end."""
    zL, zR = V[:, reg["slab_L"], 2].min(1), V[:, reg["slab_R"], 2].min(1)
    both = np.maximum(zL, zR)
    contact = next(i for i in range(1, len(both)) if both[i] <= CONTACT)
    lift_peak = int(np.minimum(zL, zR)[:contact].argmax())
    ref = {s: B[f"{s}_hand"][10, :3, 3] for s in "LR"}
    drift = max(float(np.linalg.norm(B[f"{s}_hand"][i, :3, 3] - ref[s])) for s in "LR" for i in range(10, 39))
    out_f = next((i for i in range(31, len(both)) if min(zL[i], zR[i]) > 0.0), None)
    body = np.concatenate([reg["body"], reg["head"]])
    top = V[:, :, 2].max(1)
    pz = B["pelvis"][:, 2, 3]
    return {"contact_frame_both_slabs": contact, "lift_peak_frame": lift_peak,
            "slab_min_z_cm_at": {f: [round(float(zL[f]) * 100, 1), round(float(zR[f]) * 100, 1)] for f in (5, 6, 7, 8, 9, 10, 30, 38, 39, 40)},
            "pinned_socket_drift_mm_10_38": round(drift * 1000, 2),
            "first_frame_slabs_fully_above_ground_after_30": out_f,
            "slab_depth_cm_pinned_8_38": [round(float(min(zL[8:39].min(), zR[8:39].min())) * 100, 1),
                                          round(float(max(zL[8:39].max(), zR[8:39].max())) * 100, 1)],
            "dome_top_cm_8_38": [round(float(t) * 100, 1) for t in top[8:39]],
            "dome_top_peak_frame_20_40": int(20 + top[20:41].argmax()),
            "pelvis_z_peak_frame_20_40": int(20 + pz[20:41].argmax()),
            "channel_pulse_top_extremes_cm": {"swell_15": round(float(top[15]) * 100, 1), "push_20": round(float(top[20]) * 100, 1),
                                              "swell_24": round(float(top[24]) * 100, 1), "push_28": round(float(top[28]) * 100, 1),
                                              "release_30": round(float(top[30]) * 100, 1)},
            "body_head_min_z_mm": round(float(V[:, body, 2].min()) * 1000, 1),
            "body_head_min_z_frame": int(V[:, body, 2].min(1).argmin())}


def hit(V, B, V0):
    d = np.linalg.norm(B["head"][:, :3, 3] - B["head"][0, :3, 3], axis=1)
    return {"peak_frame": int(d.argmax()), "head_disp_cm": [round(float(x) * 100, 1) for x in d]}


def death(V, B, reg):
    body = reg["body"]
    bz = V[:, body, 2].min(1)
    ground = next((i for i in range(len(bz)) if bz[i] < 0.02), None)
    step = [max(float(np.linalg.norm(B[k][i, :3, 3] - B[k][i - 1, :3, 3])) for k in B) for i in range(1, len(bz))]
    last = V[-1]
    return {"first_frame_belly_within_2cm": ground,
            "body_min_z_cm": [round(float(z) * 100, 1) for z in bz],
            "still_from_32_max_bone_step_mm": round(max(step[32:]) * 1000, 2),
            "end_min_z_mm": {k: round(float(last[idx, 2].min()) * 1000, 1) for k, idx in reg.items()},
            "end_max_z_m": round(float(last[:, 2].max()), 3)}


def _still_slide(Z, P, th):
    """Largest drift of one vertex over an unbroken run of frames in which it is below th (no ground motion)."""
    worst = 0.0
    for k in range(Z.shape[1]):
        i = 0
        while i < Z.shape[0]:
            if Z[i, k] >= th:
                i += 1
                continue
            j = i
            while j + 1 < Z.shape[0] and Z[j + 1, k] < th:
                j += 1
            if j > i:
                worst = max(worst, float(np.linalg.norm(P[i:j + 1, k] - P[i, k], axis=1).max()))
            i = j + 1
    return worst


def take_specific(R, data, reg, dom):
    V0, B0 = _pose0(data, IDLE, 0)
    for name in ("ForestRootSnarer_Slam", "ForestRootSnarer_Hit", "ForestRootSnarer_Death", "ForestRootSnarer_Mend"):
        V, B = data[name]
        R[name]["start_vs_idle0"] = L.pose_diff(V[0], {k: v[0] for k, v in B.items()}, V0, B0)
        if name != "ForestRootSnarer_Death":
            R[name]["end_vs_idle0"] = L.pose_diff(V[-1], {k: v[-1] for k, v in B.items()}, V0, B0)
    Vw, Bw = data["ForestRootSnarer_Walk"]
    R["ForestRootSnarer_Walk"]["start_vs_idle0"] = L.pose_diff(Vw[0], {k: v[0] for k, v in Bw.items()}, V0, B0)
    R["ForestRootSnarer_Walk"]["contacts"] = walk(Vw, Bw, reg)
    R["ForestRootSnarer_Slam"]["timing"] = slam(*data["ForestRootSnarer_Slam"], reg)
    R["ForestRootSnarer_Hit"]["timing"] = hit(*data["ForestRootSnarer_Hit"], V0)
    R["ForestRootSnarer_Mend"]["timing"] = mend(*data["ForestRootSnarer_Mend"], reg)
    R["ForestRootSnarer_Death"]["landing"] = death(*data["ForestRootSnarer_Death"], reg)
    Vi, Bi = data[IDLE]
    R[IDLE]["contact_slide_mm"] = {f"{r}_{tag}": round(_still_slide(Vi[:, reg[r], 2], Vi[:, reg[r], :2], th) * 1000, 2)
                                   for r in ("slab_L", "slab_R", "hind_L", "hind_R")
                                   for th, tag in ((0.003, "3mm"), (CONTACT, "1cm"))}
    R[IDLE]["contact_min_z_mm_frame0"] = {r: round(float(Vi[0, reg[r], 2].min()) * 1000, 1)
                                          for r in ("slab_L", "slab_R", "hind_L", "hind_R")}
