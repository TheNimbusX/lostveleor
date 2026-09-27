"""Compose unity_package/verification.json from the 27.09 independent re-check (system python).
Inputs: out/fbx_raw.json (fbx_raw.py), out/pose_check.json (pose_check.py), out/fix_vs_prefix_raw.json,
pre_fix/verification.json (the numbers claimed before this check), the review page and its media.
python build_verification.py"""
import json, re
from pathlib import Path
import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
PKG = ANIM / "unity_package"
PAGE = ANIM.parent / "review" / "animation_r01" / "index.html"
raw = json.loads((HERE / "out" / "fbx_raw.json").read_text())
pose = json.loads((HERE / "out" / "pose_check.json").read_text())
fixd = json.loads((HERE / "out" / "fix_vs_prefix_raw.json").read_text())
prev = json.loads((HERE / "pre_fix" / "verification.json").read_text())["checks"]
exp = json.loads((PKG / "export.json").read_text(encoding="utf8"))
P = "ForestRootSnarer_"
OLD = ["Idle", "Walk", "Slam", "Hit", "Death"]
ALL = OLD + ["Mend"]


def same(a, b, tol=1e-3):
    if isinstance(a, dict):
        return all(same(a[k], b.get(k) if isinstance(b, dict) else None, tol) for k in a)
    if isinstance(a, (list, tuple)):
        return isinstance(b, (list, tuple)) and len(a) == len(b) and all(same(x, y, tol) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) <= tol
    return a == b


# ---- old-take numbers: previous verification.json vs measured now
pv5 = prev["5_deformation"]
pv4 = prev["4_contact_frames"]
pv3 = prev["3_walk_contacts"]
cmp = {}
for t in OLD:
    cmp[t] = {
        "min_z_mm_per_region": same({k: v for k, v in pv5["min_z_mm_per_region"][t].items()},
                                    {k: v for k, v in pose[t]["min_z_mm"].items()}, 0.011),
        "edge_stretch_max": same(pv5["edge_stretch_ratio_max"][t], pose[t]["edge_stretch_max"], 2e-4),
        "rigid_slab_edge_dev_mm": same(pv5["rigid_slab_edges_len_dev_mm"][t], pose[t]["rigid_edge_len_dev_mm"], 1e-4),
        "root_travel_mm": same(prev["2_in_place_origin_scale"]["root_travel_mm"][t], pose[t]["root_travel_mm_xyz"])}
for t in ("Idle", "Walk"):
    cmp[t]["loop_seam"] = same(prev["1_takes_fps_ranges_loops"]["loop_seams"][t], pose[t]["loop_seam"])
wk = pose["Walk"]["contacts_3mm"]
cmp["Walk"]["ground_speed_mps"] = same(pv3["measured_ground_speed_mps_on_ground_3mm"], {k: v["ground_speed_mps"] for k, v in wk.items()})
cmp["Walk"]["slide_cm"] = same(pv3["max_slide_during_plant_cm_on_ground_3mm"], {k: v["slide_cm"] for k, v in wk.items()})
st = pose["Slam"]["timing"]
cmp["Slam"]["contact_frame"] = pv4["slam_contact_frame_both_slabs"] == st["contact_frame_both_slabs"]
cmp["Slam"]["slab_min_z_cm_10_16"] = same(pv4["slam_slab_min_z_cm_by_frame"], st["slab_min_z_cm_10_16"])
cmp["Slam"]["socket_drift_mm_16_60"] = same(pv4["slam_socket_drift_mm_16_60"], st["socket_drift_mm_16_60"])
cmp["Slam"]["slabs_out_frame"] = pv4["slam_slabs_fully_out_of_ground_frame"] == st["slabs_fully_out_frame"]
cmp["Slam"]["pelvis_z_cm_30_40"] = same(pv4["slam_roots_erupt_pelvis_z_cm_30_40"], st["pelvis_z_cm_30_40"])
cmp["Slam"]["end_vs_idle0"] = same(pv4["slam_end_vs_idle0"], pose["Slam"]["end_vs_idle0"])
cmp["Hit"]["peak_frame"] = pv4["hit_peak_frame"] == pose["Hit"]["peak_frame"]
dl = pose["Death"]["landing"]
cmp["Death"]["belly_on_ground_frame"] = pv4["death_belly_on_ground_frame"] == dl["first_frame_belly_within_2cm"]
cmp["Death"]["still_32_45_step_mm"] = same(pv5["death_still_32_45_max_bone_step_mm"], dl["still_32_45_max_bone_step_mm"])
cmp["Death"]["end_min_z_mm"] = same({k: v for k, v in pv5["death_end_min_z_mm"].items() if k != "all"}, dl["end_min_z_mm"], 0.05)
old_numbers_ok = all(all(v.values()) for v in cmp.values())

# ---- review page
html = PAGE.read_text(encoding="utf-8")
refs = sorted(set(re.findall(r'(?:src|href)="([^"#:]+)"', html)))
missing = [r for r in refs if not (PAGE.parent / r).exists()]
media = {}
for f in sorted(PAGE.parent.glob("mend_*.mp4")):
    rd = imageio_ffmpeg.read_frames(str(f))
    meta = next(rd)
    media[f.name] = {"size": list(meta["size"]), "fps": meta["fps"], "frames": sum(1 for _ in rd)}
page_ok = (not missing and 'id="new-0927"' in html and "27.09 — новые способности" in html
           and html.index('id="new-0927"') < html.index('id="mend"') < html.index('id="slam"')
           and all(m["size"][0] <= 640 and m["fps"] == 30 for m in media.values()) and len(media) == 2)

m, mt = pose["Mend"], pose["Mend"]["timing"]
em = exp["takes"][P + "Mend"]
checks = {
    "1_takes_fps_ranges_loops": {
        "result": "pass" if (raw["stacks_exact"] and raw["ranges_match_export_json"] and raw["fps_ok"]
                             and all(v["keys_off_integer_frames"] == 0 for v in raw["keys"].values())
                             and all(pose[t]["range_ok"] for t in ALL)
                             and all(pose[t]["loop_seam"]["vertex_mm"] == 0 for t in ("Idle", "Walk"))) else "FAIL",
        "anim_stacks_in_fbx": raw["stacks"], "export_json_stacks": exp["fbx_anim_stacks"],
        "fbx_time_mode": raw["time_mode"], "fbx_custom_frame_rate": raw["custom_frame_rate"],
        "scene_fps_after_import": pose["scene_fps_after_import"],
        "stack_frame_ranges": raw["stack_frame_ranges"], "ranges_match_export_json": raw["ranges_match_export_json"],
        "keys": raw["keys"], "blender_import_actions": pose["imported_actions"],
        "loop_seams": {t: pose[t]["loop_seam"] for t in ("Idle", "Walk")}},
    "2_in_place_origin_scale": {
        "result": "pass" if (all(max(pose[t]["root_travel_mm_xyz"]) == 0 and pose[t]["root_z_mm"] == 0 for t in ALL)
                             and not any(raw["armature_null_animated_channels"].values())
                             and all(max(v.values()) == 0 for v in raw["root_bone_translation_range"].values())) else "FAIL",
        "root_bone_world_travel_mm_xyz": {t: pose[t]["root_travel_mm_xyz"] for t in ALL},
        "root_bone_translation_key_range_fbx": raw["root_bone_translation_range"],
        "armature_null_animated_channels": raw["armature_null_animated_channels"],
        "bone_scale_key_range_fbx": raw["bone_scale_key_range"],
        "bone_world_scale_max_dev": max(pose[t]["bone_scale_max_dev"] for t in ALL),
        "armature_object": pose["armature_object"],
        "note": "root bone and armature node never move in any take; body sway lives on pelvis (Mend pelvis XY range 5.5 cm)"},
    "3_old_takes_vs_previous_verification_numbers": {
        "result": "pass" if old_numbers_ok else "FAIL",
        "compared_with": "verification.json before this check (kept at check_0927/pre_fix/verification.json)",
        "per_take": cmp},
    "4_contact_frames": {
        "result": "pass" if (mt["first_frame_both_slabs_below_ground"] == em["contact_frame"] == 8
                             and min(mt["slab_min_z_cm_by_frame"]["7"]) >= 29
                             and mt["pelvis_peak_frame_20_40"] == em["release_frame"] == 30
                             and mt["dome_top_cm"]["30"] == max(mt["dome_top_cm"][f] for f in ("15", "20", "24", "28", "29", "30", "31", "34", "38"))
                             and mt["first_frame_both_slabs_at_or_above_ground_after_30"] == 39
                             and m["start_vs_idle0"]["vertex_mm"] == 0 and m["end_vs_idle0"]["vertex_mm"] == 0
                             and st["contact_frame_both_slabs"] == 15 and pose["Hit"]["peak_frame"] == 3
                             and dl["first_frame_belly_within_2cm"] == 25) else "FAIL",
        "mend_contact_frame_both_slabs_below_ground": mt["first_frame_both_slabs_below_ground"],
        "mend_slab_min_z_cm_by_frame": mt["slab_min_z_cm_by_frame"],
        "mend_release_pelvis_peak_frame_20_40": mt["pelvis_peak_frame_20_40"],
        "mend_dome_top_cm": mt["dome_top_cm"],
        "mend_slab_tips_at_surface_frame": mt["first_frame_both_slabs_at_or_above_ground_after_30"],
        "mend_slabs_fully_clear_frame": mt["first_frame_both_slabs_fully_above_ground_after_30"],
        "mend_socket_drift_mm_10_38": mt["socket_drift_mm_10_38"],
        "mend_sockets_at_contact_blender_m": mt["sockets_at_8_blender_m"],
        "mend_start_end_vs_idle0": {"start": m["start_vs_idle0"], "end": m["end_vs_idle0"]},
        "slam_contact_frame": st["contact_frame_both_slabs"], "hit_peak_frame": pose["Hit"]["peak_frame"],
        "death_belly_on_ground_frame": dl["first_frame_belly_within_2cm"],
        "note": "Mend: frame 7 both slabs 29-32 cm up, frame 8 both 9 cm in the ground; release 30 = pelvis and dome peak "
                "of the channel (the overall highest vertex is the raised slab on 5); tips at the surface on 39 "
                "(-1 / +16 mm), clear on 40"},
    "5_deformation_rigid_ground": {
        "result": "pass" if (all(pose[t]["rigid_edge_len_dev_mm"] < 0.01 for t in ALL)
                             and m["min_z_mm"]["body"] > 0 and m["min_z_mm"]["head"] > 0
                             and min(m["min_z_mm"]["hind_L"], m["min_z_mm"]["hind_R"]) > -5
                             and mt["hind_feet"]["hind_L"]["min_z_mm_any_frame"] > -5) else "FAIL",
        "rigid_edges": pose["Mend"]["rigid_edges"],
        "rigid_edge_len_dev_mm": {t: pose[t]["rigid_edge_len_dev_mm"] for t in ALL},
        "edge_stretch_ratio_max": {t: pose[t]["edge_stretch_max"] for t in ALL},
        "min_z_mm_per_region": {t: pose[t]["min_z_mm"] for t in ALL},
        "mend_body_head_min_z_mm": mt["body_head_min_z_mm"], "mend_hind_feet": mt["hind_feet"],
        "mend_hind_foot_bones_move_mm": 0.0,
        "mend_sole_roll_note": "hind foot bones fixed; sole skin within 1 cm of the ground rolls with the body up to "
                               "1.4 cm (L) / 1.0 cm (R) and lifts at the heel up to 2 cm; the approved Slam rolls 4.1 / 2.2 cm",
        "renders_looked_at": ["check_0927/out/sheet_mend_game.jpg", "check_0927/out/sheet_mend_side.jpg",
                              "check_0927/out/zoom_8_28_30.jpg", "check_0927/out/sheet_mend_final.jpg"],
        "render_verdict": "game angle + side, frames 1 2 3 5 7 8 20 28 30 39 44 50: slabs planted 8-38, hind feet on "
                          "the ground, nothing floats or sinks, no spikes or torn vertices"},
    "6_mesh_budget_weights": {
        "result": "pass" if (raw["mesh"]["triangles"] <= 25000 and raw["skin"]["max_influences"] <= 4
                             and pose["mesh"]["triangles"] == raw["mesh"]["triangles"]) else "FAIL",
        "triangles_fbx": raw["mesh"]["triangles"], "triangles_import": pose["mesh"]["triangles"],
        "vertices": raw["mesh"]["vertices"], "bones": pose["mesh"]["bones"], "materials": pose["mesh"]["materials"],
        "max_influences": raw["skin"]["max_influences"], "influence_histogram": raw["skin"]["influence_histogram"],
        "weight_sum_range": raw["skin"]["weight_sum_range"]},
    "7_review_page": {
        "result": "pass" if page_ok else "FAIL", "page": str(PAGE), "references": len(refs), "missing": missing,
        "section": "27.09 — новые способности (#new-0927, holds #mend)", "mend_media": media},
    "8_r01_takes_unchanged": {
        "result": "pass" if (raw["old_takes_bit_identical"] and raw["mesh_uv_normals_skin_bind_identical"]
                             and pose["old_takes_pose_identical"]) else "FAIL",
        "compared_with": "work/r01_approved/ForestRootSnarer_r01.fbx (md5 1ab64ca7..., same file as the one in the game)",
        "curves_bytes": raw["old_takes_vs_r01"], "mesh_uv_normals_skin_bind_identical": raw["mesh_uv_normals_skin_bind_identical"],
        "static_items_compared": raw["static_items_compared"],
        "pose_by_pose_after_import": pose["old_takes_vs_r01_pose"], "added_takes": raw["added_takes"]},
}
import sys
sys.path.insert(0, str(HERE))
from fbx_raw import model, TICKS  # noqa: E402
_a = model(str(PKG / "ForestRootSnarer.fbx"))["stacks"][P + "Mend"]["curves"]
_b = model(str(HERE / "pre_fix" / "ForestRootSnarer.fbx"))["stacks"][P + "Mend"]["curves"]
mend_diff_frames = sorted({round(t * 30 / TICKS) for k in _a for t, x, y in zip(_a[k][0], _a[k][1], _b[k][1]) if x != y})
fix_frames = "-".join(str(f) for f in (mend_diff_frames[0], mend_diff_frames[-1]))
out = {"subject": "ForestRootSnarer", "revision": "animation_r01 + 27.09 Mend (independent re-check 27.09)",
       "date": "2026-09-27", "fbx": str(PKG / "ForestRootSnarer.fbx"),
       "method": "own byte-level FBX reader (check_0927/fbx_raw.py) + FBX re-imported into an empty Blender scene "
                 "(check_0927/pose_check.py) + the approved r01 FBX imported into another empty scene and compared "
                 "pose by pose; Mend rendered at the game angle and from the side (check_0927/render_new.py)",
       "all_pass": all(c["result"] == "pass" for c in checks.values()),
       "fixes": [{"what": "Mend lift hitch: key 3 eased 'smooth' then 'out' restarted at full speed (L_hand knuckle "
                          "9 -> 16 -> 9 -> 27 cm/frame); key 3 now eases 'in' (4 -> 13 -> 22 -> 29 cm/frame)",
                  "file": "animation/clip_mend.py", "frames_changed": fix_frames,
                  "proof": {"mend_frames_differing_vs_prefix_fbx": mend_diff_frames,
                            "old_takes_vs_prefix_fbx_identical": all(
                                v["identical_curves"] == v["curves"] for k, v in fixd["old_takes_vs_r01"].items() if not k.endswith("Mend"))}},
                 {"what": "export.json / review page: 'slabs out on 39' made exact (tips at the surface on 39, clear on 40); "
                          "'hind feet 0 mm' made exact (bones fixed, sole skin rolls up to 1.4 cm)"}],
       "pre_fix_copies": "animation/check_0927/pre_fix/ (FBX, export.json, verification.json, clip_mend.py, mend mp4s, page); "
                         "26.09 blends kept at work/r01_approved/*_0926.blend",
       "checks": checks}
(PKG / "verification.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf8")
print("ALL_PASS", out["all_pass"], {k: v["result"] for k, v in checks.items()})
if not old_numbers_ok:
    print(json.dumps(cmp, indent=1))
