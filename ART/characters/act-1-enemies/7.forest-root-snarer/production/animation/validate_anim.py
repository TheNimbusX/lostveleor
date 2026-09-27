"""Evaluated-mesh checks for every take in the working scene.
blender -b ForestRootSnarer_Anim_r01.blend -P validate_anim.py -- <out.json>
Ground contact, slip of planted contacts, loop seams, in-place root, contact frames."""
import sys, json
from pathlib import Path
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_walk  # noqa: E402

out = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
sc = bpy.context.scene
arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
rig = anim_core.Rig(arm, mesh)
for pb in arm.pose.bones:
    for c in pb.constraints:
        c.influence = 0.0
gi = {g.index: g.name for g in mesh.vertex_groups}
dom = np.array([gi[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in mesh.data.vertices])
slab = {s: dom == f"{s}_arm_lower" for s in "LR"}
body = rig.body_mask()
not_slab = ~(slab["L"] | slab["R"])
face = np.isin(dom, ("neck", "head", "jaw"))


def sample(f):
    sc.frame_set(f)
    co = rig.mesh_co()
    pb = arm.pose.bones
    r = {"min_z": float(co[:, 2].min()), "body_min_z": float(co[body][:, 2].min()),
         "nonslab_min_z": float(co[not_slab][:, 2].min()),
         "socket": {s: np.array(pb[f"{s}_hand"].head) for s in "LR"},
         "ankle": {s: np.array(pb[f"{s}_foot"].head) for s in "LR"},
         "slab_low": {s: co[slab[s]][co[slab[s]][:, 2].argmin()] for s in "LR"},
         "slab_min_z": {s: float(co[slab[s]][:, 2].min()) for s in "LR"},
         "slab_co": {s: co[slab[s]].copy() for s in "LR"},
         "pelvis": np.array(pb["pelvis"].head), "root": np.array(pb["root"].head),
         "top_z": float(co[:, 2].max()), "face_min_z": float(co[face][:, 2].min()),
         "mats": {b: np.array(pb[b].matrix) for b in rig.deform}}
    return r


def r4(x):
    return round(float(x), 4)


res = {}
for act in bpy.data.actions:
    name = act.name
    arm.animation_data.action = act
    n = int(act.frame_range[1])
    S = [sample(f) for f in range(n + 1)]
    d = {"frames": n, "min_z_all": r4(min(s["min_z"] for s in S)), "min_z_without_slabs": r4(min(s["nonslab_min_z"] for s in S)),
         "root_moves_m": r4(max(np.linalg.norm(s["root"]) for s in S)),
         "pelvis_xy_range_m": r4(max(np.ptp([s["pelvis"][k] for s in S]) for k in (0, 1)))}
    if act.get("loop"):
        d["loop_seam_max_matrix_diff"] = r4(max(np.abs(S[0]["mats"][b] - S[n]["mats"][b]).max() for b in rig.deform))
    if name.endswith(("Idle", "Hit")):
        d["planted_drift_m"] = {f"{s}_{k}": r4(max(np.linalg.norm(x[k][s][:2] - S[0][k][s][:2]) for x in S))
                                for s in "LR" for k in ("socket", "ankle")}
        d["slab_min_z_range"] = [r4(min(min(x["slab_min_z"].values()) for x in S)), r4(max(max(x["slab_min_z"].values()) for x in S))]
    if name.endswith("Walk"):
        v = clip_walk.V
        st = {}
        for s in "LR":
            errs, low_slip, ank_err = [], [], []
            for f in range(n):
                w0, _ = clip_walk.arm_phase(f, s)
                w1, _ = clip_walk.arm_phase(f + 1, s)
                if w0 is None and w1 is None:
                    dv = S[f + 1]["socket"][s] - S[f]["socket"][s]
                    errs.append(np.linalg.norm(dv[:2] - np.array([0, v])))
                    a, b = S[f]["slab_co"][s], S[f + 1]["slab_co"][s]
                    touch = a[:, 2] < a[:, 2].min() + 0.01          # verts touching the ground
                    rel = b[touch][:, :2] - a[touch][:, :2] - np.array([0, v])
                    low_slip.append(float(np.linalg.norm(rel, axis=1).mean()))
                h = clip_walk.HIND[s]
                a0 = clip_walk.limb(f, h["down"], clip_walk.HIND_PERIOD, clip_walk.HIND_DUTY, h["yc"], 0)[2]
                a1 = clip_walk.limb(f + 1, h["down"], clip_walk.HIND_PERIOD, clip_walk.HIND_DUTY, h["yc"], 0)[2]
                if a0 is None and a1 is None:
                    dv = S[f + 1]["ankle"][s] - S[f]["ankle"][s]
                    ank_err.append(np.linalg.norm(dv - np.array([0, v, 0])))
            st[s] = {"slab_socket_speed_err_m_per_frame": r4(max(errs)), "slab_touching_verts_slip_m_per_frame": r4(max(low_slip)),
                     "hind_ankle_speed_err_m_per_frame": r4(max(ank_err)), "stance_frames_checked": len(errs)}
        d["contacts_vs_2_4_mps"] = st
        d["swing_slab_min_clearance_m"] = r4(min(S[f]["slab_min_z"][s] for f in range(n) for s in "LR"
                                                 if clip_walk.arm_phase(f, s)[0] is not None and 0.15 < clip_walk.arm_phase(f, s)[0] < 0.85))
    if name.endswith("Slam"):
        d["slab_min_z_by_frame"] = {f: [r4(S[f]["slab_min_z"]["L"]), r4(S[f]["slab_min_z"]["R"])] for f in (12, 13, 14, 15, 16, 36, 60, 63, 66, 72)}
        d["first_frame_slabs_below_ground"] = next(f for f in range(4, n + 1) if max(S[f]["slab_min_z"].values()) < -0.02)
        d["pinned_socket_drift_15_60_m"] = r4(max(np.linalg.norm(S[f]["socket"][s] - S[16]["socket"][s]) for f in range(16, 61) for s in "LR"))
        d["pinned_depth_range_m"] = [r4(min(min(S[f]["slab_min_z"].values()) for f in range(15, 61))),
                                     r4(max(max(S[f]["slab_min_z"].values()) for f in range(15, 61)))]
        d["vfx_sockets_at_contact"] = {s: [r4(c) for c in S[15]["socket"][s]] for s in "LR"}
        d["hind_ankle_drift_m"] = r4(max(np.linalg.norm(S[f]["ankle"][s] - S[0]["ankle"][s]) for f in range(n + 1) for s in "LR"))
    if name.endswith("Mend"):
        both = [max(S[f]["slab_min_z"].values()) for f in range(n + 1)]
        d["slab_min_z_by_frame"] = {f: [r4(S[f]["slab_min_z"]["L"]), r4(S[f]["slab_min_z"]["R"])]
                                    for f in (0, 3, 5, 6, 7, 8, 9, 10, 20, 28, 30, 33, 38, 39, 40, 42, 46, 50)}
        d["first_frame_slabs_below_ground"] = next(f for f in range(1, n + 1) if both[f] < -0.02)
        d["last_frame_slabs_above_ground_before_contact"] = max(f for f in range(0, d["first_frame_slabs_below_ground"]) if min(S[f]["slab_min_z"].values()) > 0.01)
        d["first_frame_slabs_out_after_38"] = next(f for f in range(38, n + 1) if min(S[f]["slab_min_z"].values()) > -0.002)
        d["pinned_socket_drift_10_38_m"] = r4(max(np.linalg.norm(S[f]["socket"][s] - S[10]["socket"][s]) for f in range(10, 39) for s in "LR"))
        d["socket_move_8_to_10_m"] = r4(max(np.linalg.norm(S[10]["socket"][s] - S[8]["socket"][s]) for s in "LR"))
        d["pinned_depth_range_m"] = [r4(min(min(S[f]["slab_min_z"].values()) for f in range(8, 39))),
                                     r4(max(max(S[f]["slab_min_z"].values()) for f in range(8, 39)))]
        d["vfx_sockets_at_contact"] = {s: [r4(c) for c in S[8]["socket"][s]] for s in "LR"}
        d["hind_ankle_drift_m"] = r4(max(np.linalg.norm(S[f]["ankle"][s] - S[0]["ankle"][s]) for f in range(n + 1) for s in "LR"))
        top = [float(S[f]["top_z"]) for f in range(n + 1)]
        d["dome_top_z_by_frame"] = {f: r4(top[f]) for f in (0, 10, 15, 20, 24, 28, 29, 30, 31, 33, 38)}
        d["dome_top_peak_frame_24_36"] = max(range(24, 37), key=lambda f: top[f])
        d["pelvis_z_by_frame"] = {f: r4(S[f]["pelvis"][2]) for f in (0, 8, 10, 15, 20, 24, 28, 29, 30, 31, 33, 38, 42, 50)}
        d["channel_body_min_z_8_38"] = r4(min(S[f]["body_min_z"] for f in range(8, 39)))
        d["channel_face_min_z_10_29"] = r4(min(S[f]["face_min_z"] for f in range(10, 30)))
        d["end_vs_start_max_matrix_diff"] = r4(max(np.abs(S[0]["mats"][b] - S[n]["mats"][b]).max() for b in rig.deform))
    if name.endswith("Death"):
        d["body_min_z_by_frame"] = {f: r4(S[f]["body_min_z"]) for f in (0, 13, 19, 23, 24, 25, 26, 28, 32, 40, 45)}
        d["first_frame_body_on_ground"] = next(f for f in range(n + 1) if S[f]["body_min_z"] < 0.005)
        d["still_32_45_max_bone_move_m"] = r4(max(np.linalg.norm(S[f]["mats"][b][:3, 3] - S[45]["mats"][b][:3, 3])
                                                 for f in range(32, 46) for b in rig.deform))
        d["resting_45_body_min_z"] = r4(S[45]["body_min_z"])
    res[name] = d
    print("VALID", name, json.dumps({k: v for k, v in d.items() if not isinstance(v, dict) or k.startswith("contacts")})[:400], flush=True)
out.write_text(json.dumps(res, indent=1))
print("VALIDATE_DONE", out)
