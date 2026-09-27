"""Check the baked takes against the authored metrics and the game contract.

blender -b ForestSplitter_Baked_r01.blend -P validate_bake.py  -> validation.json
"""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import measure  # noqa: E402
import takes_loops as tl  # noqa: E402
from takes import R01_ORDER as ORDER, TAKES  # noqa: E402  (r01 only; roll takes: bake_roll.py)

LEGS = ("front_L", "front_R", "hind_L", "hind_R")
scene = bpy.context.scene
arm = bpy.data.objects["ARM_ForestSplitter"]
mesh = bpy.data.objects["SM_ForestSplitter_LOD0"]
authored = json.loads((HERE / "bake_report.json").read_text(encoding="utf-8"))["takes"]
out = {"fps": scene.render.fps, "deform_bones": len(arm.data.bones),
       "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons), "takes": {}}


def at(f):
    scene.frame_set(int(f), subframe=f - int(f))
    pbs = arm.pose.bones
    return {"ankle": {leg: pbs["leg_%s_foot" % leg].head.copy() for leg in LEGS}, "beak": pbs["head"].tail.copy(),
            "root": pbs["root"].matrix.copy(), "body": pbs["body"].head.copy()}


ok = True
for take in ORDER:
    spec = TAKES[take]
    act = bpy.data.actions[take]
    arm.animation_data.action = act
    n = spec["frames"]
    rows = authored[take]["rows"]
    t = {"range": list(act.frame_range), "loop": act.use_cyclic}
    err, root_dev, minz, beak_y, body_xy = 0.0, 0.0, 1.0, [], []
    for f in range(n + 1):
        s = at(f)
        for leg in LEGS:
            err = max(err, (s["ankle"][leg] - Vector(rows[f]["ankle"][leg])).length)
        root_dev = max(root_dev, max(abs(a - b) for ra, rb in zip(s["root"], arm.data.bones["root"].matrix_local)
                                     for a, b in zip(ra, rb)))
        z, _ = measure.skin_min_z(mesh)
        minz = min(minz, z)
        beak_y.append(round(s["beak"].y, 4))
        body_xy.append((round(s["body"].x, 4), round(s["body"].y, 4)))
    t["bake_vs_authored_max_ankle_err_m"] = round(err, 5)
    t["root_bone_max_dev"] = round(root_dev, 6)
    t["skin_min_z_m"] = round(minz, 4)
    t["max_ik_pull_m"] = max([max(r["pulled"].values()) for r in rows if r["pulled"]] or [0.0])
    t["max_reach"] = max(max(r["reach"].values()) for r in rows)
    first, last = at(0), at(n)
    t["start_end_body_xy_delta_m"] = round((Vector(first["body"][:2]) - Vector(last["body"][:2])).length, 5)
    if spec["loop"]:
        t["loop_seam_max_ankle_m"] = round(max((first["ankle"][l] - last["ankle"][l]).length for l in LEGS), 5)
    if take.endswith("Bite"):
        c = spec["contact"]
        t["contact_frame"] = c
        t["beak_forwardmost_frame"] = min(range(len(beak_y)), key=lambda i: beak_y[i])
        t["beak_y"] = {"rest": beak_y[0], "contact": beak_y[c]}
        t["shell_clap_frame"] = min(range(n + 1), key=lambda i: rows[i]["shell"][0])
    if take.endswith("Walk"):
        # planted feet must travel backwards at the sim speed (in-place trot)
        speeds = []
        for f in range(n):
            a, b = rows[f], rows[f + 1]
            for leg in LEGS:
                if leg in a["planted"] and leg in b["planted"]:
                    speeds.append((b["ankle"][leg][1] - a["ankle"][leg][1]) * 30)
        t["stance_foot_speed_mps"] = [round(min(speeds), 3), round(max(speeds), 3)]
        t["stride_m_per_cycle"] = round(tl.STEP * tl.WALK_N, 4)
    if take.endswith("Pop"):
        z = [round(r["ankle"]["hind_L"][2], 3) for r in rows]
        t["hind_ankle_z_by_frame"] = z
    t["pass"] = (err < 0.002 and root_dev < 1e-5 and minz > -0.012 and t["max_ik_pull_m"] < 0.001
                 and (not spec["loop"] or t["loop_seam_max_ankle_m"] < 1e-4)
                 and (spec["loop"] or take.endswith(("Death", "Pop")) or t["start_end_body_xy_delta_m"] < 1e-4))
    if take.endswith("Bite"):
        t["pass"] = t["pass"] and t["beak_forwardmost_frame"] == t["contact_frame"]
    ok = ok and t["pass"]
    out["takes"][take] = t
    print("VALID", take, json.dumps(t), flush=True)
out["all_pass"] = ok
(HERE / "validation.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
print("VALIDATION_DONE", ok)
