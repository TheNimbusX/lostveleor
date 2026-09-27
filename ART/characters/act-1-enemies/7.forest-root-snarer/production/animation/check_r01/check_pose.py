"""Independent pose check of the shipped FBX (fresh empty scene, FBX re-imported).
blender -b --factory-startup -P check_pose.py -- <fbx> <out.json>"""
import json, sys
from pathlib import Path
import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_lib as L  # noqa: E402
import check_takes as T  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT = argv[0], Path(argv[1])
EXPECT = {"ForestRootSnarer_Idle": (0, 60, True), "ForestRootSnarer_Walk": (0, 16, True),
          "ForestRootSnarer_Slam": (0, 72, False), "ForestRootSnarer_Hit": (0, 12, False),
          "ForestRootSnarer_Death": (0, 45, False), "ForestRootSnarer_Mend": (0, 50, False)}

arm, mesh, acts = L.import_fbx(FBX)
sc = bpy.context.scene
R = {"fbx": FBX, "scene_fps": sc.render.fps / sc.render.fps_base, "actions": sorted(acts),
     "armature_object": {"location": list(arm.location), "rotation": list(arm.rotation_euler),
                         "scale": list(arm.scale)}}
dom, dw, cnt = L.weights(mesh)
reg = L.regions(dom, dw)
R["mesh"] = {"triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons), "vertices": len(dom),
             "max_influences": int(cnt.max()), "region_sizes": {k: int(len(v)) for k, v in reg.items()}}
# rest (bind) pose
arm.data.pose_position = "REST"
Vr, Br = L.sample(arm, mesh, [0])
arm.data.pose_position = "POSE"
rest_co = Vr[0]
R["rest"] = {"root_head": [round(float(x), 5) for x in Br["root"][0][:3, 3]],
             "facing_head_minus_pelvis": [round(float(x), 3) for x in (Br["head"][0][:3, 3] - Br["pelvis"][0][:3, 3])],
             "L_arm_upper_x": round(float(Br["L_arm_upper"][0][0, 3]), 3)}
aw_inv = np.linalg.inv(np.array(arm.matrix_world))
data = {}
for name, (f0, f1, loop) in EXPECT.items():
    act = acts.get(name)
    if act is None:
        R[name] = {"missing": True}
        continue
    L.set_action(arm, act)
    fr = [int(round(x)) for x in act.frame_range]
    frames = list(range(fr[0], fr[1] + 1))
    V, B = L.sample(arm, mesh, frames)
    data[name] = (V, B)
    r = {"frame_range": fr, "expected": [f0, f1], "range_ok": fr == [f0, f1]}
    root = B["root"][:, :3, 3]
    r["root_travel_mm"] = [round(float(np.ptp(root[:, i])) * 1000, 3) for i in range(3)]
    r["root_head_z_mm"] = round(float(root[:, 2].mean()) * 1000, 3)
    sc_err = 0.0
    for k, M in B.items():
        loc = np.einsum("ij,fjk->fik", aw_inv, M)
        n = np.linalg.norm(loc[:, :3, :3], axis=1)
        sc_err = max(sc_err, float(np.abs(n - 1).max()))
    r["bone_scale_max_dev"] = round(sc_err, 6)
    r["min_z_mm_all"] = round(float(V[:, :, 2].min()) * 1000, 2)
    r["min_z_mm_per_region"] = {k: round(float(V[:, idx, 2].min()) * 1000, 2) for k, idx in reg.items() if k != "all"}
    hi, ahi, lo, alo, E = L.edge_stretch(mesh, V, rest_co)
    r["edge_stretch_max"] = round(hi, 4)
    r["edge_stretch_min"] = round(lo, 4)
    r["edge_stretch_worst_at"] = {"frame": frames[ahi[0]], "bones": sorted({dom[E[ahi[1]][0]], dom[E[ahi[1]][1]]})}
    rig_e = (dom[E[:, 0]] == dom[E[:, 1]]) & (dw[E[:, 0]] >= 0.999) & (dw[E[:, 1]] >= 0.999)
    L0 = np.linalg.norm(rest_co[E[rig_e, 0]] - rest_co[E[rig_e, 1]], axis=1)
    dev = 0.0
    for i in range(len(frames)):
        L1 = np.linalg.norm(V[i][E[rig_e, 0]] - V[i][E[rig_e, 1]], axis=1)
        dev = max(dev, float(np.abs(L1 - L0).max()))
    r["rigid_edges"] = int(rig_e.sum())
    r["rigid_edge_len_dev_mm"] = round(dev * 1000, 4)
    if loop:
        r["loop_seam"] = L.pose_diff(V[0], {k: v[0] for k, v in B.items()}, V[-1], {k: v[-1] for k, v in B.items()})
    R[name] = r
    print("TAKE", name, json.dumps(r), flush=True)

T.take_specific(R, data, reg, dom)
if len(argv) > 2:  # optional cache for diagnostics (keep it outside the repo)
    np.savez_compressed(argv[2], rest=rest_co.astype(np.float32), dom=dom, dw=dw,
                        edges=np.array([e.vertices[:] for e in mesh.data.edges]),
                        **{k.split("_")[-1]: v[0].astype(np.float32) for k, v in data.items()})
OUT.write_text(json.dumps(R, indent=1))
print("CHECK_DONE", OUT, flush=True)
