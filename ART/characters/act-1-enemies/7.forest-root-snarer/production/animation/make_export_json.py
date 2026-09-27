"""unity_package/export.json: take -> frame range, loop, contact/release frames, sockets + checks.
python make_export_json.py  (after build_package.py, verify_package.py, validate_anim.py)"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE / "unity_package"
val = json.loads((HERE / "work" / "validation.json").read_text())
fbx = json.loads((PKG / "fbx_check.json").read_text())
build = json.loads((HERE / "work" / "build_report.json").read_text())
ver_walk = json.loads((HERE / "check_r01" / "out" / "pose_check.json").read_text())["ForestRootSnarer_Walk"]


def unity(p):
    """Blender model space (faces -Y, L = +X, Z up) -> Unity model space after this FBX import
    (faces +Z, Y up): (x, y, z) -> (-x, z, -y)."""
    return [round(-p[0], 3), round(p[2], 3), round(-p[1], 3)]


sock = val["ForestRootSnarer_Slam"]["vfx_sockets_at_contact"]
walk = val["ForestRootSnarer_Walk"]
mend = val["ForestRootSnarer_Mend"]
msock = mend["vfx_sockets_at_contact"]
old_vs_r01 = json.loads((HERE / "check_r01" / "out" / "old_takes_vs_r01.json").read_text())
takes = {
    "ForestRootSnarer_Idle": {"frames": [0, 60], "loop": True, "seconds": 2.0,
                              "notes": "two heavy breaths per loop, all four contacts planted; frame 60 == frame 0"},
    "ForestRootSnarer_Walk": {"frames": [0, 16], "loop": True, "seconds": 0.533, "speed_mps": 2.4,
                              "ground_per_cycle_m": 1.28,
                              "contact_frames": {"L_slab_plant": 0, "R_slab_plant": 8,
                                                 "R_hind_plant": [1.5, 9.5], "L_hind_plant": [5.5, 13.5]},
                              "slab_stance_frames": 5.5,
                              "notes": "in place; planted contacts roll: the slab / sole vertex on the ground moves back at "
                                       "0.08 m/frame (2.4 m/s, measured 2.38-2.41 on the skinned mesh, slide <= 0.93 cm per "
                                       "plant), the knuckle socket rolls at 0.069-0.086 m/frame. The Sim must move the entity "
                                       "at 2.4 m/s (or scale the clip speed by speed/2.4) for zero foot skate"},
    "ForestRootSnarer_Slam": {"frames": [0, 72], "loop": False, "seconds": 2.4,
                              "contact_frame": 15, "roots_erupt_frame": 36, "release_frame": 60, "recovery_end_frame": 72,
                              "phases": {"gather": [0, 3], "rear_up_slabs_overhead": [3, 12], "slam": [12, 15],
                                         "pinned_straining": [15, 60], "wrench_out_and_settle": [60, 72]},
                              "sim_ticks": "contact 15 = slam tick (circle opens under the hero); roots erupt tick 36; "
                                           "punish window 36..72 (36 ticks); clip ends on Idle frame 0",
                              "vfx_sockets": {"bones": ["L_hand", "R_hand"],
                                              "note": "hand bones are knuckle points of the slabs (no skin); "
                                                      "use them for impact dust / root VFX",
                                              "at_contact_blender_m": sock,
                                              "at_contact_unity_model_m": {s: unity(v) for s, v in sock.items()}},
                              "slab_depth_below_ground_m": [abs(v) for v in val["ForestRootSnarer_Slam"]["pinned_depth_range_m"][::-1]]},
    "ForestRootSnarer_Hit": {"frames": [0, 12], "loop": False, "seconds": 0.4,
                             "notes": "flinch peaks on frame 3; starts and ends on Idle frame 0"},
    "ForestRootSnarer_Death": {"frames": [0, 45], "loop": False, "seconds": 1.5,
                               "ground_contact_frame": 25, "still_from_frame": 32,
                               "notes": "recoil 0-8, buckle 8-19, belly on the ground 25 (face down between the slabs, "
                                        "chin on the ground too), small bounce 28, lies still 32-45 (hold the last frame)"},
    "ForestRootSnarer_Mend": {"frames": [0, 50], "loop": False, "seconds": 1.667, "added": "2026-09-27",
                              "ability": "support heal «Волна из корней» (heal ring, target frame vfx_target_frames_2026-09-27/1-mend-ring.png)",
                              "contact_frame": 8, "channel_frames": [8, 30], "release_frame": 30,
                              "pull_out_frames": [38, 50], "recovery_end_frame": 50,
                              "phases": {"short_lift": [0, 5], "stab": [5, 8], "channel_rooted_pulse": [8, 30],
                                         "release_heave": [28, 30], "stands_open_slabs_in_ground": [30, 38],
                                         "pull_out_and_settle": [38, 50]},
                              "sim_ticks": "t0 plant starts (stab); slabs in the ground from frame 8 (first frame both slabs "
                                           "below ground; frame 7 both still >= 29 cm up); channel to 30; heal ring on tick 30 "
                                           "(release heave, dome top peaks exactly on 30); recovery 30-50, slab tips reach the "
                                           "surface on 39 (L -1 mm, R +16 mm) and are clear of it on 40, clip ends on Idle frame 0",
                              "lift_note": "0-3 eases in (accelerates through the passing pose on 3), 3-5 settles to the top; "
                                           "27.09 independent check re-eased frames 1-2 (the lift stopped on 3 and restarted, "
                                           "a hitch); frames 0 and 3-50 unchanged",
                              "pulse": "calm, no tremble: swell 15 / push 20 / swell 24 / deepest push 28 (dome top "
                                       "1.24 / 1.18 / 1.24 / 1.17 m), release 30 (dome top 1.32 m); head kept low",
                              "vfx_sockets": {"bones": ["L_hand", "R_hand"],
                                              "note": "knuckle points of the slabs; ring / root VFX from both sockets or "
                                                      "from the entity origin between them; the sockets are fixed 10-38",
                                              "at_contact_blender_m": msock,
                                              "at_contact_unity_model_m": {s: unity(v) for s, v in msock.items()}},
                              "mushroom_puff": "the mushrooms have no bones (they ride pelvis / spine_01 / spine_02): the puff "
                                               "on 30 is the back arching up; a spore puff VFX on the dome sells it",
                              "slab_depth_below_ground_m": [abs(v) for v in mend["pinned_depth_range_m"][::-1]]},
}
for name, t in takes.items():
    c = fbx["takes"].get(name, {})
    t["fbx_check"] = {"frame_range": c.get("frame_range"), "max_bone_diff_vs_blender_m": c.get("max_bone_head_diff_m"),
                      "root_xy_travel_m": c.get("root_xy_travel_m")}
out = {
    "subject": "ForestRootSnarer", "name_ru": "Корнехват", "revision": "animation_r01 + 27.09 Mend",
    "date": "2026-09-27", "history": {"2026-09-26": "animation_r01: Idle, Walk, Slam, Hit, Death (approved)",
                                      "2026-09-27": "added ForestRootSnarer_Mend (support heal «Волна из корней»); "
                                                    "r01 takes unchanged bit for bit; independent check re-eased Mend frames 1-2 "
                                                    "(lift hitch), contact / release / ranges unchanged"},
    "fbx": "ForestRootSnarer.fbx", "fps": 30, "root_motion": False,
    "root_motion_note": "clips are in place; the root bone never moves; the Sim moves the entity",
    "muzzle_offset": None, "muzzle_note": "melee mob, no projectile; slam and mend VFX sockets are the L_hand / R_hand bones",
    "axes": {"blender": "faces -Y, Z up, L side = +X", "fbx_export": {"axis_forward": "-Z", "axis_up": "Y"},
             "unity": "faces +Z, Y up (same settings as ForestRootSnarer_Rig.fbx and the Stonehoof / Wendigo packages)"},
    "fbx_settings": {"deform_bones_only": True, "leaf_bones": False, "bake_every_frame": True, "simplify": 0,
                     "takes_from": "NLA strips (take names exact, no armature prefix)", "apply_unit_scale": True,
                     "global_scale": 1.0, "rig_type_in_unity": "Generic"},
    "mesh": {"triangles": fbx["triangles"], "vertices": fbx["vertices"], "materials": fbx["materials"],
             "bones": fbx["imported_bones"], "max_influences": 4, "budget_triangles": 25000},
    "textures": {"folder": "textures/", "in_fbx": ["T_ForestRootSnarer_Color.png", "T_ForestRootSnarer_NormalGL.png"],
                 "set_up_by_hand": ["T_ForestRootSnarer_ORM.png (R occlusion, G roughness, B metallic)"]},
    "fbx_anim_stacks": fbx["anim_stacks_in_fbx"],
    "takes": takes,
    "checks": {"fbx_roundtrip": "re-imported FBX matches the Blender takes (bone and vertex difference 0.0 m)",
               "independent_check": "verification.json (FBX parsed + re-imported into an empty scene; all 8 checks)",
               "r01_takes_unchanged": {"bit_identical": old_vs_r01["all_old_takes_bit_identical"],
                                       "mesh_skin_bind_identical": old_vs_r01["mesh_skin_bind_identical"],
                                       "compared_with": "work/r01_approved/ForestRootSnarer_r01.fbx",
                                       "report": "check_r01/out/old_takes_vs_r01.json"},
               "walk_contact_slide_on_ground_cm": {k: v["max_slide_during_plant_cm_3mm"] for k, v in ver_walk["contacts"].items()},
               "walk_contact_ground_speed_mps": {k: v["ground_speed_mps_3mm"] for k, v in ver_walk["contacts"].items()},
               "walk_knuckle_socket_speed_err_m_per_frame": max(v["slab_socket_speed_err_m_per_frame"] for v in walk["contacts_vs_2_4_mps"].values()),
               "loop_seams": {"Idle": val["ForestRootSnarer_Idle"]["loop_seam_max_matrix_diff"], "Walk": walk["loop_seam_max_matrix_diff"]},
               "slam_body_min_z_m": val["ForestRootSnarer_Slam"]["min_z_without_slabs"],
               "slam_pinned_socket_drift_m": val["ForestRootSnarer_Slam"]["pinned_socket_drift_15_60_m"],
               "death_first_frame_on_ground": val["ForestRootSnarer_Death"]["first_frame_body_on_ground"],
               "death_rest_body_min_z_m": val["ForestRootSnarer_Death"]["resting_45_body_min_z"],
               "death_still_32_45_max_bone_move_m": val["ForestRootSnarer_Death"]["still_32_45_max_bone_move_m"],
               "mend_first_frame_slabs_in_ground": mend["first_frame_slabs_below_ground"],
               "mend_pinned_socket_drift_10_38_m": mend["pinned_socket_drift_10_38_m"],
               "mend_dome_top_peak_frame": mend["dome_top_peak_frame_24_36"],
               "mend_slabs_out_frame": mend["first_frame_slabs_out_after_38"],
               "mend_body_min_z_m": mend["min_z_without_slabs"],
               "mend_end_vs_idle0_max_matrix_diff": mend["end_vs_start_max_matrix_diff"],
               "max_ik_reach_error_m": max(b["max_reach_err"] for b in build.values())},
}
(PKG / "export.json").write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf8")
print("EXPORT_JSON", PKG / "export.json")
