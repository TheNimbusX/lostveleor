"""Check the r02 roll takes in ForestSplitter_Baked_r02.blend against the authored metrics and the sim contract,
and prove the r01 takes are untouched.

blender -b ForestSplitter_Baked_r02.blend -P validate_roll.py  -> validation_r02.json
 - r01 takes: every fcurve key (frame, value, interpolation) equal to ForestSplitter_Baked_r01.blend
 - new takes: bake vs authored ankles, root bone static, skin min z, IK pull, loop seam, hand-off seams
   (Idle f0 -> Curl f0, Curl f30 = Loop f0 = Uncurl f0 = Dizzy f0, Uncurl/Dizzy last = Idle f0)
 - ball: bounds, lateral-axis spin pivot and radius (for the view that spins the body while rolling)
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import measure  # noqa: E402
from takes import R01_ORDER, R02_NEW, TAKES  # noqa: E402

LEGS = ("front_L", "front_R", "hind_L", "hind_R")
scene = bpy.context.scene
arm = bpy.data.objects["ARM_ForestSplitter"]
mesh = bpy.data.objects["SM_ForestSplitter_LOD0"]
assert Path(bpy.data.filepath).name == "ForestSplitter_Baked_r02.blend"
authored = json.loads((HERE / "bake_report_r02.json").read_text(encoding="utf-8"))["takes"]
names = [g.name for g in mesh.vertex_groups]
dom = np.array([names[max(v.groups, key=lambda g: g.weight).group] for v in mesh.data.vertices])
shell = np.isin(dom, ["shell_L", "shell_R"])
out = {"blend": bpy.data.filepath, "fps": scene.render.fps, "takes": {}, "handoffs": {}, "r01_identical": {}}


def fcurves(act):
    return list(act.layers[0].strips[0].channelbag(act.slots[0]).fcurves)


def keys(act):
    d = {}
    for fc in fcurves(act):
        kp = fc.keyframe_points
        co = np.empty(len(kp) * 2)
        kp.foreach_get("co", co)
        it = np.empty(len(kp), dtype=np.int32)
        kp.foreach_get("interpolation", it)
        d[(fc.data_path, fc.array_index)] = (co, it)
    return d


# ---- r01 takes: bit-identical to the approved baked blend ----
before = set(bpy.data.actions)
with bpy.data.libraries.load(str(HERE / "ForestSplitter_Baked_r01.blend"), link=False) as (src, dst):
    dst.actions = list(R01_ORDER)
loaded = {a for a in bpy.data.actions if a not in before}
old_by_name = {}
for a in loaded:
    base = a.name.rsplit(".", 1)[0] if a.name[-4:-3] == "." else a.name
    old_by_name[base] = a
all_same = True
for take in R01_ORDER:
    a, b = keys(bpy.data.actions[take]), keys(old_by_name[take])
    same = a.keys() == b.keys() and all(np.array_equal(a[k][0], b[k][0]) and np.array_equal(a[k][1], b[k][1])
                                        for k in a)
    r = bpy.data.actions[take]
    o = old_by_name[take]
    same = same and tuple(r.frame_range) == tuple(o.frame_range) and r.use_cyclic == o.use_cyclic
    out["r01_identical"][take] = {"fcurves": len(a), "keys": int(sum(len(v[1]) for v in a.values())), "identical": same}
    all_same = all_same and same
for a in loaded:
    bpy.data.actions.remove(a)


def use(take):
    act = bpy.data.actions[take]
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]


def at(f):
    scene.frame_set(int(f), subframe=f - int(f))
    pbs = arm.pose.bones
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return {"M": {pb.name: np.array(pb.matrix) for pb in pbs},
            "ankle": {leg: np.array(pbs["leg_%s_foot" % leg].head) for leg in LEGS},
            "S": co.reshape(-1, 3)}


def diff(a, b):
    pos = max(float(np.linalg.norm(a["M"][n][:3, 3] - b["M"][n][:3, 3])) for n in a["M"])
    rot = 0.0
    for n in a["M"]:   # chord form: no arccos noise near 0 deg
        d = float(np.linalg.norm(a["M"][n][:3, :3] - b["M"][n][:3, :3])) / (2 * np.sqrt(2))
        rot = max(rot, float(np.degrees(2 * np.arcsin(min(1.0, d)))))
    skin = float(np.linalg.norm(a["S"] - b["S"], axis=1).max())
    return {"bone_pos_mm": round(pos * 1000, 4), "bone_rot_deg": round(rot, 4), "skin_mm": round(skin * 1000, 4)}


use("ForestSplitter_Idle")
idle0 = at(0)
rest_root = np.array(arm.data.bones["root"].matrix_local)
snap = {}
ok = all_same
for take in R02_NEW:
    spec = TAKES[take]
    n = spec["frames"]
    act = bpy.data.actions[take]
    use(take)
    rows = authored[take]["rows"]
    t = {"range": list(act.frame_range), "loop": act.use_cyclic, "frames": n}
    err, root_dev, minz, shellz = 0.0, 0.0, 1.0, 1.0
    for f in [x * 0.25 for x in range(4 * n + 1)]:
        s = at(f)
        if f == int(f):
            for leg in LEGS:
                err = max(err, float(np.linalg.norm(s["ankle"][leg] - np.array(rows[int(f)]["ankle"][leg]))))
        root_dev = max(root_dev, float(np.abs(s["M"]["root"] - rest_root).max()))
        minz = min(minz, float(s["S"][:, 2].min()))
        shellz = min(shellz, float(s["S"][shell, 2].min()))
        if f in (0, n):
            snap[(take, int(f))] = s
    t["bake_vs_authored_max_ankle_err_m"] = round(err, 5)
    t["root_bone_max_dev"] = round(root_dev, 7)
    t["skin_min_z_m"] = round(minz, 4)
    t["shell_min_z_m"] = round(shellz, 4)
    t["max_ik_pull_m"] = max([max(r["pulled"].values()) for r in rows if r["pulled"]] or [0.0])
    t["max_reach"] = max(max(r["reach"].values()) for r in rows)
    if spec["loop"]:
        t["loop_seam"] = diff(snap[(take, 0)], snap[(take, n)])
    t["pass"] = (err < 0.002 and root_dev < 1e-5 and minz > -0.012 and t["max_ik_pull_m"] < 0.001
                 and (not spec["loop"] or t["loop_seam"]["skin_mm"] < 0.1))
    out["takes"][take] = t
    print("VALID", take, json.dumps(t), flush=True)
    ok = ok and t["pass"]

C, L, U, D = ("ForestSplitter_RollCurl", "ForestSplitter_RollLoop", "ForestSplitter_RollUncurl",
              "ForestSplitter_RollDizzy")
pairs = {"Idle f0 -> RollCurl f0": (idle0, snap[(C, 0)]),
         "RollCurl f30 -> RollLoop f0": (snap[(C, 30)], snap[(L, 0)]),
         "RollLoop f0 -> RollUncurl f0": (snap[(L, 0)], snap[(U, 0)]),
         "RollLoop f0 -> RollDizzy f0": (snap[(L, 0)], snap[(D, 0)]),
         "RollUncurl f30 -> Idle f0": (snap[(U, 30)], idle0),
         "RollDizzy f45 -> Idle f0": (snap[(D, 45)], idle0)}
for k, (a, b) in pairs.items():
    out["handoffs"][k] = diff(a, b)
    ok = ok and out["handoffs"][k]["skin_mm"] < 0.1

# ---- ball geometry over the whole loop (entity-local; Blender -Y = forward) ----
use(L)
lo, hi, rmax = np.full(3, 9.0), np.full(3, -9.0), 0.0
S0 = snap[(L, 0)]["S"]
c = (S0.min(0) + S0.max(0)) / 2
for f in range(TAKES[L]["frames"] + 1):
    S = at(f)["S"]
    lo, hi = np.minimum(lo, S.min(0)), np.maximum(hi, S.max(0))
    rmax = max(rmax, float(np.sqrt((S[:, 1] - c[1]) ** 2 + (S[:, 2] - c[2]) ** 2).max()))
out["ball"] = {
    "bounds_min_m": [round(float(x), 3) for x in lo], "bounds_max_m": [round(float(x), 3) for x in hi],
    "size_m": {"width_x": round(float(hi[0] - lo[0]), 3), "length_y": round(float(hi[1] - lo[1]), 3),
               "height_z": round(float(hi[2] - lo[2]), 3)},
    "spin_pivot_blender": [0.0, round(float(c[1]), 3), round(float(c[2]), 3)],
    "spin_pivot_unity_local": [0.0, round(float(c[2]), 3), round(float(-c[1]), 3)],
    "spin_radius_m": round(rmax, 3),
    "lift_to_clear_ground_m": round(max(0.0, rmax - float(c[2])), 3),
    "what": "bounds centre of the ball pose (RollLoop f0); spin_radius = farthest skin vertex from the lateral "
            "axis through that pivot over the whole loop. Spinning about the pivot at its own height clips the "
            "ground by up to lift_to_clear_ground_m.",
}
out["r01_all_identical"] = all_same
out["all_pass"] = ok
(HERE / "validation_r02.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
print("VALIDATION_R02", ok, json.dumps({"handoffs": out["handoffs"], "ball": out["ball"], "r01": all_same}), flush=True)
