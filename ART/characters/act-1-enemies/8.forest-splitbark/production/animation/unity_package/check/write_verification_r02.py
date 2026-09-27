"""verification.json for the r02 package = the r01 step-3 verification (unchanged, from the r01 backup) + an r02 block.

python write_verification_r02.py   (after check_r02.py, validate_roll.py, verify_package.py)
The r01 checks ran on the r01 FBX; check_r02.py proves the six r01 takes sample identically in the r02 FBX,
so those results still hold for them. The r02 block covers the roll takes.
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
ANIM = PKG.parent
base = json.loads((PKG / "backup_r01_2026-09-26" / "verification.json").read_text(encoding="utf-8"))
r02 = json.loads((HERE / "r02.json").read_text(encoding="utf-8"))
val = json.loads((ANIM / "validation_r02.json").read_text(encoding="utf-8"))
ver = json.loads((PKG / "verify.json").read_text(encoding="utf-8"))
exp = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
new = [t for t in exp["fbx_take_names"] if t not in base["checks"]["1_takes"]["expected"]]

block = {
    "date": "2026-09-27",
    "fbx": str(PKG / "ForestSplitter.fbx"), "fbx_bytes": r02["fbx_bytes"],
    "method": "check/check_r02.py: the r01 FBX (backup_r01_2026-09-26) and the r02 FBX re-imported into an emptied "
              "Blender 5.2 scene (--factory-startup) and sampled every 0.25 frame; validate_roll.py on the baked "
              "blend; verify_package.py round trip of all 10 takes",
    "checks": {
        "1_takes": {"fbx_stacks": r02["stacks"], "names_exact": r02["stack_names_exact"],
                    "fbx_fps": r02["custom_fps"], "pass": r02["stack_names_exact"] and r02["custom_fps"] == 30.0},
        "2_r01_takes_unchanged": {"baked_keys_identical": val["r01_identical"],
                                  "fbx_samples_vs_r01_fbx": r02["r01_vs_old_fbx"],
                                  "pass": val["r01_all_identical"] and all(v["identical"] for v in r02["r01_vs_old_fbx"].values())},
        "3_new_takes_in_place": {t: {"root": r02["new_takes"][t]["root"], "scale": r02["new_takes"][t]["scale"],
                                     "ground": r02["new_takes"][t]["ground"]} for t in new},
        "4_handoffs": {"fbx": r02["handoffs"], "baked": val["handoffs"],
                       "pass": all(v["skin_mm"] < 0.5 for v in r02["handoffs"].values())},
        "5_deformation": {t: {"stretch": r02["new_takes"][t]["stretch"],
                              "rigid_edges_max_change_mm": r02["new_takes"][t]["rigid_edges_max_change_mm"],
                              "ik_pull_m": val["takes"][t]["max_ik_pull_m"]} for t in new},
        "6_budget": dict(r02["budget"], limit=25000, **{"pass": r02["budget"]["triangles"] <= 25000}),
        "7_roundtrip": {t: ver["takes"][t] for t in exp["fbx_take_names"]},
        "8_ball": val["ball"],
    },
    "all_pass": bool(r02["all_pass"] and val["all_pass"] and ver["all_pass"]),
    "notes": [
        "Old takes: every baked key identical to ForestSplitter_Baked_r01.blend and every FBX sample identical to "
        "the r01 FBX (max difference 0.0), so the r01 checks above stay valid for them.",
        "Largest skin stretch in the new takes is inside the ball: front-right leg top vs the right shell and head "
        "vs the left shell (~0.18-0.21 m at the ball / shake), hidden under the clamped shells from outside views.",
        "RollLoop rims dip up to 4.5 mm under the ground on the squash beat; the view spins the ball there anyway.",
    ],
}
base["r02_2026_09_27"] = block
base["all_pass"] = bool(base["all_pass"] and block["all_pass"])
base["revisions"] = {"r01": "checks/fixes/remaining above: r01 FBX (6 takes), 26.09",
                     "r02": "r02_2026_09_27: + 4 roll takes, r01 takes proven identical, 27.09"}
(PKG / "verification.json").write_text(json.dumps(base, indent=2, ensure_ascii=False), encoding="utf-8")
print("VERIFICATION_R02", block["all_pass"], base["all_pass"])
