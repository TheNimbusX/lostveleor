"""Checks 1, 2, 5 (numbers), 6 on the re-imported FBX -> out/takes.json.

blender -b -P v_takes.py
"""
import math
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402

arm, mesh = vc.load()
scene = bpy.context.scene
acts = vc.arm_actions(arm)
R = {"objects": sorted((o.name, o.type) for o in bpy.data.objects), "scene_fps": scene.render.fps / scene.render.fps_base,
     "imported_takes": sorted(acts), "expected_takes": sorted(vc.EXPORT["takes"]), "takes": {}}

# 6: triangles and weights
R["triangles"] = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
nz = [sum(1 for g in v.groups if g.weight > 1e-4) for v in mesh.data.vertices]
ws = [sum(g.weight for g in v.groups) for v in mesh.data.vertices]
R["max_weights_per_vertex"] = max(nz)
R["vertices_over_4_weights"] = sum(1 for n in nz if n > 4)
R["weight_sum_min_max"] = [min(ws), max(ws)]
R["unweighted_vertices"] = sum(1 for n in nz if n == 0)
R["armature_world_scale"] = list(arm.matrix_world.to_scale())
R["mesh_parent"] = mesh.parent.name if mesh.parent else None
R["mesh_armature_modifier"] = [m.object.name for m in mesh.modifiers if m.type == "ARMATURE"]

dom = vc.dominant(mesh)
spike = vc.rigid(mesh, "LeftHand") | vc.rigid(mesh, "RightHand")
rigid_sets = {b: vc.rigid(mesh, b) for b in ("LeftHand", "RightHand", "Head")}
E = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
mesh.data.edges.foreach_get("vertices", E)
E = E.reshape(-1, 2)
rc = vc.rest_co(mesh)
L0 = np.linalg.norm(rc[E[:, 0]] - rc[E[:, 1]], axis=1)
okE = L0 > 1e-5
rigidE = {b: m[E[:, 0]] & m[E[:, 1]] & okE for b, m in rigid_sets.items()}
R["rest_min_z"] = float(rc[:, 2].min())
R["rest_xy_center"] = [float(x) for x in (rc[:, :2].min(0) + rc[:, :2].max(0)) / 2]


def pose_mats():
    return {pb.name: np.array(pb.matrix) for pb in arm.pose.bones}


def rot_deg(a, b):
    ra = a[:3, :3] / np.linalg.norm(a[:3, :3], axis=0)
    rb = b[:3, :3] / np.linalg.norm(b[:3, :3], axis=0)
    c = (np.trace(ra.T @ rb) - 1) / 2
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


for take, spec in vc.EXPORT["takes"].items():
    act = acts.get(take)
    if act is None:
        R["takes"][take] = {"missing": True}
        continue
    vc.use(arm, act)
    f0, f1 = spec["frames"]
    r = {"action_range": list(act.frame_range), "expected": [f0, f1]}
    # object-level channels and bone scale keys
    obj_span, scale_dev = 0.0, 0.0
    for fc in vc.fcurves(act):
        v = [k.co[1] for k in fc.keyframe_points]
        if not fc.data_path.startswith("pose."):
            obj_span = max(obj_span, max(v) - min(v))
        elif fc.data_path.endswith(".scale"):
            scale_dev = max(scale_dev, max(abs(x - 1) for x in v))
    r["object_level_channel_span"] = obj_span
    r["bone_scale_max_dev_from_1"] = scale_dev
    arm_t, hips, minz, minz_body, st = [], [], [], [], []
    per, mats = {}, {}
    worst = (1.0, 1.0, -1, "", -1, "")
    rigid_dev = 0.0
    for f in range(f0, f1 + 1):
        vc.goto(f)
        arm_t.append(np.array(arm.matrix_world.translation))
        hips.append(np.array(arm.matrix_world @ arm.pose.bones["Hips"].head))
        co = vc.mesh_co(mesh)
        per[f] = co
        if f in (f0, f1):
            mats[f] = pose_mats()
        minz.append(float(co[:, 2].min()))
        minz_body.append(float(co[~spike, 2].min()))
        L = np.linalg.norm(co[E[:, 0]] - co[E[:, 1]], axis=1)
        ratio = np.where(okE, L / np.where(okE, L0, 1), 1.0)
        i, j = int(ratio.argmax()), int(ratio.argmin())
        if ratio[i] > worst[0]:
            worst = (float(ratio[i]), worst[1], f, str(dom[E[i, 0]]), worst[4], worst[5])
        if ratio[j] < worst[1]:
            worst = (worst[0], float(ratio[j]), worst[2], worst[3], f, str(dom[E[j, 0]]))
        for b, m in rigidE.items():
            rigid_dev = max(rigid_dev, float(np.abs(ratio[m] - 1).max()))
    arm_t, hips = np.array(arm_t), np.array(hips)
    r["armature_object_travel_m"] = float(np.linalg.norm(arm_t - arm_t[0], axis=1).max() + np.linalg.norm(arm_t[0]))
    r["hips_xy_range_m"] = [float(x) for x in hips[:, :2].max(0) - hips[:, :2].min(0)]
    r["hips_xy_net_first_to_last_m"] = [float(x) for x in hips[-1, :2] - hips[0, :2]]
    r["hips_xy_max_from_origin_m"] = float(np.linalg.norm(hips[:, :2], axis=1).max())
    r["min_z_all"] = min(minz)
    r["min_z_all_frame"] = f0 + int(np.argmin(minz))
    r["min_z_body"] = min(minz_body)
    r["min_z_body_frame"] = f0 + int(np.argmin(minz_body))
    r["frames_body_below_minus2cm"] = [f0 + i for i, z in enumerate(minz_body) if z < -0.02]
    r["edge_stretch_max"] = {"ratio": worst[0], "frame": worst[2], "bone": worst[3]}
    r["edge_squash_min"] = {"ratio": worst[1], "frame": worst[4], "bone": worst[5]}
    r["rigid_parts_edge_dev_max"] = rigid_dev
    # seam / velocity continuity
    seam_v = float(np.linalg.norm(per[f0] - per[f1], axis=1).max())
    seam_t = max(float(np.linalg.norm(mats[f0][b][:3, 3] - mats[f1][b][:3, 3])) for b in mats[f0])
    seam_r = max(rot_deg(mats[f0][b], mats[f1][b]) for b in mats[f0])
    r["first_last_vertex_max_m"] = seam_v
    r["first_last_bone_pos_max_m"] = seam_t
    r["first_last_bone_rot_max_deg"] = seam_r
    if spec["loop"]:
        v_in = per[f1] - per[f1 - 1]
        v_out = per[f0 + 1] - per[f0]
        r["loop_velocity_jump_m_per_frame"] = float(np.linalg.norm(v_in - v_out, axis=1).max())
        r["loop_ok"] = seam_v < 0.001 and seam_r < 0.5
    # max per-frame vertex speed anywhere (pops)
    sp = [float(np.linalg.norm(per[f + 1] - per[f], axis=1).max()) for f in range(f0, f1)]
    r["max_vertex_step_m"] = {"value": max(sp), "frame": f0 + int(np.argmax(sp))}
    acc = [float(np.linalg.norm(per[f + 1] - 2 * per[f] + per[f - 1], axis=1).max()) for f in range(f0 + 1, f1)]
    ia = int(np.argmax(acc))
    r["max_vertex_accel_m_per_frame2"] = {"value": acc[ia], "frame": f0 + 1 + ia}
    np.save(vc.OUT / f"co_{take}.npy", np.stack([per[f] for f in range(f0, f1 + 1)]).astype(np.float32))
    R["takes"][take] = r
    print("TAKE", take, {k: r[k] for k in ("action_range", "armature_object_travel_m", "min_z_body", "edge_stretch_max")}, flush=True)
np.save(vc.OUT / "rest.npy", rc.astype(np.float32))
np.save(vc.OUT / "dominant.npy", dom)
np.save(vc.OUT / "spike_mask.npy", spike)
vc.dump("takes.json", R)
