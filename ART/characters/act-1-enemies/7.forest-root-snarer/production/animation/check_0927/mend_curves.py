"""Diagnostics for the Mend take of the shipped FBX: per-frame trajectories (smoothness / pops / hind feet).
blender -b --factory-startup -P mend_curves.py -- <fbx> <out.json>"""
import json, sys
from pathlib import Path
import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT = argv[0], Path(argv[1])
TAKE = argv[2] if len(argv) > 2 else "ForestRootSnarer_Mend"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX, use_anim=True, ignore_leaf_bones=False, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.data.objects if o.type == "MESH")
act = next(a for a in bpy.data.actions if a.name.endswith(TAKE))
ad = arm.animation_data or arm.animation_data_create()
ad.action = act
if hasattr(ad, "action_slot") and ad.action_slot is None:
    ad.action_slot = act.slots[0]
gid = {g.index: g.name for g in mesh.vertex_groups}
dom = []
for v in mesh.data.vertices:
    w, b = max((g.weight, gid[g.group]) for g in v.groups)
    dom.append((b, w))
idx = {k: np.array([i for i, (b, w) in enumerate(dom) if b == bone and (w >= 0.99 or "foot" in bone)])
       for k, bone in (("slab_L", "L_arm_lower"), ("slab_R", "R_arm_lower"), ("foot_L", "L_foot"), ("foot_R", "R_foot"))}
sc = bpy.context.scene
n = int(round(act.frame_range[1]))
bones = ["pelvis", "spine_02", "head", "L_hand", "R_hand", "L_foot", "R_foot", "L_arm_lower", "R_arm_lower"]
T = {b: [] for b in bones}
C = {k: [] for k in idx}
Vf = {k: [] for k in ("foot_L", "foot_R")}
buf = np.empty(len(mesh.data.vertices) * 3, np.float32)
for f in range(n + 1):
    sc.frame_set(f)
    for b in bones:
        pb = arm.pose.bones[b]
        T[b].append(list(arm.matrix_world @ pb.head))
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    me.vertices.foreach_get("co", buf)
    co = buf.reshape(-1, 3) @ np.array(mesh.matrix_world)[:3, :3].T + np.array(mesh.matrix_world)[:3, 3]
    ev.to_mesh_clear()
    for k, ii in idx.items():
        C[k].append(co[ii].mean(0).tolist())
    for k in Vf:
        Vf[k].append(co[idx[k]])
out = {"take": TAKE, "frames": n}
for b in bones:
    a = np.array(T[b])
    v = np.linalg.norm(np.diff(a, axis=0), axis=1)
    out[b] = {"z_cm": [round(float(z) * 100, 1) for z in a[:, 2]], "speed_cm_per_f": [round(float(x) * 100, 1) for x in v],
              "xy_range_cm": [round(float(np.ptp(a[:, 0])) * 100, 2), round(float(np.ptp(a[:, 1])) * 100, 2)]}
for k in C:
    a = np.array(C[k])
    out["centroid_" + k] = {"z_cm": [round(float(z) * 100, 1) for z in a[:, 2]],
                            "speed_cm_per_f": [round(float(x) * 100, 1) for x in np.linalg.norm(np.diff(a, axis=0), axis=1)]}
for k in Vf:
    a = np.stack(Vf[k])          # F, n, 3
    sole = a[0, :, 2] < 0.01      # within 1 cm of the ground on frame 0
    d = np.linalg.norm(a[:, sole, :2] - a[0, sole, :2], axis=2)   # F, s
    out["sole_" + k] = {"n": int(sole.sum()), "xy_drift_mm_per_frame_max": [round(float(x) * 1000, 1) for x in d.max(1)],
                        "z_mm_per_frame_min_max": [[round(float(a[f, sole, 2].min()) * 1000, 1), round(float(a[f, sole, 2].max()) * 1000, 1)]
                                                   for f in range(n + 1)]}
OUT.write_text(json.dumps(out, indent=1))
print("CURVES_DONE", flush=True)
