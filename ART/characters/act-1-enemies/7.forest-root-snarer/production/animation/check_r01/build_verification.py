"""Compose unity_package/verification.json from the independent checks (system python).
Inputs: out/fbx_facts.json (fbx_facts.py), out/pose_check.json (check_pose.py in Blender),
out/old_takes_vs_r01.json (compare_old_takes.py), the review page and its media. python build_verification.py"""
import json, re
from pathlib import Path
import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
PKG = HERE.parent / "unity_package"
PAGE = HERE.parents[1] / "review" / "animation_r01" / "index.html"
facts = json.loads((HERE / "out" / "fbx_facts.json").read_text())
pose = json.loads((HERE / "out" / "pose_check.json").read_text())
exp = json.loads((PKG / "export.json").read_text(encoding="utf8"))
T = {k.split("_")[-1]: v for k, v in pose.items() if k.startswith("ForestRootSnarer_")}
names = ["ForestRootSnarer_" + n for n in ("Idle", "Walk", "Slam", "Hit", "Death", "Mend")]
old = json.loads((HERE / "out" / "old_takes_vs_r01.json").read_text())
stacks = facts["anim_stacks"]
geo = facts["geometries"][0]


def ok(c):
    return "pass" if c else "FAIL"


checks = {}
# 1 takes / fps / ranges / seams
rng = {n: [stacks[n]["local_start_frame"], stacks[n]["local_stop_frame"]] for n in names if n in stacks}
checks["1_takes_fps_ranges_loops"] = {
    "result": ok(sorted(stacks) == sorted(names) and facts["custom_frame_rate"] == [30.0]
                 and all(rng[n] == [float(x) for x in exp["takes"][n]["frames"]] for n in names)
                 and all(stacks[n]["keys_off_integer_frames"] == 0 for n in names)
                 and all(T[t]["loop_seam"]["vertex_mm"] <= 1.0 and T[t]["loop_seam"]["bone_rot_deg"] <= 0.5 for t in ("Idle", "Walk"))),
    "anim_stacks_in_fbx": sorted(stacks), "fbx_time_mode": facts["time_mode"], "fbx_frame_rate": facts["custom_frame_rate"],
    "stack_frame_ranges": rng, "keys_off_integer_frames": {n: stacks[n]["keys_off_integer_frames"] for n in names},
    "blender_import_actions": pose["actions"],
    "loop_seams": {t: T[t]["loop_seam"] for t in ("Idle", "Walk")}}
# 2 in place / origin / scale
checks["2_in_place_origin_scale"] = {
    "result": ok(all(max(T[t]["root_travel_mm"]) == 0 and T[t]["root_head_z_mm"] == 0 and T[t]["bone_scale_max_dev"] < 1e-4 for t in T)
                 and all(stacks[n]["bone_scale_key_range"] == [1.0, 1.0] for n in names)),
    "root_travel_mm": {t: T[t]["root_travel_mm"] for t in T}, "root_bone_head_z_mm": {t: T[t]["root_head_z_mm"] for t in T},
    "bone_scale_keys_in_fbx": {n: stacks[n]["bone_scale_key_range"] for n in names},
    "bone_world_scale_max_dev": max(T[t]["bone_scale_max_dev"] for t in T),
    "armature_object": pose["armature_object"], "rest_root_head": pose["rest"]["root_head"],
    "note": "the armature Null node carries a constant -90 deg X / scale 100 (Blender unit export, same as the in-game ForestWendigo.fbx); every bone scale key is exactly 1"}
# 3 walk
wc = T["Walk"]["contacts"]
checks["3_walk_contacts"] = {
    "result": ok(all(v["max_slide_during_plant_cm_3mm"] < 2.0 for v in wc.values())),
    "target_speed_mps": 2.4,
    "measured_ground_speed_mps_on_ground_3mm": {k: v["ground_speed_mps_3mm"] for k, v in wc.items()},
    "max_slide_during_plant_cm_on_ground_3mm": {k: v["max_slide_during_plant_cm_3mm"] for k, v in wc.items()},
    "max_slip_per_frame_cm_on_ground_3mm": {k: v["max_slip_per_frame_cm_3mm"] for k, v in wc.items()},
    "within_1cm_of_ground": {k: {"speed_mps": v["ground_speed_mps_1cm"], "slide_cm": v["max_slide_during_plant_cm_1cm"]} for k, v in wc.items()},
    "contact_min_z_mm": {k: v["min_z_mm"] for k, v in wc.items()},
    "plant_runs_frames": {k: v["plant_runs_frames"] for k, v in wc.items()},
    "idle_contact_slide_mm": T["Idle"]["contact_slide_mm"]}
# 4 contact frames
sl, de, hi, me = T["Slam"]["timing"], T["Death"]["landing"], T["Hit"]["timing"], T["Mend"]["timing"]
pz = sl["pelvis_z_cm_30_40"]
mend_ok = (me["contact_frame_both_slabs"] == 8 and min(me["slab_min_z_cm_at"]["7"]) > 20
           and me["pinned_socket_drift_mm_10_38"] < 1 and me["dome_top_peak_frame_20_40"] == 30
           and me["pelvis_z_peak_frame_20_40"] == 30 and me["first_frame_slabs_fully_above_ground_after_30"] <= 41
           and T["Mend"]["end_vs_idle0"]["vertex_mm"] <= 1.0 and T["Mend"]["start_vs_idle0"]["vertex_mm"] <= 1.0)
checks["4_contact_frames"] = {
    "result": ok(sl["contact_frame_both_slabs"] == 15 and min(sl["slab_min_z_cm_at"]["14"]) > 20
                 and sl["pinned_socket_drift_mm_16_60"] < 1 and sl["first_frame_slabs_fully_above_ground"] == 61
                 and pz.index(min(pz)) + 30 == 36 and hi["peak_frame"] == 3 and de["first_frame_belly_within_2cm"] == 25
                 and mend_ok),
    "sim_contract": "Simulation.RootSnarer: slam (circle) tick 15, impact tick 15+21 = 36, stands 36 ticks -> 72; "
                    "mend (27.09): plant from t0, slabs in the ground on 8, channel to 30, heal ring tick 30, recovery 30-50",
    "slam_slab_min_z_cm_by_frame": sl["slab_min_z_cm_at"], "slam_contact_frame_both_slabs": sl["contact_frame_both_slabs"],
    "slam_overhead_frames": sl["overhead_frames_within_5cm_of_peak"],
    "slam_socket_drift_mm_16_60": sl["pinned_socket_drift_mm_16_60"],
    "slam_socket_move_15_to_16_mm": sl["pinned_socket_drift_mm_until_60"],
    "slam_slabs_fully_out_of_ground_frame": sl["first_frame_slabs_fully_above_ground"],
    "slam_roots_erupt_pelvis_z_cm_30_40": pz, "slam_roots_erupt_extreme_frame": pz.index(min(pz)) + 30,
    "slam_end_vs_idle0": T["Slam"]["end_vs_idle0"], "hit_peak_frame": hi["peak_frame"],
    "death_belly_on_ground_frame": de["first_frame_belly_within_2cm"],
    "mend_contact_frame_both_slabs": me["contact_frame_both_slabs"], "mend_slab_min_z_cm_by_frame": me["slab_min_z_cm_at"],
    "mend_pinned_socket_drift_mm_10_38": me["pinned_socket_drift_mm_10_38"],
    "mend_release_dome_top_peak_frame": me["dome_top_peak_frame_20_40"], "mend_release_pelvis_peak_frame": me["pelvis_z_peak_frame_20_40"],
    "mend_pulse_dome_top_cm": me["channel_pulse_top_extremes_cm"],
    "mend_slabs_fully_out_of_ground_frame": me["first_frame_slabs_fully_above_ground_after_30"],
    "mend_start_end_vs_idle0": {"start": T["Mend"]["start_vs_idle0"], "end": T["Mend"]["end_vs_idle0"]},
    "mend_body_head_min_z_mm": me["body_head_min_z_mm"]}
# 5 deformation
checks["5_deformation"] = {
    "result": ok(all(T[t]["rigid_edge_len_dev_mm"] < 0.01 for t in T)
                 and all(abs(v) <= 10 for k, v in de["end_min_z_mm"].items()) and de["still_from_32_max_bone_step_mm"] < 1),
    "rigid_slab_edges_len_dev_mm": {t: T[t]["rigid_edge_len_dev_mm"] for t in T},
    "edge_stretch_ratio_max": {t: T[t]["edge_stretch_max"] for t in T},
    "edge_stretch_note": "worst stretch is the L/R clavicle-neck skin at the shoulder junction (short 1-2 cm edges, up to ~9 cm longer "
                         "with the slabs overhead in Slam 8-12); renders at the game angle, front, back and side show no spikes, tears "
                         "or exploded vertices; slabs are rigid",
    "min_z_mm_per_region": {t: T[t]["min_z_mm_per_region"] for t in T},
    "slam_note": "slabs 11-22 cm in the ground 15-60 by design (buried), body never below -4 mm; Mend: slabs 9-14 cm "
                 "in the ground 8-38 by design, belly >= 12 cm, face >= 4.6 cm; worst Mend stretch = clavicle skin in the "
                 "sideways lift 4-6 (same junction as Slam, lower ratio)",
    "death_end_min_z_mm": de["end_min_z_mm"], "death_still_32_45_max_bone_step_mm": de["still_from_32_max_bone_step_mm"],
    "renders_looked_at": "check_r01/out/*.jpg (game 52 deg 3/4, front, back, side, low)"}
# 6 mesh
checks["6_mesh_budget_weights"] = {
    "result": ok(geo["triangles"] <= 25000 and geo["max_influences"] <= 4 and abs(geo["weight_sum_min"] - 1) < 1e-3),
    "triangles": geo["triangles"], "vertices": geo["vertices"], "max_influences": geo["max_influences"],
    "influence_histogram": geo["influence_histogram"], "weight_sum_range": [geo["weight_sum_min"], geo["weight_sum_max"]],
    "skin_clusters": geo["clusters"]}
# 7 review page
html = PAGE.read_text(encoding="utf8")
refs = sorted(set(re.findall(r'(?:src|href)="([^"#]+)"', html)))
missing = [r for r in refs if not (PAGE.parent / r).resolve().exists()]
media = json.loads((PAGE.parent / "media.json").read_text(encoding="utf8"))
frames = {}
for take, m in media.items():
    for view, v in m["videos"].items():
        n, _ = imageio_ffmpeg.count_frames_and_secs(str(PAGE.parent / v["file"]))
        frames[v["file"]] = [n, v["video_frames"]]
checks["7_review_page"] = {"result": ok(not missing and all(a == b for a, b in frames.values())),
                           "page": str(PAGE), "references": len(refs), "missing": missing,
                           "mp4_decoded_vs_expected_frames": frames}
# 8 r01 takes unchanged by the 27.09 extension
checks["8_r01_takes_unchanged"] = {
    "result": ok(old["all_old_takes_bit_identical"] and old["mesh_skin_bind_identical"] and old["added_takes"] == ["ForestRootSnarer_Mend"]),
    "compared_with": "work/r01_approved/ForestRootSnarer_r01.fbx (approved 26.09 package)",
    "added_takes": old["added_takes"],
    "per_take": {t: {k: v for k, v in d.items() if k in ("curves", "keys", "identical_curves", "range_identical")}
                 for t, d in old["takes"].items()},
    "mesh_skin_bind_identical": old["mesh_skin_bind_identical"],
    "method": "every AnimationCurve (KeyTime + KeyValueFloat arrays) of the 5 old stacks and every geometry / skin "
              "cluster / bind-pose array compared for exact equality in the FBX bytes (check_r01/compare_old_takes.py)"}
out = {"subject": "ForestRootSnarer", "revision": "animation_r01 + 27.09 Mend (independent check)", "date": "2026-09-27",
       "fbx": str(PKG / "ForestRootSnarer.fbx"), "method": "FBX parsed byte-level (check_r01/fbx_bin.py) and re-imported into an "
       "empty Blender scene (check_r01/check_pose.py); every number below is measured on that import",
       "all_pass": all(c["result"] == "pass" for c in checks.values()), "checks": checks}
(PKG / "verification.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf8")
print(json.dumps({k: v["result"] for k, v in checks.items()}), "missing", missing)
