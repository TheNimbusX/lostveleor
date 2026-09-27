"""Assemble unity_package/verification.json from the verify/out measurements (system python).

python a_verification.py
"""
import json
import os
import re
from pathlib import Path

import numpy as np

V = Path(__file__).resolve().parent
O = V / "out"
PKG = V.parent
PROD = PKG.parents[1]
J = {n: json.loads((O / f"{n}.json").read_text()) for n in ("raw", "takes", "contacts", "fold", "walk", "stretch")}
EXP = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
T, C, R = J["takes"], J["contacts"], J["raw"]


def arm_growth():
    """max length gain (cm) of wrist/forearm edges per take (edges whose both ends are mostly Hand+ForeArm)."""
    E = np.load(O / "edges.npy")
    rest = np.load(O / "rest.npy")
    W = np.load(O / "weights.npy")
    G = json.loads((O / "groups.json").read_text())["groups"]
    arm = W[:, [G.index(b) for b in ("LeftHand", "RightHand", "LeftForeArm", "RightForeArm")]].sum(1) > 0.5
    m = arm[E[:, 0]] & arm[E[:, 1]]
    L0 = np.linalg.norm(rest[E[m, 0]] - rest[E[m, 1]], axis=1)
    out = {}
    for t in T["takes"]:
        co = np.load(O / f"co_{t}.npy")
        L = np.linalg.norm(co[:, E[m, 0]] - co[:, E[m, 1]], axis=2)
        g = (L - L0).max(1)
        out[t.split("_")[1]] = [round(float(g.max()) * 100, 1), int(g.argmax())]
    return out


def page_refs():
    page = PROD / "review" / "animation_r01" / "index.html"
    h = page.read_text(encoding="utf-8")
    refs = re.findall(r"(?:src|href)=[\"']([^\"'#]+)[\"']", h)
    missing = [r for r in refs if not r.startswith("http") and not (page.parent / r).exists()]
    return {"page": str(page), "references": len(refs), "missing": missing, "videos": sum(r.endswith(".mp4") for r in refs)}


takes = T["takes"]
exp_takes = EXP["takes"]
c1 = {
    "names_match": T["imported_takes"] == T["expected_takes"],
    "fbx_time_mode": R["TimeMode"], "fbx_custom_frame_rate": R["CustomFrameRate"], "reimport_scene_fps": T["scene_fps"],
    "ranges_fbx_stack": {k: v["local_frames"] for k, v in R["stacks"].items()},
    "ranges_match_export": all(R["stacks"][k]["local_frames"] == [float(x) for x in exp_takes[k]["frames"]] for k in exp_takes),
    "keys_off_integer_frames": R["keys_off_integer_frames"],
    "loops": {k.split("_")[1]: {"seam_vertex_mm": round(v["first_last_vertex_max_m"] * 1000, 3),
                                "seam_bone_rot_deg": round(v["first_last_bone_rot_max_deg"], 3),
                                "velocity_jump_m_per_frame": round(v["loop_velocity_jump_m_per_frame"], 4)}
              for k, v in takes.items() if v.get("loop_ok") is not None},
}
c1["pass"] = bool(c1["names_match"] and c1["ranges_match_export"] and T["scene_fps"] == 30 and R["keys_off_integer_frames"] == 0
                  and all(l["seam_vertex_mm"] <= 1 and l["seam_bone_rot_deg"] <= 0.5 for l in c1["loops"].values()))
c2 = {"armature_object_travel_m": {k.split("_")[1]: v["armature_object_travel_m"] for k, v in takes.items()},
      "object_level_channel_span": max(v["object_level_channel_span"] for v in takes.values()),
      "bone_scale_max_dev_from_1": max(v["bone_scale_max_dev_from_1"] for v in takes.values()),
      "hips_xy_max_from_origin_m": {k.split("_")[1]: round(v["hips_xy_max_from_origin_m"], 3) for k, v in takes.items()},
      "hips_xy_net_first_to_last_m": {k.split("_")[1]: [round(x, 3) for x in v["hips_xy_net_first_to_last_m"]] for k, v in takes.items()},
      "rest_min_z_m": T["rest_min_z"], "rest_xy_center_m": T["rest_xy_center"], "armature_world_scale": T["armature_world_scale"],
      "note": "no root motion: armature object never moves; Hips sway only (Death: body falls 0.34 m forward, the entity stays put)"}
c2["pass"] = bool(all(v == 0 for v in c2["armature_object_travel_m"].values()) and c2["object_level_channel_span"] < 1e-6
                  and c2["bone_scale_max_dev_from_1"] < 1e-4 and abs(T["rest_min_z"]) < 1e-3
                  and max(abs(x) for x in T["rest_xy_center"]) < 1e-3)
c3 = dict(J["walk"])
c4 = {"LineCast": {"contract_contact": 24, "first_frame_both_tips_at_ground": C["LineCast"]["first_frame_both_spikes_at_ground"],
                   "spike_low_z_m_f20_27": C["LineCast"]["spike_min_z_L_R_f20_27"], "tip_speed_mps_f18_26": C["LineCast"]["tip_speed_mps_f18_26"],
                   "buried_depth_26_60_m": C["LineCast"]["tip_depth_hold_26_60"], "buried_drift_26_60_m": C["LineCast"]["tip_drift_hold_26_60_m"],
                   "sim": "first ground spike erupts at tick 27 (3 ticks after the slam); the view maps windup 0-27 ticks onto clip 0-24"},
      "Burst": {"contract_release": 21, "first_frame_at_99pct_spread": C["Burst"]["first_frame_at_99pct_spread"],
                "spread_m": C["Burst"]["spread_m"]},
      "Shot": {"contract_release": 21, "tip_most_forward_frame": C["Shot"]["tip_most_forward_frame"],
               "tip_speed_peak_frame": C["Shot"]["tip_speed_peak_frame"], "tip_speed_mps_f16_26": C["Shot"]["tip_speed_mps_f16_26"],
               "muzzle_unity_at_release": C["Shot"]["muzzle_world_unity_at_release"], "muzzle_axis_unity": C["Shot"].get("muzzle_axis_unity_at_release"),
               "muzzle_to_spike_tip_vertex_m": C["Shot"]["muzzle_to_spike_tip_vertex_m"]},
      "Hit": {"contract_peak": 3, "measured_peak": C["Hit"]["mean_vertex_displacement_peak_frame"]}}
c4["pass"] = bool(c4["LineCast"]["first_frame_both_tips_at_ground"] == 24 and min(C["LineCast"]["spike_min_z_L_R_f20_27"]["23"]) > 0.3
                  and c4["Burst"]["first_frame_at_99pct_spread"] == 21 and c4["Shot"]["tip_most_forward_frame"] == 21
                  and c4["Shot"]["muzzle_to_spike_tip_vertex_m"] < 0.01 and c4["Hit"]["measured_peak"] == 3)
ag = arm_growth()
c5 = {"rigid_spike_edge_dev_max": max(v["rigid_parts_edge_dev_max"] for v in takes.values()),
      "body_min_z_m": {k.split("_")[1]: round(v["min_z_body"], 4) for k, v in takes.items()},
      "frames_body_below_minus_2cm": {k.split("_")[1]: v["frames_body_below_minus2cm"] for k, v in takes.items()},
      "linecast_spikes_buried_by_design_m": round(takes["ForestThorncaster_LineCast"]["min_z_all"], 3),
      "death_final": C["Death"], "wrist_edge_gain_cm_max_frame": ag,
      "largest_edge_gain_cm": {k: J["stretch"][k]["abs_len_change_max_cm"] for k in J["stretch"]},
      "wrist_fold_deg_max_L_R": {t: [max(r[0] for r in rows.values()), max(r[1] for r in rows.values())] for t, rows in J["fold"].items()},
      "renders_looked_at": "verify/out/sheet3_*.jpg (game camera 52 deg, 3/4), sheet_wrist3.jpg, sheet_knee3.jpg (clay close-ups)"}
c5["pass"] = bool(c5["rigid_spike_edge_dev_max"] < 1e-3 and all(not v for v in c5["frames_body_below_minus_2cm"].values())
                  and abs(C["Death"]["final_min_z_body"]) < 0.01 and C["Death"]["final_verts_within_2cm_of_ground"] > 0
                  and max(v[0] for v in ag.values()) < 6.0)
c6 = {"triangles": T["triangles"], "budget": 25000, "max_weights_per_vertex": T["max_weights_per_vertex"],
      "vertices_over_4": T["vertices_over_4_weights"], "weight_sum_range": T["weight_sum_min_max"], "unweighted": T["unweighted_vertices"]}
c6["pass"] = bool(T["triangles"] <= 25000 and T["max_weights_per_vertex"] <= 4 and T["unweighted_vertices"] == 0)
c7 = page_refs()
c7["pass"] = not c7["missing"] and c7["videos"] == 14
out = {
    "fbx": str(PKG / "ForestThorncaster.fbx"), "method": "FBX re-imported into an empty Blender 5.2 scene; binary FBX parsed for "
    "time mode and stack ranges; vertex positions sampled every frame; renders at the game camera inspected by eye",
    "scripts": str(V),
    "checks": {"1_takes_fps_ranges_loops": c1, "2_in_place_root_scale": c2, "3_walk_feet": c3, "4_contact_frames": c4,
               "5_deformation_ground": c5, "6_triangles_weights": c6, "7_review_page": c7},
    "fixed_in_r03": {
        "wrist_seam_rig": {"before_cm": {"LineCast": [12.1, 8], "Burst": [16.8, 20], "Shot": [12.3, 10], "Death": [16.0, 44]},
                           "after_cm": ag, "change": "rig/fix_wrist_weights.py + rig_wrist.py (build_rig.py step 6b)"},
        "shot_wrist_fold": {"before_deg": 165.7, "after_deg": c5["wrist_fold_deg_max_L_R"]["Shot"][1]},
        "linecast_strike": {"before_tip_speed_mps_21_24": [23.8, 67.9, 12.6, 18.5], "before_tip_z_22_23": [0.75, 0.41],
                            "after_tip_speed_mps_21_24": [C["LineCast"]["tip_speed_mps_f18_26"][str(f)] for f in (21, 22, 23, 24)]},
        "burst_release_extreme": {"before": "spread 2.68 m at 21, max 2.76 m at 23", "after_first_99pct_frame": c4["Burst"]["first_frame_at_99pct_spread"]},
        "death_float": {"before": "no ground contact 16-21 (up to 8.5 cm), knees 2-5 cm above ground 22-31",
                        "after_body_min_z_m": c5["body_min_z_m"]["Death"]},
    },
}
out["pass"] = all(c["pass"] for c in out["checks"].values())
(PKG / "verification.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")
print("VERIFICATION", {k: v["pass"] for k, v in out["checks"].items()}, "ALL", out["pass"])
