"""27.09 independent check, part B: re-import the shipped FBX into an empty scene and measure it; then
import the approved r01 FBX into another empty scene and compare the old takes pose-by-pose.
blender -b --factory-startup -P pose_check.py -- <new.fbx> <r01.fbx> <out.json>"""
import json, math, sys
from pathlib import Path
import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
NEW, OLD, OUT = argv[0], argv[1], Path(argv[2])
P = "ForestRootSnarer_"
TAKES = {"Idle": (0, 60, True), "Walk": (0, 16, True), "Slam": (0, 72, False), "Hit": (0, 12, False),
         "Death": (0, 45, False), "Mend": (0, 50, False)}
REG = {"slab_L": ["L_arm_lower"], "slab_R": ["R_arm_lower"], "hind_L": ["L_foot"], "hind_R": ["R_foot"],
       "body": ["pelvis", "spine_01", "spine_02"], "head": ["neck", "head", "jaw"]}


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, anim_offset=0.0)
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    mesh = [o for o in bpy.data.objects if o.type == "MESH"]
    assert len(arm) == 1 and len(mesh) == 1, (arm, mesh)
    return arm[0], mesh[0], {a.name.split("|")[-1]: a for a in bpy.data.actions}


def use(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if hasattr(ad, "action_slot") and ad.action_slot is None and len(act.slots):
        ad.action_slot = act.slots[0]


def sample(arm, mesh, frames):
    sc = bpy.context.scene
    n = len(mesh.data.vertices)
    V = np.zeros((len(frames), n, 3))
    names = [pb.name for pb in arm.pose.bones]
    M = np.zeros((len(frames), len(names), 4, 4))
    buf = np.empty(n * 3, np.float32)
    for i, f in enumerate(frames):
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        me = ev.to_mesh()
        me.vertices.foreach_get("co", buf)
        mw = np.array(mesh.matrix_world)
        V[i] = buf.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
        ev.to_mesh_clear()
        aw = np.array(arm.matrix_world)
        for j, pb in enumerate(arm.pose.bones):
            M[i, j] = aw @ np.array(pb.matrix)
    return V, M, names


def rot_deg(A, B):
    ra = A[:3, :3] / np.linalg.norm(A[:3, :3], axis=0)
    rb = B[:3, :3] / np.linalg.norm(B[:3, :3], axis=0)
    c = (np.trace(ra.T @ rb) - 1) / 2
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


def pdiff(Va, Ma, Vb, Mb):
    return {"bone_pos_mm": round(float(np.linalg.norm(Ma[:, :3, 3] - Mb[:, :3, 3], axis=1).max()) * 1000, 3),
            "bone_rot_deg": round(max(rot_deg(a, b) for a, b in zip(Ma, Mb)), 4),
            "vertex_mm": round(float(np.linalg.norm(Va - Vb, axis=1).max()) * 1000, 3)}


# ---------------- new FBX
arm, mesh, acts = load(NEW)
sc = bpy.context.scene
R = {"fbx": NEW, "scene_fps_after_import": sc.render.fps / sc.render.fps_base,
     "imported_actions": sorted(acts), "armature_object": {
         "loc": [round(v, 6) for v in arm.location], "rot_deg": [round(math.degrees(v), 4) for v in arm.rotation_euler],
         "scale": [round(v, 6) for v in arm.scale], "has_own_animation": bool(arm.animation_data and any(
             fc.data_path in ("location", "rotation_euler", "rotation_quaternion", "scale")
             for a in bpy.data.actions for fc in getattr(a, "fcurves", [])))}}
gid = {g.index: g.name for g in mesh.vertex_groups}
nv = len(mesh.data.vertices)
dom = np.empty(nv, object)
dw = np.zeros(nv)
cnt = np.zeros(nv, int)
for v in mesh.data.vertices:
    gs = [(g.weight, gid[g.group]) for g in v.groups if g.weight > 0]
    cnt[v.index] = len(gs)
    w, b = max(gs)
    dom[v.index], dw[v.index] = b, w
reg = {}
for r, bones in REG.items():
    m = np.isin(dom, bones)
    if r.startswith("slab"):
        m &= dw >= 0.99
    reg[r] = np.nonzero(m)[0]
R["mesh"] = {"triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons), "vertices": nv,
             "max_influences": int(cnt.max()), "bones": len(arm.data.bones), "materials": len(mesh.data.materials),
             "region_sizes": {k: len(v) for k, v in reg.items()}}
E = np.array([e.vertices[:] for e in mesh.data.edges])
arm.data.pose_position = "REST"
Vr, Mr, names = sample(arm, mesh, [0])
arm.data.pose_position = "POSE"
rest = Vr[0]
bi = {n: i for i, n in enumerate(names)}
rig = (dom[E[:, 0]] == dom[E[:, 1]]) & (dw[E[:, 0]] >= 0.999) & (dw[E[:, 1]] >= 0.999)
L0 = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
aw_inv = np.linalg.inv(np.array(arm.matrix_world))
DATA = {}
for t, (f0, f1, loop) in TAKES.items():
    act = acts.get(P + t)
    if act is None:
        R[t] = {"missing": True}
        continue
    use(arm, act)
    fr = [round(v, 4) for v in act.frame_range]
    frames = list(range(f0, f1 + 1))
    V, M, _ = sample(arm, mesh, frames)
    DATA[t] = (V, M)
    root = M[:, bi["root"], :3, 3]
    r = {"action_frame_range": fr, "range_ok": fr == [f0, f1],
         "root_travel_mm_xyz": [round(float(np.ptp(root[:, k])) * 1000, 4) for k in range(3)],
         "root_z_mm": round(float(root[:, 2].max()) * 1000, 4)}
    loc = np.einsum("ij,fbjk->fbik", aw_inv, M)
    r["bone_scale_max_dev"] = round(float(np.abs(np.linalg.norm(loc[:, :, :3, :3], axis=2) - 1).max()), 6)
    r["min_z_mm"] = {k: round(float(V[:, idx, 2].min()) * 1000, 2) for k, idx in reg.items()}
    r["min_z_mm"]["all"] = round(float(V[:, :, 2].min()) * 1000, 2)
    ok = L0 > 1e-5
    worst, wf = 1.0, 0
    dev = 0.0
    for i in range(len(frames)):
        L1 = np.linalg.norm(V[i][E[:, 0]] - V[i][E[:, 1]], axis=1)
        ratio = L1[ok] / L0[ok]
        if ratio.max() > worst:
            worst, wf = float(ratio.max()), frames[i]
        dev = max(dev, float(np.abs(L1[rig] - L0[rig]).max()))
    r["edge_stretch_max"] = round(worst, 4)
    r["edge_stretch_max_frame"] = wf
    r["rigid_edges"] = int(rig.sum())
    r["rigid_edge_len_dev_mm"] = round(dev * 1000, 4)
    if loop:
        r["loop_seam"] = pdiff(V[0], M[0], V[-1], M[-1])
    R[t] = r
    print("TAKE", t, json.dumps(r), flush=True)

V0, M0 = DATA["Idle"][0][0], DATA["Idle"][1][0]
for t in ("Walk", "Slam", "Hit", "Death", "Mend"):
    V, M = DATA[t]
    R[t]["start_vs_idle0"] = pdiff(V[0], M[0], V0, M0)
    if t != "Death":
        R[t]["end_vs_idle0"] = pdiff(V[-1], M[-1], V0, M0)


def zmin(V, r):
    return V[:, reg[r], 2].min(1)


# ---- Walk contacts (ground scrolls +Y at 2.4 m/s under the in-place walk)
V, M = DATA["Walk"]
vw = 2.4 / 30
wk = {}
seq = [f % 16 for f in range(33)]
for r in ("slab_L", "slab_R", "hind_L", "hind_R"):
    Z = np.stack([V[f][reg[r], 2] for f in seq])
    XY = np.stack([V[f][reg[r], :2] for f in seq])
    touch = Z < 0.003
    dys, slide = [], 0.0
    for i in range(32):
        both = touch[i] & touch[i + 1]
        if both.any():
            dys.append(float((XY[i + 1, both, 1] - XY[i, both, 1]).mean()))
    for k in range(Z.shape[1]):
        i = 0
        while i < 33:
            if not touch[i, k]:
                i += 1
                continue
            j = i
            while j + 1 < 33 and touch[j + 1, k]:
                j += 1
            if j > i:
                c = XY[i:j + 1, k] - np.array([[0, vw * m] for m in range(j - i + 1)])
                slide = max(slide, float(np.linalg.norm(c - c[0], axis=1).max()))
            i = j + 1
    wk[r] = {"ground_speed_mps": round(float(np.mean(dys)) * 30, 3), "slide_cm": round(slide * 100, 2),
             "min_z_mm": round(float(Z.min()) * 1000, 1)}
R["Walk"]["contacts_3mm"] = wk

# ---- Slam
V, M = DATA["Slam"]
zL, zR = zmin(V, "slab_L"), zmin(V, "slab_R")
both = np.maximum(zL, zR)
contact = next(i for i in range(5, 73) if both[i] <= 0.01)
hL, hR = M[:, bi["L_hand"], :3, 3], M[:, bi["R_hand"], :3, 3]
drift = max(max(float(np.linalg.norm(hL[i] - hL[16])), float(np.linalg.norm(hR[i] - hR[16]))) for i in range(16, 61))
R["Slam"]["timing"] = {"contact_frame_both_slabs": contact,
                       "slab_min_z_cm_10_16": {f: [round(float(zL[f]) * 100, 1), round(float(zR[f]) * 100, 1)] for f in range(10, 17)},
                       "socket_drift_mm_16_60": round(drift * 1000, 2),
                       "slabs_fully_out_frame": next(i for i in range(contact + 1, 73) if min(zL[i], zR[i]) > 0),
                       "pelvis_z_cm_30_40": [round(float(M[i, bi["pelvis"], 2, 3]) * 100, 1) for i in range(30, 41)]}
# ---- Hit / Death
V, M = DATA["Hit"]
hd = np.linalg.norm(M[:, bi["head"], :3, 3] - M[0, bi["head"], :3, 3], axis=1)
R["Hit"]["peak_frame"] = int(hd.argmax())
V, M = DATA["Death"]
bz = zmin(V, "body")
steps = [float(np.linalg.norm(M[i, :, :3, 3] - M[i - 1, :, :3, 3], axis=1).max()) for i in range(33, 46)]
R["Death"]["landing"] = {"first_frame_belly_within_2cm": next(i for i in range(46) if bz[i] < 0.02),
                         "still_32_45_max_bone_step_mm": round(max(steps) * 1000, 2),
                         "end_min_z_mm": {k: round(float(V[-1][idx, 2].min()) * 1000, 1) for k, idx in reg.items()}}

# ---- Mend (new take): stab contact, pinned channel, release heave, pull-out, Idle 0 at the end
V, M = DATA["Mend"]
zL, zR = zmin(V, "slab_L"), zmin(V, "slab_R")
top = V[:, :, 2].max(1)
pz = M[:, bi["pelvis"], 2, 3]
hL, hR = M[:, bi["L_hand"], :3, 3], M[:, bi["R_hand"], :3, 3]
hind = {}
for r in ("hind_L", "hind_R"):
    Z = V[:, reg[r], 2]
    low = Z[0] < 0.003                 # sole vertices on the ground in frame 0 (== Idle 0)
    xy = V[:, reg[r], :2][:, low]
    hind[r] = {"sole_vertices": int(low.sum()),
               "sole_z_mm_range": [round(float(Z[:, low].min()) * 1000, 2), round(float(Z[:, low].max()) * 1000, 2)],
               "sole_xy_drift_mm": round(float(np.linalg.norm(xy - xy[0], axis=2).max()) * 1000, 2),
               "min_z_mm_any_frame": round(float(Z.min()) * 1000, 2)}
body_head = np.concatenate([reg["body"], reg["head"]])
bh = V[:, body_head, 2].min(1)
first_in = next(i for i in range(51) if max(zL[i], zR[i]) < 0.0)
first_touch = next(i for i in range(51) if max(zL[i], zR[i]) <= 0.01)
R["Mend"]["timing"] = {
    "slab_min_z_cm_by_frame": {f: [round(float(zL[f]) * 100, 1), round(float(zR[f]) * 100, 1)] for f in range(51)},
    "first_frame_both_slabs_below_ground": first_in,
    "first_frame_both_slabs_within_1cm": first_touch,
    "last_frame_any_slab_below_1mm": int(max(i for i in range(51) if min(zL[i], zR[i]) < -0.001)),
    "first_frame_both_slabs_at_or_above_ground_after_30": next(i for i in range(31, 51) if min(zL[i], zR[i]) >= -0.0015),
    "first_frame_both_slabs_fully_above_ground_after_30": next(i for i in range(31, 51) if min(zL[i], zR[i]) > 0),
    "slab_depth_cm_8_38": [round(float(min(zL[8:39].min(), zR[8:39].min())) * 100, 1),
                           round(float(max(zL[8:39].max(), zR[8:39].max())) * 100, 1)],
    "socket_drift_mm_10_38": round(max(max(float(np.linalg.norm(hL[i] - hL[10])), float(np.linalg.norm(hR[i] - hR[10])))
                                       for i in range(10, 39)) * 1000, 2),
    "sockets_at_8_blender_m": {"L": [round(float(x), 4) for x in hL[8]], "R": [round(float(x), 4) for x in hR[8]]},
    "dome_top_cm": {f: round(float(top[f]) * 100, 1) for f in (0, 5, 8, 15, 20, 24, 28, 29, 30, 31, 34, 38, 44, 50)},
    "dome_top_peak_frame": int(top.argmax()),
    "pelvis_peak_frame_20_40": int(20 + pz[20:41].argmax()),
    "body_head_min_z_mm": round(float(bh.min()) * 1000, 1), "body_head_min_z_frame": int(bh.argmin()),
    "hind_feet": hind,
    "max_bone_step_mm_per_frame": round(max(float(np.linalg.norm(M[i, :, :3, 3] - M[i - 1, :, :3, 3], axis=1).max())
                                            for i in range(1, 51)) * 1000, 1),
    "max_vertex_step_mm_per_frame": round(max(float(np.linalg.norm(V[i] - V[i - 1], axis=1).max())
                                              for i in range(1, 51)) * 1000, 1)}
# pops: largest frame-to-frame acceleration of any vertex (a snap reads as a spike here)
acc = [float(np.linalg.norm(V[i + 1] - 2 * V[i] + V[i - 1], axis=1).max()) for i in range(1, 50)]
R["Mend"]["timing"]["max_vertex_accel_mm_per_f2"] = round(max(acc) * 1000, 1)
R["Mend"]["timing"]["max_vertex_accel_frame"] = int(1 + np.argmax(acc))
print("MEND", json.dumps(R["Mend"]["timing"]), flush=True)
for t in ("Slam", "Walk", "Death"):
    Vt = DATA[t][0]
    a = [float(np.linalg.norm(Vt[i + 1] - 2 * Vt[i] + Vt[i - 1], axis=1).max()) for i in range(1, len(Vt) - 1)]
    R[t]["max_vertex_accel_mm_per_f2"] = round(max(a) * 1000, 1)

# ---------------- old takes: approved r01 FBX vs new FBX, pose by pose
new_names = names
arm, mesh, acts = load(OLD)
cmp = {}
for t in ("Idle", "Walk", "Slam", "Hit", "Death"):
    act = acts[P + t]
    use(arm, act)
    f0, f1, _ = TAKES[t]
    V, M, nm = sample(arm, mesh, list(range(f0, f1 + 1)))
    order = [nm.index(n) for n in new_names]
    Vn, Mn = DATA[t]
    cmp[t] = {"old_action_range": [round(v, 4) for v in act.frame_range], "frames": f1 - f0 + 1,
              "max_bone_matrix_abs_diff": float(np.abs(M[:, order] - Mn).max()),
              "max_vertex_diff_m": float(np.abs(V - Vn).max())}
R["old_takes_vs_r01_pose"] = cmp
R["old_takes_pose_identical"] = all(c["max_bone_matrix_abs_diff"] == 0 and c["max_vertex_diff_m"] == 0 for c in cmp.values())
print("CMP", json.dumps(cmp), flush=True)
OUT.write_text(json.dumps(R, indent=1))
print("POSE_DONE", flush=True)
