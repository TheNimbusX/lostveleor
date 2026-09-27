"""System python: merge build/export/verify/deform-test json into rig_report.json.
python assemble_report.py <rig_dir>
"""
import sys, json
from pathlib import Path

rig = Path(sys.argv[1]).resolve()
work = rig / "work"
build = json.loads((work / "build_final.json").read_text(encoding="utf-8"))
exp = json.loads((work / "export_final.json").read_text(encoding="utf-8"))
ver = json.loads((work / "verify_fbx.json").read_text(encoding="utf-8"))
pose = json.loads((rig / "deform_test" / "pose_report.json").read_text(encoding="utf-8"))

deform_summary = {}
for name, d in pose.items():
    deform_summary[name] = {
        "verts_edge_ratio_over_2x": d["verts_ratio_over_2"],
        "max_edge_ratio_by_region": {r: v["max_stretch"] for r, v in d["edge_ratio_by_region"].items()},
        "edges_over_1_5x_by_region": {r: v["over_1_5"] for r, v in d["edge_ratio_by_region"].items()},
        "ik_target_error_m": d["ik_target_error_m"],
        "renders": sorted(p.name for p in (rig / "deform_test").glob(f"{name}_*.png")),
    }

report = {
    "mob": "ForestRootSnarer (Корнехват)",
    "stage": "STEP 1 game rig",
    "files": {"rig_blend": exp["master_blend"], "rig_fbx": exp["unity_fbx"], "fbx_sha256": exp["fbx_sha256"],
              "textures": exp["textures"], "deform_test_dir": str(rig / "deform_test"),
              "scripts": ["rig_layout.py", "build_rig.py", "rig_build_lib.py", "rig_weights.py", "rig_controls.py",
                          "export_rig.py", "verify_fbx.py", "pose_lib.py", "pose_test.py", "viz_weights.py"]},
    "source_model": build["source"],
    "conventions": {
        "front_axis": "-Y", "up_axis": "+Z", "units": "metres", "fps": 30,
        "origin": "entity origin on the ground (0,0,0); root bone at origin, no root motion baked",
        "side_naming": "creature's own side: L_* = +X, R_* = -X (same as Stonehoof front_left at +X)",
        "fbx": exp["fbx_settings"],
        "armature_object": "ARM_ForestRootSnarer", "mesh_object": "SM_ForestRootSnarer_LOD0",
        "material": exp["materials"],
    },
    "geometry": {"triangles": exp["triangles"], "vertices": exp["vertices"], "budget_triangles": 25000,
                 "height_m": 1.35, "bbox_m": {"min": ver["bbox_min"], "max": ver["bbox_max"]}},
    "bones": exp["bones"],
    "bone_summary": {
        "count_total_in_blend": len(exp["bones"]), "ik_helper_bones": [b["name"] for b in exp["bones"] if b["name"].startswith("CTRL_")],
        "count_exported_fbx": ver["bone_count"],
        "hierarchy": "root > pelvis > spine_01 > spine_02 > neck > head > jaw; spine_02 > {L,R}_clavicle > arm_upper > arm_lower > hand; pelvis > {L,R}_leg_upper > leg_lower > foot",
        "notes": {
            "arm_upper": "short shoulder-junction bone (0.15 m); the slab pivots here",
            "arm_lower": "the whole bark slab incl. the fist, 100% rigid",
            "hand": "knuckle ground-contact socket (exported, 0 skin weights) - use for planting/IK and slam VFX",
            "jaw": "chin drop only (closed mouth mesh), keep <= ~12 deg",
            "mushroom_back": "no own bones; rides pelvis/spine_01/spine_02",
            "IK": "CTRL_{L,R}_{hand,foot} targets + CTRL_{L,R}_{elbow,knee} poles, non-deform, parented to root, constraints IK_* on *_arm_lower / *_leg_lower (chain 2) with influence 0; dropped by the deform-only FBX",
        },
    },
    "weights": {
        "method": "heat pass A (all deform bones) for limbs + heat pass B (torso bones only) for the body; "
                  "clavicle/arm_upper/leg_upper masked to the shoulder/hip junction; slab+fist rigid on arm_lower; "
                  "jaw by rule; 6 Laplacian smoothing passes (rigid verts locked); max 4 influences, normalised",
        "rule_config": build["cfg"], "rule_stats": build["rule_stats"],
        "heat_results": {"A": build["heat_A"], "B": build["heat_B"],
                         "A_unweighted": build["heat_A_unweighted"], "B_unweighted": build["heat_B_unweighted"]},
        **exp["weights"],
    },
    "ik_rest_check": build["ik"],
    "fbx_roundtrip": ver,
    "deformation_test": {
        "poses": {
            "arms_overhead": "slam wind-up like slam ref 0.67-1.5 s: reared up on hind legs (pelvis -50 deg, spine -22), both slabs overhead",
            "arms_slammed": "slam contact like ref 1.67 s: body dropped 0.10 m, slabs swung out to the sides",
            "hind_leg_lift": "L hind thigh -60 deg (knee forward/up), shin +45, foot -20",
            "jaw_open": "head -10, jaw +12",
            "ik_plant": "all 4 IK on, targets at rest, pelvis shoved 0.15 m forward / 0.06 m down",
        },
        "views": "front + side (+ game camera az -60 el 52) textured; *_stretch = per-vertex worst edge ratio heat-map (red >= 2.5x)",
        "results": deform_summary,
        "visual_review": [
            "bark slabs stay rigid in every pose (0 distance change inside the slab after FBX re-import)",
            "mossy dome + mushrooms: no tearing in any pose (close-ups deform_test/shoulder_closeups.png); only 61 of 9533 dome edges exceed 1.5x in arms_overhead, all on the shoulder caps next to the arm junction; 0 in hind_leg_lift",
            "arms_overhead reads like the slam ref; stretch concentrated in armpits and the chest crease under the chin (head flexed ~45 deg vs chest), smooth, no spikes",
            "arms_slammed: small stretch at the inner shoulder only",
            "hind_leg_lift: belly flank in front of the thigh compresses, hip back stretches; no mushroom distortion",
            "jaw_open: small chin drop, face intact",
        ],
    },
    "open_problems": [
        "Rest pose is not planted: +X slab touches z=0, -X slab bottom ~0.06 m, L(+X) hind foot 0.10 m and R hind foot 0.03 m above ground - idle/locomotion clips must plant all four with the IK helpers (or a few-cm pose offset).",
        "Mesh is not mirror-symmetric: L(+X) hind leg ~0.18 m further back than R; L leg is almost straight at rest, so with the body shoved 0.15 m forward the L foot IK falls 1.5 cm short - lower the pelvis a few cm in the base stance.",
        "Hands are unskinned sockets: no wrist bend by design (a wrist bend creased the bark). Knuckle-walk roll must be done by rotating the whole slab (arm_lower).",
        "Extreme arm raise (~150 deg) stretches the armpit / under-chin crease up to ~13x on a few small edges; fine at game camera but avoid flexing the head more than ~30 deg against the chest.",
        "Jaw is a chin-drop only on a closed mouth; no mouth interior.",
        "Candidate mesh has 378 open boundary edges (Tripo decimation); heat solve still succeeded (0 unweighted verts in both passes).",
        "ORM texture is not wired into the FBX standard slots (only Color + NormalGL come through); set up the Unity material from rig/textures.",
    ],
}
(rig / "rig_report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("REPORT", rig / "rig_report.json")
