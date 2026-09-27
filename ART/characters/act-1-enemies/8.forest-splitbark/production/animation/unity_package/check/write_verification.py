"""Step-3 verdict: check/measure.json (+ review page scan) -> unity_package/verification.json. System python.

python write_verification.py
"""
import json
import re
import urllib.parse
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
REVIEW = PKG.parents[1] / "review" / "animation_r01"
m = json.loads((HERE / "measure.json").read_text(encoding="utf-8"))
before = json.loads((HERE / "backup_before_fix" / "measure_before_fix.json").read_text(encoding="utf-8"))
exp = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
T = m["takes"]
names = exp["fbx_take_names"]
checks = {}

# 1 -- takes, names, fps, ranges, loops
seams = {k: T[k]["loop_seam"] for k in names if "loop_seam" in T[k]}
ranges_ok = all(T[k]["frame_range"] == [0.0, float(exp["takes"][k]["frames"][1])]
                and m["fbx"]["stacks"].get(k) == float(exp["takes"][k]["frames"][1]) for k in names)
checks["1_takes"] = {
    "fbx_stacks": m["fbx"]["stacks"], "expected": names, "names_exact": list(m["fbx"]["stacks"]) == names,
    "fbx_fps": m["fbx"]["custom_fps"], "fbx_time_mode": m["fbx"]["time_mode"], "ranges_match_export": ranges_ok,
    "loop_seams": seams, "loop_velocity": {k: T[k]["loop_velocity_jump_mps"] for k in seams},
    "pass": (list(m["fbx"]["stacks"]) == names and m["fbx"]["custom_fps"] == 30.0 and ranges_ok
             and all(s["bone_pos_mm"] <= 1.0 and s["bone_rot_deg"] <= 0.5 for s in seams.values())),
}

# 2 -- in place, origin on the ground, no scale
root = {k: T[k]["root"] for k in names}
checks["2_in_place"] = {
    "root_bone": root, "armature_object_static": {k: T[k]["armature_object_static"] for k in names},
    "armature_object": m["armature_object"], "rest_skin_min_z_mm": m["mesh"]["rest_skin_min_z_mm"],
    "max_bone_scale_dev": max(T[k]["scale"]["max_bone_scale_dev"] for k in names),
    "scale_key_range": [min(T[k]["scale_keys"]["min"] for k in names), max(T[k]["scale_keys"]["max"] for k in names)],
    "facing_guide_static": all(T[k]["facing_guide_static"] for k in names),
    "pass": (all(r["root_travel_xy_mm"] == 0 and r["root_travel_z_mm"] == 0 and r["root_rot_deg"] == 0
                 for r in root.values()) and all(T[k]["armature_object_static"] for k in names)
             and abs(m["mesh"]["rest_skin_min_z_mm"]) < 0.5
             and max(T[k]["scale"]["max_bone_scale_dev"] for k in names) < 1e-4),
}

# 3 -- walk
w = T["ForestSplitter_Walk"]
checks["3_walk"] = {
    "target_mps": w["walk_speed_target_mps"], "measured_mps": w["walk_speed_measured_mps"],
    "ankle_plant_speed_mps": sorted({p["speed_mps"] for leg in w["walk"].values() for p in leg["ankle_plants"]}),
    "max_slide_mm": w["walk_max_slide_mm"],
    "per_leg": {leg: {"sole": v["sole_plants"], "ankle": v["ankle_plants"], "sole_min_z_mm": v["sole_min_z_mm"]}
                for leg, v in w["walk"].items()},
    "note": "sole = centroid of the lowest 15 mm of each paw's skin; it rocks a little while the ankle moves exactly",
    "pass": w["walk_max_slide_mm"] < 20.0 and abs(min(w["walk_speed_measured_mps"]) - 3.1) < 0.2
            and abs(max(w["walk_speed_measured_mps"]) - 3.1) < 0.2,
}

# 4 -- contract frames
b = T["ForestSplitter_Bite"]["bite"]
fwd = b["head_forwardmost_per_frame_m"]
span = b["shell_span_per_frame_m"]
paw = b["front_L_sole_z_per_frame_mm"]
d = T["ForestSplitter_Death"]["death"]
p = T["ForestSplitter_Pop"]["pop"]
beak_frame = max(range(len(fwd)), key=lambda i: fwd[i])
clap_frame = min(range(8, len(span)), key=lambda i: span[i])   # tightest shells after the windup
paw_lands = next(f for f in range(13, len(paw)) if paw[f] <= 1.0 and paw[f - 1] > 5.0)
checks["4_contacts"] = {
    "bite": {"contract_frame": 18, "beak_forwardmost_integer_frame": beak_frame,
             "beak_forwardmost_subframe": b["beak_forwardmost_frame"], "shell_clap_frame": clap_frame,
             "front_L_paw_lands_frame": paw_lands, "body_forwardmost_frame": b["body_forwardmost_frame"],
             "beak_tip_at_contact_m": b["beak_tip_at_contact_m"],
             "export_beak_mesh_tip": exp["takes"]["ForestSplitter_Bite"].get("beak_mesh_tip_at_contact_m")},
    "death": {"release_frame": d["release_frame"], "last_frame": d["last_frame"],
              "shell_span_max_frame": max(range(len(d["shell_span_per_frame_m"])),
                                          key=lambda i: d["shell_span_per_frame_m"][i])},
    "pop": {"contract": p["contract"], "takeoff_first_airborne": p["takeoff_frame_first_airborne"],
            "landing_first_grounded": p["landing_frame_first_grounded"], "airborne": p["airborne_frames"]},
}
c4 = checks["4_contacts"]
c4["pass"] = (beak_frame == 18 and clap_frame == 18 and paw_lands == 18 and b["body_forwardmost_frame"] == 18
              and abs(b["beak_forwardmost_frame"] - 18) <= 0.25 and d["release_frame"] == d["last_frame"] == 12
              and c4["death"]["shell_span_max_frame"] == 12
              and p["takeoff_frame_first_airborne"] == p["contract"]["takeoff"]
              and p["landing_frame_first_grounded"] == p["contract"]["landing"])

# 5 -- deformation and ground
checks["5_deformation"] = {
    "per_take": {k: {"edge_grow_max_mm": T[k]["stretch"]["grow_max_mm"], "at": T[k]["stretch"]["grow_max_at"],
                     "pair": T[k]["stretch"]["grow_max_pair"], "edges_grow_over_5cm": T[k]["stretch"]["edges_grow_over_5cm"],
                     "skin_min_z_mm": T[k]["ground"]["skin_min_z_mm"], "verts_below_minus_1cm": T[k]["ground"]["verts_below_minus_1cm"]}
                 for k in names},
    "bite_head_shell_sleeve_mm": {
        "before_fix": before["takes"]["ForestSplitter_Bite"]["stretch"]["grow_max_mm"],
        "after_fix": max(v[0] for k, v in T["ForestSplitter_Bite"]["stretch"]["pair_max_mm_at"].items()
                         if k.startswith("head-shell")),
        "edges_over_5cm_before_after": [before["takes"]["ForestSplitter_Bite"]["stretch"]["edges_grow_over_5cm"],
                                        T["ForestSplitter_Bite"]["stretch"]["edges_grow_over_5cm"]]},
    "death_shell_split_mm": T["ForestSplitter_Death"]["stretch"]["pair_max_mm_at"].get("shell_L-shell_R"),
    "death_end_pose": {"skin_min_z_mm": d["end_skin_min_z_mm"], "sole_z_mm": d["end_sole_z_mm"]},
    "rest_sole_float_mm": {leg: v["min_z_mm"] for leg, v in T["ForestSplitter_Idle"]["feet"].items()},
    "renders_looked_at": "check/renders/_s_<Take>.jpg (game angle, 5-6 frames per take), _s_Bite_*_new.jpg (close/side)",
}
for k in names:
    checks["5_deformation"]["per_take"][k]["rigid_edges_max_change_mm"] = T[k]["rigid_edges"]["max_length_change_mm"]
    checks["5_deformation"]["per_take"][k]["finite"] = T[k]["finite"]
checks["5_deformation"]["rigid_edge_count"] = T[names[0]]["rigid_edges"]["count"]
checks["5_deformation"]["pass"] = all(
    v["verts_below_minus_1cm"] == 0 and v["finite"] and v["rigid_edges_max_change_mm"] < 0.5
    and v["edge_grow_max_mm"] < 400 for v in checks["5_deformation"]["per_take"].values())

# 6 -- budget
checks["6_budget"] = {"triangles": m["mesh"]["triangles"], "limit": 25000, "max_influences": m["mesh"]["max_influences"],
                      "verts_over_4": m["mesh"]["verts_over_4"], "unweighted_verts": m["mesh"]["unweighted_verts"],
                      "weight_sum_dev_max": m["mesh"]["weight_sum_dev_max"],
                      "pass": m["mesh"]["triangles"] <= 25000 and m["mesh"]["max_influences"] <= 4
                      and m["mesh"]["unweighted_verts"] == 0}

# 7 -- review page
html = (REVIEW / "index.html").read_text(encoding="utf-8")
refs = sorted(set(re.findall(r'(?:src|href)="([^"#][^"]*)"', html)))
missing = [r for r in refs if not r.startswith("http") and not (REVIEW / urllib.parse.unquote(r)).exists()]
sections = [t for t in names if f'id="{t}"' in html]
checks["7_review_page"] = {"page": str(REVIEW / "index.html"), "refs": len(refs), "missing": missing,
                           "take_sections": sections, "pass": not missing and sections == names}

out = {"fbx": str(PKG / "ForestSplitter.fbx"), "fbx_bytes": m["fbx"]["bytes"], "method":
       "FBX re-imported into an empty Blender 5.2 scene (--factory-startup), sampled every 0.25 frame; "
       "check/check_measure.py, check_metrics.py, check_takes.py, check_render.py, probe_bite.py",
       "checks": checks, "all_pass": all(c["pass"] for c in checks.values()),
       "fixes": [
           "Bite: neck jab 0.25 -> 0.11 m (rig validated 0.14 m) + 3.5 cm body lurch + 10 deg nose-up on the same "
           "envelope (takes_bite.py); head/shell sleeve 245 -> 130 mm, beak tip 0.916 -> 0.851 m forward, contact "
           "still frame 18; re-baked, validated, package rebuilt and round-trip verified.",
           "export.json: added beak_mesh_tip_at_contact_m (real beak tip, ~13 cm below the head bone tail) and "
           "child_bite (Splitling 12+8 ticks = contact phase 0.6, reuse Bite at x1.5).",
           "Review page: Bite videos/strip re-rendered, text and issues updated, step-3 summary added."],
       "remaining": [
           "Death release frame: shells split 35 cm apart on the fused mesh, dark stretched moss + a stretched fin "
           "at the front (frames 9-12); needs the split VFX (model change otherwise).",
           "Rest paws: only front_L touches the ground; front_R floats 8.5 mm, hind 6-10 mm (model); hind IK is at "
           "reach 0.97/0.975 so feet cannot be lowered without crouching every take.",
           "Walk: paw skin dips to -7.6 mm; sole contact points slide up to 12.6 mm (ankles exact 3.1 m/s).",
           "Head-shell stretch 130 mm at bite contact is still visible from the side; from the game camera it reads "
           "as a short neck.",
           "Material still named tripo_material_<uuid>; Unity import (loop flags, material) not done."]}
(PKG / "verification.json").write_text(json.dumps(out, indent=2, ensure_ascii=False), encoding="utf-8")
print({k: c["pass"] for k, c in checks.items()}, "ALL", out["all_pass"])
