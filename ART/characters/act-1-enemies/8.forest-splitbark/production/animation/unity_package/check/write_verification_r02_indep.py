"""Adds the 27.09 INDEPENDENT check block to verification.json (run after write_verification_r02.py).

python write_verification_r02_indep.py
Reads check/r02_indep.json (final FBX) and check/r02_indep_before_fix.json (the builder's r02 FBX before the fix).
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
ver = json.loads((PKG / "verification.json").read_text(encoding="utf-8"))
now = json.loads((HERE / "r02_indep.json").read_text(encoding="utf-8"))
old = json.loads((HERE / "r02_indep_before_fix.json").read_text(encoding="utf-8"))
B, A = now["B_import"], now["A_raw_fbx"]
Bo = old["B_import"]["5_contract_frames"]
c5 = B["5_contract_frames"]


block = {
    "date": "2026-09-27",
    "fbx": now["fbx"], "fbx_bytes": now["fbx_bytes"],
    "method": ("check/check_r02_indep.py (blender -b --factory-startup): A) raw FBX parse of the r02 and the backed-up "
               "r01 FBX: per-bone KeyTime/KeyValueFloat arrays of every r01 stack, stack spans, GlobalSettings, geometry "
               "and skin clusters; B) the FBX imported into an emptied scene, all 10 takes sampled every 0.25 frame "
               "(bone world matrices + skin), old takes compared to the r01 FBX samples and to the r01 numbers stored "
               "above, new takes to export.json. check_render.py: game angle 4-6 frames per new take + side views "
               "(renders/game_Roll*, side_Roll*, _s_r02_indep_*.jpg; before the fix in renders/before_contact_fix)."),
    "checks": {
        "A_raw_fbx": {"stack_order_exact": A["names_exact_and_ordered"], "spans_match_export": all(
            v["matches_export"] for v in A["spans"].values()), "fps": A["global"]["CustomFrameRate"],
            "time_mode": A["global"]["TimeMode"], "axes_units_same_as_r01": A["global_same_as_r01"],
            "r01_stack_curves_identical": {t: v["identical"] for t, v in A["r01_curves"].items()},
            "geometry_identical_to_r01": A["geometry_identical_to_r01"],
            "skin_clusters_identical_to_r01": A["skin_clusters_identical_to_r01"], "pass": A["pass"]},
        "1_takes_names_ranges_30fps": {"pass": B["1_takes_ranges_fps"]["pass"],
                                       "ranges": {t: v["range"] for t, v in B["1_takes_ranges_fps"]["actions"].items()}},
        "2_in_place_origin_facing": {"pass": B["2_in_place"]["pass"],
                                     "root_xy_travel_mm_max": max(v["root"]["root_travel_xy_mm"] for k, v in
                                                                  B["2_in_place"].items() if isinstance(v, dict) and "root" in v),
                                     "armature_object_travel_mm_max": max(v["armature_object_travel_mm"] for k, v in
                                                                          B["2_in_place"].items() if isinstance(v, dict) and "root" in v),
                                     "bone_scale_dev_max": max(v["scale"]["max_bone_scale_dev"] for k, v in
                                                               B["2_in_place"].items() if isinstance(v, dict) and "root" in v),
                                     "rest_skin_min_z_mm": B["2_in_place"]["origin_on_ground_rest_min_z_mm"],
                                     "facing_guide_blender": B["2_in_place"]["facing_guide_blender"]},
        "3_old_takes_unchanged": {"pass": B["3_old_takes_unchanged"]["pass"],
                                  "fbx_samples_vs_r01_fbx": B["3_old_takes_unchanged"]["vs_r01_fbx_samples"],
                                  "stored_r01_numbers_reproduced": {t: v["all_match"] for t, v in
                                                                    B["3_old_takes_unchanged"]["vs_stored_r01_numbers"].items()}},
        "4_loop_and_handoffs": {"pass": B["4_loops_handoffs"]["pass"], "RollLoop_seam": B["4_loops_handoffs"]["RollLoop_seam"],
                                "RollLoop_velocity_seam_mps": B["4_loops_handoffs"]["RollLoop_velocity_seam_mps"],
                                "RollLoop_velocity_worst_inside_mps": B["4_loops_handoffs"]["RollLoop_velocity_worst_inside_mps"],
                                "handoffs_skin_mm": {k: v["skin_mm"] for k, v in B["4_loops_handoffs"]["handoffs"].items()}},
        "5_contact_frames": {
            "pass": c5["pass"],
            "RollCurl": {k: c5["RollCurl"][k] for k in ("first_frame_legs_tucked_(<15mm)", "first_frame_head_tucked_(<15mm)",
                                                        "body_yaw_abs_max_24_30", "rim_lift_24_30_above_ball_mm",
                                                        "f30_equals_ball_mm", "ok")},
            "RollLoop": {k: c5["RollLoop"][k] for k in ("rim_dip_below_ground_mm", "ball_pose_min_z_mm", "ok")},
            "RollUncurl": {k: c5["RollUncurl"][k] for k in ("f0_equals_ball_mm", "end_equals_idle0_mm",
                                                            "paw_first_grounded_frame", "support_gap_while_body_rises_mm",
                                                            "skid_path_near_ground_mm", "slide_while_planted_mm", "ok")},
            "RollDizzy": {k: c5["RollDizzy"][k] for k in ("f0_equals_ball_mm", "end_equals_idle0_mm", "bump",
                                                          "paw_first_grounded_frame", "support_gap_while_body_rises_mm",
                                                          "skid_path_near_ground_mm", "ok")}},
        "6_rigid_ground": {"pass": B["6_rigid_ground_deform"]["pass"],
                           **{t: {"rigid_edges_max_change_mm": v["rigid_edges_max_change_mm"],
                                  "skin_min_z_mm": v["ground"]["skin_min_z_mm"],
                                  "verts_below_minus_1cm": v["ground"]["verts_below_minus_1cm"],
                                  "edge_grow_max_mm": v["edge_grow_max_mm"]}
                              for t, v in B["6_rigid_ground_deform"].items() if isinstance(v, dict)}},
        "7_ball_spin_data_in_export": B["7_ball_spin_data"],
        "8_budget": B["8_budget"],
        "renders_looked_at": "renders/_s_r02_indep_game.jpg (game angle, 22 frames) and _s_r02_indep_side.jpg",
    },
    "before_fix": {
        "fbx": "backup_r02_before_contact_fix_2026-09-27/ForestSplitter.fbx",
        "all_pass": old["all_pass"],
        **{t: {"skin_min_z_mm_frames": {f: Bo[t]["skin_min_z_mm"][str(f)] for f in rng},
               "support_gap_while_body_rises_mm": Bo[t]["support_gap_while_body_rises_mm"],
               "skid_path_near_ground_mm": Bo[t]["skid_path_near_ground_mm"],
               "slide_while_planted_mm": Bo[t]["slide_while_planted_mm"], "ok": Bo[t]["ok"]}
           for t, rng in (("RollUncurl", range(8, 18)), ("RollDizzy", range(5, 13)))},
    },
    "fixes": [
        "RollUncurl / RollDizzy (takes_roll.py): the body rose off the shell rims while all four paws still hung "
        "up to 1.9-2.0 cm in the air (Uncurl 10-14, Dizzy 6-10), then the paws skated 6-10 cm into place within a few mm of "
        "the ground. Foot height now has its own curve (plant()): the paws swing out in the air and land last "
        "(Uncurl 7-13, plant 13; Dizzy 5-10, plant 10) and the body pushes up only after that (Uncurl 12-21, "
        "Dizzy 10-15). Start/end poses, head, shells, bump, sway, step-home unchanged. build_r02.sh re-run: "
        "re-baked, merged onto the untouched r01 blends, validated, FBX re-exported with all 10 takes, round trip, "
        "review page videos/strips for Uncurl, Dizzy and both roll sequences re-rendered; export.json phases and "
        "review text updated. The r01 takes are still bit-identical (raw FBX curves and samples).",
    ],
    "remaining": [
        "RollCurl 5-7: the paws lift while the body drops onto its rims, up to 12 mm of air for ~2 frames (a plop, "
        "body moving down); 10-12 the squash bounce lifts the ball 9 mm; 24-30 shake lifts the rims 7.7 mm - all "
        "designed, not changed.",
        "RollUncurl 13-16: the front-left shin skin dips up to ~6 mm under the ground while the paw is planted and the "
        "body still low (r01 Walk dips 7.6 mm).",
        "Rest paws float as in r01 (front_R 8.5 mm, hind 6-9 mm; model/IK reach), so the Uncurl/Dizzy stance has the "
        "same heights.",
        "Unity import still to do: RollLoop loop flag; replace the razlom/ copy of the FBX; the spin, 0.40 m/tick move, "
        "8 cm lift and upright finish are the view's job (export.json RollLoop.roll).",
    ],
    "all_pass": bool(now["all_pass"]),
}
ver["r02_independent_check_2026_09_27"] = block
ver["all_pass"] = bool(ver["all_pass"] and block["all_pass"])
ver.setdefault("revisions", {})["r02_independent"] = ("r02_independent_check_2026_09_27: independent re-import check, "
                                                      "Uncurl/Dizzy plant-then-push fix, 27.09")
(PKG / "verification.json").write_text(json.dumps(ver, indent=2, ensure_ascii=False), encoding="utf-8")
print("VERIFICATION_R02_INDEP", block["all_pass"], ver["all_pass"])
