"""27.09 INDEPENDENT check of the r02 ForestSplitter.fbx (10 takes). Nothing is saved except check/r02_indep.json.

blender -b --factory-startup -P check_r02_indep.py
Part A (raw FBX, no Blender import): the curve data (KeyTime + KeyValueFloat per bone channel) of every r01 stack
in the r02 FBX vs the backed-up r01 FBX, stack spans, global axes/fps, geometry + skin clusters, tris, influences.
Part B (Blender import into an emptied scene, sampled every 0.25 frame): old takes vs the r01 FBX and vs the
numbers stored in verification.json (r01 block); new takes vs export.json (ranges, in place, loop seam, hand-offs,
contact frames, rigid parts, ground contact, ball size / spin pivot).
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import check_metrics as cm  # noqa: E402  (numpy-only metric definitions used for the stored r01 numbers)
import check_takes as ct  # noqa: E402

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NEW = Path(ARGS[0]) if ARGS else PKG / "ForestSplitter.fbx"          # optional: another FBX to check
OUT_JSON = HERE / (ARGS[1] if len(ARGS) > 1 else "r02_indep.json")
OLD = PKG / "backup_r01_2026-09-26" / "ForestSplitter.fbx"
EXP = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
VER = json.loads((PKG / "verification.json").read_text(encoding="utf-8"))
OLD_EXP = json.loads((OLD.parent / "export.json").read_text(encoding="utf-8"))
R01 = OLD_EXP["fbx_take_names"]
ALL = EXP["fbx_take_names"]
NEWT = [t for t in ALL if t not in R01]
KT = 46186158000
STEP = 0.25
scene = bpy.context.scene
rep = {"fbx": str(NEW), "fbx_bytes": NEW.stat().st_size, "old_fbx": str(OLD)}


# ============================ Part A: raw FBX ============================
def parse(path):
    from io_scene_fbx import parse_fbx
    root, ver = parse_fbx.parse(str(path))
    objs, conns, gs = {}, [], {}
    for el in root.elems:
        if el.id == b"GlobalSettings":
            for p in (q for e in el.elems if e.id == b"Properties70" for q in e.elems):
                gs[p.props[0].decode()] = p.props[-1]
        if el.id == b"Objects":
            for ob in el.elems:
                objs[ob.props[0]] = ob
        if el.id == b"Connections":
            conns = [tuple(c.props) for c in el.elems]
    return ver, objs, conns, gs


def name_of(ob):
    return ob.props[1].split(b"\x00")[0].decode()


def sub(ob, key):
    return next((e for e in ob.elems if e.id == key), None)


def p70(ob, key):
    e = sub(ob, b"Properties70")
    if e is None:
        return None
    return next((q.props[-1] for q in e.elems if q.props[0] == key), None)


def fbx_data(path):
    ver, objs, conns, gs = parse(path)
    kids = {}
    for c in conns:
        kids.setdefault(c[2], []).append(c)
    parent_of = {}
    for c in conns:
        parent_of.setdefault(c[1], []).append(c)
    stacks = {}
    for oid, ob in objs.items():
        if ob.id != b"AnimationStack":
            continue
        nm = name_of(ob)
        span = {k: (p70(ob, k.encode()) or 0) / KT * 30 for k in ("LocalStart", "LocalStop", "ReferenceStart", "ReferenceStop")}
        curves = {}
        layers = [c[1] for c in kids.get(oid, []) if objs[c[1]].id == b"AnimationLayer"]
        for lid in layers:
            for c in kids.get(lid, []):
                cn = objs.get(c[1])
                if cn is None or cn.id != b"AnimationCurveNode":
                    continue
                tgt = [(objs[p[2]], p[3].decode()) for p in parent_of.get(c[1], []) if p[0] == b"OP" and p[2] in objs]
                for mdl, prop in tgt:
                    for cc_ in kids.get(c[1], []):
                        crv = objs[cc_[1]]
                        if crv.id != b"AnimationCurve":
                            continue
                        kt = tuple(sub(crv, b"KeyTime").props[0])
                        kv = tuple(sub(crv, b"KeyValueFloat").props[0])
                        curves[(name_of(mdl), prop, cc_[3].decode())] = (kt, kv)
        stacks[nm] = {"span": span, "curves": curves, "layers": len(layers)}
    order = [name_of(ob) for ob in objs.values() if ob.id == b"AnimationStack"]
    geo = [ob for ob in objs.values() if ob.id == b"Geometry"]
    clusters = [ob for ob in objs.values() if ob.id == b"Deformer" and ob.props[2] == b"Cluster"]
    g = geo[0]
    verts = tuple(sub(g, b"Vertices").props[0])
    pvi = tuple(sub(g, b"PolygonVertexIndex").props[0])
    tris, n = 0, 0
    for i in pvi:
        n += 1
        if i < 0:
            tris += n - 2
            n = 0
    infl = {}
    cl_sig = {}
    for cl in clusters:
        idx = sub(cl, b"Indexes")
        w = sub(cl, b"Weights")
        ii = tuple(idx.props[0]) if idx else ()
        ww = tuple(w.props[0]) if w else ()
        for v, x in zip(ii, ww):
            if x > 1e-6:
                infl[v] = infl.get(v, 0) + 1
        cl_sig[name_of(cl)] = (ii, ww, tuple(sub(cl, b"Transform").props[0]), tuple(sub(cl, b"TransformLink").props[0]))
    return {"version": ver, "order": order, "stacks": stacks, "gs": gs, "verts": verts, "pvi": pvi,
            "tris": tris, "max_infl": max(infl.values()), "clusters": cl_sig, "n_geo": len(geo)}


A_new, A_old = fbx_data(NEW), fbx_data(OLD)
gkeys = ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign", "UnitScaleFactor",
         "OriginalUnitScaleFactor", "TimeMode", "CustomFrameRate")
raw = {"fbx_version": A_new["version"],
       "global": {k: A_new["gs"].get(k) for k in gkeys},
       "global_same_as_r01": all(A_new["gs"].get(k) == A_old["gs"].get(k) for k in gkeys),
       "stack_order": A_new["order"], "names_exact_and_ordered": A_new["order"] == ALL,
       "spans": {}, "r01_curves": {}, "geometry_identical_to_r01": (A_new["verts"] == A_old["verts"]
                                                                   and A_new["pvi"] == A_old["pvi"]),
       "skin_clusters_identical_to_r01": A_new["clusters"] == A_old["clusters"],
       "triangles": A_new["tris"], "max_influences": A_new["max_infl"], "geometry_objects": A_new["n_geo"]}
ok_raw = raw["names_exact_and_ordered"] and raw["global_same_as_r01"] and A_new["gs"].get("CustomFrameRate") == 30.0
ok_raw = ok_raw and A_new["gs"].get("TimeMode") == 6 and raw["triangles"] <= 25000 and raw["max_influences"] <= 4
ok_raw = ok_raw and raw["geometry_identical_to_r01"] and raw["skin_clusters_identical_to_r01"]
for t in ALL:
    sp = A_new["stacks"][t]["span"]
    want = EXP["takes"][t]["frames"]
    raw["spans"][t] = {k: round(v, 4) for k, v in sp.items()}
    good = abs(sp["LocalStart"] - want[0]) < 1e-3 and abs(sp["LocalStop"] - want[1]) < 1e-3
    raw["spans"][t]["matches_export"] = good
    kt = [k for (kt_, kv) in A_new["stacks"][t]["curves"].values() for k in (kt_[0], kt_[-1])]
    raw["spans"][t]["key_range_frames"] = [round(min(kt) / KT * 30, 4), round(max(kt) / KT * 30, 4)]
    raw["spans"][t]["channels"] = len(A_new["stacks"][t]["curves"])
    raw["spans"][t]["bones_keyed"] = len({k[0] for k in A_new["stacks"][t]["curves"]})
    ok_raw = ok_raw and good
for t in R01:
    a, b = A_old["stacks"][t], A_new["stacks"][t]
    same_keys = set(a["curves"]) == set(b["curves"])
    diff = [k for k in a["curves"] if b["curves"].get(k) != a["curves"][k]]
    nkeys = sum(len(v[0]) for v in b["curves"].values())
    raw["r01_curves"][t] = {"channels": len(b["curves"]), "keys": nkeys, "same_channels": same_keys,
                            "channels_differing": len(diff), "span_same": a["span"] == b["span"],
                            "identical": same_keys and not diff and a["span"] == b["span"]}
    ok_raw = ok_raw and raw["r01_curves"][t]["identical"]
raw["pass"] = ok_raw
rep["A_raw_fbx"] = raw
print("RAW", json.dumps(raw, default=str)[:3000], flush=True)
del A_old


# ============================ Part B: Blender import ============================
def empty():
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials,
                 bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for blk in list(coll):
            coll.remove(blk)


def load(path):
    empty()
    bpy.ops.import_scene.fbx(filepath=str(path), anim_offset=0.0)
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    guide = next((o for o in scene.objects if o.name.startswith("FacingGuide")), None)
    return arm, mesh, guide


def act_for(prefix_ob, take):
    return next((a for a in bpy.data.actions if a.name == prefix_ob + "|" + take), None)


def use(ob, act):
    ob.animation_data_create()
    ob.animation_data.action = act
    if act is not None and len(act.slots):
        ob.animation_data.action_slot = act.slots[0]


def fcurves(act):
    out = []
    for layer in act.layers:
        for strip in layer.strips:
            for slot in act.slots:
                cb = strip.channelbag(slot)
                if cb:
                    out += list(cb.fcurves)
    return out


def goto(f):
    scene.frame_set(int(math.floor(f)), subframe=float(f - math.floor(f)))


def skin(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    mw = np.array(mesh.matrix_world)
    return co @ mw[:3, :3].T + mw[:3, 3]


def sample(arm, mesh, guide, take, n):
    use(arm, act_for(arm.name, take))
    if guide is not None:
        use(guide, act_for(guide.name, take))
    ts = np.arange(0, n + 1e-9, STEP)
    B = {pb.name: np.empty((len(ts), 4, 4)) for pb in arm.pose.bones}
    S = np.empty((len(ts), len(mesh.data.vertices), 3))
    G = np.empty((len(ts), 3))
    O = np.empty((len(ts), 4, 4))
    for i, f in enumerate(ts):
        goto(f)
        mw = arm.matrix_world
        for pb in arm.pose.bones:
            B[pb.name][i] = np.array(mw @ pb.matrix)
        S[i] = skin(mesh)
        G[i] = np.array(guide.matrix_world.translation) if guide else 0
        O[i] = np.array(mw)
    return {"t": ts, "B": B, "S": S, "G": G, "O": O}


# ---- r01 FBX samples (reference) ----
arm, mesh, guide = load(OLD)
old = {t: sample(arm, mesh, guide, t, OLD_EXP["takes"][t]["frames"][1]) for t in R01}

# ---- r02 FBX ----
arm, mesh, guide = load(NEW)
imported = sorted(a.name for a in bpy.data.actions)
B = {"imported_actions": imported, "fps_after_import": scene.render.fps / scene.render.fps_base,
     "armature_object": {"loc": [round(x, 6) for x in arm.location], "rot": [round(x, 6) for x in arm.rotation_euler],
                         "scale": [round(x, 6) for x in arm.scale]}}
names = [pb.name for pb in arm.pose.bones]
arm.data.pose_position = "REST"
goto(0)
rest_S = skin(mesh)
rest_B = {pb.name: np.array(arm.matrix_world @ pb.matrix) for pb in arm.pose.bones}
arm.data.pose_position = "POSE"
vg = [g.name for g in mesh.vertex_groups]
dom, wmax, cnt = [], [], []
for v in mesh.data.vertices:
    ws = [(g.weight, vg[g.group]) for g in v.groups if g.weight > 1e-6]
    cnt.append(len(ws))
    w, nme = max(ws) if ws else (0.0, None)
    dom.append(nme)
    wmax.append(w)
dom, wmax, cnt = np.array(dom, dtype=object), np.array(wmax), np.array(cnt)
E = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
mesh.data.edges.foreach_get("vertices", E)
E = E.reshape(-1, 2)
rest_len = np.linalg.norm(rest_S[E[:, 0]] - rest_S[E[:, 1]], axis=1)
solid = wmax >= 0.999
rig_e = E[solid[E[:, 0]] & solid[E[:, 1]] & (dom[E[:, 0]] == dom[E[:, 1]])]
rig_len = np.linalg.norm(rest_S[rig_e[:, 0]] - rest_S[rig_e[:, 1]], axis=1)
rigid_bones = sorted({str(dom[i]) for i in rig_e[:, 0]})
B["mesh"] = {"verts": len(mesh.data.vertices), "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
             "max_influences": int(cnt.max()), "unweighted": int((cnt == 0).sum()),
             "rest_skin_min_z_mm": round(float(rest_S[:, 2].min()) * 1000, 3),
             "rigid_edges": int(len(rig_e)), "rigid_bones": rigid_bones}
feet = ct.foot_sets(rest_S, dom)

tk = {}
for t in ALL:
    tk[t] = sample(arm, mesh, guide, t, EXP["takes"][t]["frames"][1])

# ---------- 1. takes present, ranges, fps ----------
c1 = {"actions": {}}
ok1 = B["fps_after_import"] == 30.0 and rep["A_raw_fbx"]["names_exact_and_ordered"]
for t in ALL:
    a = act_for(arm.name, t)
    fr = [round(x, 4) for x in a.frame_range] if a else None
    good = a is not None and fr == [float(x) for x in EXP["takes"][t]["frames"]]
    c1["actions"][t] = {"range": fr, "export": EXP["takes"][t]["frames"], "loop_in_export": EXP["takes"][t]["loop"],
                        "ok": good}
    ok1 = ok1 and good
c1["fbx_fps"] = rep["A_raw_fbx"]["global"]["CustomFrameRate"]
c1["pass"] = ok1
B["1_takes_ranges_fps"] = c1

# ---------- 2. in place ----------
c2 = {}
ok2 = True
for t in ALL:
    s = tk[t]
    a = act_for(arm.name, t)
    obj_paths = sorted({fc.data_path for fc in fcurves(a) if not fc.data_path.startswith("pose.bones")})
    O = s["O"]
    r = {"root": cm.root_motion(s), "scale": cm.scale_dev(s),
         "armature_object_travel_mm": round(float(np.abs(O - O[0]).max()) * 1000, 4),
         "armature_object_channels": obj_paths,
         "facing_guide_travel_mm": round(float(np.abs(s["G"] - s["G"][0]).max()) * 1000, 4),
         "facing_guide": [round(float(x), 4) for x in s["G"][0]]}
    r["ok"] = (r["root"]["root_travel_xy_mm"] < 0.01 and r["root"]["root_travel_z_mm"] < 0.01
               and r["root"]["root_rot_deg"] < 0.01 and r["armature_object_travel_mm"] < 0.01
               and r["scale"]["max_bone_scale_dev"] < 1e-4 and r["facing_guide_travel_mm"] < 0.01)
    c2[t] = r
    ok2 = ok2 and r["ok"]
c2["origin_on_ground_rest_min_z_mm"] = B["mesh"]["rest_skin_min_z_mm"]
g0 = tk[ALL[0]]["G"][0]
c2["facing_guide_blender"] = [round(float(x), 4) for x in g0]
c2["faces_minus_y"] = bool(g0[1] < -0.9 and abs(g0[0]) < 1e-3)
c2["pass"] = ok2 and c2["faces_minus_y"] and abs(c2["origin_on_ground_rest_min_z_mm"]) < 1.0
B["2_in_place"] = c2

# ---------- 3. old takes unchanged ----------
c3 = {"vs_r01_fbx_samples": {}, "vs_stored_r01_numbers": {}}
ok3 = rep["A_raw_fbx"]["pass"]
pt = VER["checks"]["5_deformation"]["per_take"]
for t in R01:
    a, b = old[t], tk[t]
    bd = max(float(np.abs(a["B"][n] - b["B"][n]).max()) for n in a["B"])
    sd = float(np.abs(a["S"] - b["S"]).max()) if a["S"].shape == b["S"].shape else float("nan")
    c3["vs_r01_fbx_samples"][t] = {"samples": len(b["t"]), "max_bone_diff": bd, "max_skin_diff_m": sd,
                                   "identical": bd == 0.0 and sd == 0.0}
    ok3 = ok3 and bd == 0.0 and sd == 0.0
    st = cm.elongation(b, E, rest_len, dom)
    gr = cm.ground(b)
    now = {"edge_grow_max_mm": st["grow_max_mm"], "at": st["grow_max_at"], "pair": st["grow_max_pair"],
           "edges_grow_over_5cm": st["edges_grow_over_5cm"], "skin_min_z_mm": gr["skin_min_z_mm"],
           "verts_below_minus_1cm": gr["verts_below_minus_1cm"],
           "rigid_edges_max_change_mm": cm.rigid_dev(b, rig_e, rig_len)}
    stored = pt[t]
    cmpk = ("edge_grow_max_mm", "at", "pair", "edges_grow_over_5cm", "skin_min_z_mm", "verts_below_minus_1cm")
    match = {k: (now[k] == stored[k]) or (isinstance(now[k], float) and abs(now[k] - stored[k]) <= 0.051) for k in cmpk}
    match["rigid_edges_max_change_mm"] = abs(now["rigid_edges_max_change_mm"] - stored["rigid_edges_max_change_mm"]) < 0.001
    extra = {}
    if EXP["takes"][t]["loop"]:
        sm, vs = cm.seam(b), cm.velocity_seam(b)
        extra = {"loop_seam": sm, "loop_velocity": vs,
                 "stored_seam": VER["checks"]["1_takes"]["loop_seams"][t],
                 "stored_velocity": VER["checks"]["1_takes"]["loop_velocity"][t]}
        match["loop_seam"] = sm == extra["stored_seam"]
        match["loop_velocity"] = vs == extra["stored_velocity"]
    rb = cm.root_motion(b)
    match["root"] = rb == VER["checks"]["2_in_place"]["root_bone"][t]
    if t.endswith("_Bite"):
        head = np.where(dom == "head")[0]
        i18 = int(np.argmin(np.abs(b["t"] - 18)))
        P = b["S"][i18, head[int(b["S"][i18, head, 1].argmin())]]
        tip = {"forward": round(float(-P[1]), 3), "up": round(float(P[2]), 3), "right": round(float(-P[0]), 3)}
        extra["beak_tip_f18"] = tip
        match["beak_tip_f18"] = tip == VER["checks"]["4_contacts"]["bite"]["beak_tip_at_contact_m"]
    if t.endswith("_Pop"):
        pp = ct.pop(b, feet, EXP["takes"][t])["pop"]
        extra["airborne"] = pp["airborne_frames"]
        match["airborne"] = pp["airborne_frames"] == VER["checks"]["4_contacts"]["pop"]["airborne"]
    c3["vs_stored_r01_numbers"][t] = {"now": now, "stored": {k: stored[k] for k in cmpk + ("rigid_edges_max_change_mm",)},
                                      **extra, "match": match, "all_match": all(match.values())}
    ok3 = ok3 and all(match.values())
c3["pass"] = ok3
B["3_old_takes_unchanged"] = c3
del old


# ---------- helpers for new takes ----------
def seam_pose(a, ia, b, ib):
    pos = max(float(np.linalg.norm(a["B"][n][ia, :3, 3] - b["B"][n][ib, :3, 3])) for n in names)
    rot = max(cm.rot_deg(a["B"][n][ia, :3, :3], b["B"][n][ib, :3, :3]) for n in names)
    sk = float(np.linalg.norm(a["S"][ia] - b["S"][ib], axis=1).max())
    return {"bone_pos_mm": round(pos * 1000, 4), "bone_rot_deg": round(rot, 4), "skin_mm": round(sk * 1000, 4)}


def vel(s, i, side):
    """Skin velocity (m/s) over one 0.25-frame step at sample i: side=+1 leaving, -1 entering."""
    h = STEP / 30.0
    return (s["S"][i + 1] - s["S"][i]) / h if side > 0 else (s["S"][i] - s["S"][i - 1]) / h


def idx(s, f):
    return int(np.argmin(np.abs(s["t"] - f)))


def per_int(s, arr, nd=1):
    return {int(f): round(float(arr[idx(s, f)]), nd) for f in range(int(s["t"][-1]) + 1)}


C, L, U, D = [t for t in NEWT]
idle = tk["ForestSplitter_Idle"]
ball_S = tk[L]["S"][0]
legs_v = np.array([str(d).startswith("leg_") for d in dom])
head_v = dom == "head"
shell_v = np.array([str(d).startswith("shell_") for d in dom])

# ---------- 4. loops + hand-offs ----------
c4 = {"RollLoop_seam": seam_pose(tk[L], 0, tk[L], -1)}
v_in, v_out = vel(tk[L], len(tk[L]["t"]) - 1, -1), vel(tk[L], 0, +1)
c4["RollLoop_velocity_seam_mps"] = round(float(np.linalg.norm(v_in - v_out, axis=1).max()), 4)
Vi = (tk[L]["S"][1:] - tk[L]["S"][:-1]) / (STEP / 30)
c4["RollLoop_velocity_worst_inside_mps"] = round(max(float(np.linalg.norm(Vi[k + 1] - Vi[k], axis=1).max())
                                                    for k in range(len(Vi) - 1)), 4)
hand = {"Idle f0 -> RollCurl f0": (idle, 0, tk[C], 0), "RollCurl f30 -> RollLoop f0": (tk[C], -1, tk[L], 0),
        "RollLoop f0 -> RollUncurl f0": (tk[L], 0, tk[U], 0), "RollLoop f0 -> RollDizzy f0": (tk[L], 0, tk[D], 0),
        "RollUncurl f30 -> Idle f0": (tk[U], -1, idle, 0), "RollDizzy f45 -> Idle f0": (tk[D], -1, idle, 0)}
c4["handoffs"] = {k: seam_pose(*v) for k, v in hand.items()}
# speed of the skin just before / after each hand-off (m/s, max vertex) - how hard the cut is
c4["handoff_speeds_mps"] = {
    "RollCurl end (entering launch)": round(float(np.linalg.norm(vel(tk[C], len(tk[C]["t"]) - 1, -1), axis=1).max()), 3),
    "RollLoop start (leaving)": round(float(np.linalg.norm(vel(tk[L], 0, +1), axis=1).max()), 3),
    "RollUncurl end (entering Idle)": round(float(np.linalg.norm(vel(tk[U], len(tk[U]["t"]) - 1, -1), axis=1).max()), 3),
    "RollDizzy end (entering Idle)": round(float(np.linalg.norm(vel(tk[D], len(tk[D]["t"]) - 1, -1), axis=1).max()), 3),
    "Idle start (leaving)": round(float(np.linalg.norm(vel(idle, 0, +1), axis=1).max()), 3)}
c4["pass"] = (c4["RollLoop_seam"]["skin_mm"] < 0.1 and c4["RollLoop_seam"]["bone_rot_deg"] < 0.01
              and all(v["skin_mm"] < 0.1 for v in c4["handoffs"].values()))
B["4_loops_handoffs"] = c4

# ---------- 5. contact / contract frames ----------
c5 = {}
s = tk[C]
dev_ball = np.linalg.norm(s["S"] - ball_S, axis=2)
body_z = s["B"]["body"][:, 2, 3]
ball_body_z = tk[L]["B"]["body"][0, 2, 3]
Rref = tk[L]["B"]["body"][0, :3, :3]
yaw = []
for M in s["B"]["body"][:, :3, :3]:
    Dm = M @ Rref.T                      # world-space rotation from the ball body orientation
    yaw.append(math.degrees(math.atan2(Dm[1, 0], Dm[0, 0])))
yaw = np.array(yaw)
minz = s["S"][:, :, 2].min(axis=1)
c5["RollCurl"] = {
    "legs_dev_from_ball_mm": per_int(s, dev_ball[:, legs_v].max(axis=1) * 1000),
    "head_dev_from_ball_mm": per_int(s, dev_ball[:, head_v].max(axis=1) * 1000),
    "all_dev_from_ball_mm": per_int(s, dev_ball.max(axis=1) * 1000),
    "body_above_ball_mm": per_int(s, (body_z - ball_body_z) * 1000),
    "first_frame_legs_tucked_(<15mm)": next((f for f in range(31) if dev_ball[idx(s, f), legs_v].max() < 0.015), None),
    "first_frame_head_tucked_(<15mm)": next((f for f in range(31) if dev_ball[idx(s, f), head_v].max() < 0.015), None),
    "body_yaw_deg_24_30": [round(float(yaw[i]), 2) for i in range(idx(s, 24), len(s["t"]), 2)],
    "body_yaw_abs_max_24_30": round(float(np.abs(yaw[idx(s, 24):]).max()), 2),
    "body_yaw_abs_max_0_24": round(float(np.abs(yaw[:idx(s, 24)]).max()), 3),
    "skin_min_z_mm": per_int(s, minz * 1000),
    "rim_lift_24_30_above_ball_mm": round(float((minz[idx(s, 24):].max() - ball_S[:, 2].min()) * 1000), 2),
    "f30_equals_ball_mm": round(float(dev_ball[-1].max() * 1000), 4)}
r = c5["RollCurl"]
r["note"] = ("tucked = within 15 mm of the ball pose (the 10-14 squash bounce keeps the body up to 9 mm above it); "
             "rim lift during the shake is measured on the lowest skin point above the ball's own 2.4 mm")
r["ok"] = (r["first_frame_legs_tucked_(<15mm)"] is not None and r["first_frame_legs_tucked_(<15mm)"] <= 12
           and r["first_frame_head_tucked_(<15mm)"] is not None and r["first_frame_head_tucked_(<15mm)"] <= 12
           and 3.0 <= r["body_yaw_abs_max_24_30"] <= 5.5 and r["body_yaw_abs_max_0_24"] < 1.0
           and r["rim_lift_24_30_above_ball_mm"] <= 8.0 and r["f30_equals_ball_mm"] < 0.1)

s = tk[L]
minz = s["S"][:, :, 2].min(axis=1)
c5["RollLoop"] = {"skin_min_z_mm": per_int(s, minz * 1000, 2),
                  "rim_dip_below_ground_mm": round(float(-minz.min() * 1000), 2),
                  "ball_pose_min_z_mm": round(float(ball_S[:, 2].min() * 1000), 2),
                  "max_dev_from_ball_mm": round(float(np.linalg.norm(s["S"] - ball_S, axis=2).max() * 1000), 2)}
r = c5["RollLoop"]
r["ok"] = r["rim_dip_below_ground_mm"] <= 5.0 and r["max_dev_from_ball_mm"] < 60

for t, lab in ((U, "RollUncurl"), (D, "RollDizzy")):
    s = tk[t]
    sol = ct.soles(s, feet)
    rr = {"f0_equals_ball_mm": round(float(np.linalg.norm(s["S"][0] - ball_S, axis=1).max() * 1000), 4),
          "end_equals_idle0_mm": round(float(np.linalg.norm(s["S"][-1] - idle["S"][0], axis=1).max() * 1000), 4),
          "body_z_mm": per_int(s, (s["B"]["body"][:, 2, 3] - idle["B"]["body"][0, 2, 3]) * 1000),
          "body_back_mm": per_int(s, (s["B"]["body"][:, 1, 3] - idle["B"]["body"][0, 1, 3]) * 1000),
          "head_forward_m": per_int(s, -s["B"]["head"][:, 1, 3], 3),
          "sole_z_mm": {leg: per_int(s, zl * 1000) for leg, (P, zl) in sol.items()},
          "skin_min_z_mm": per_int(s, s["S"][:, :, 2].min(axis=1) * 1000),
          "feet": ct.grounded_slide(s, sol)}
    # first integer frame from which each paw stays on the ground (sole within 5 mm of its own minimum... and of 0)
    land = {}
    isol = ct.soles(idle, feet)
    for leg, (P, zl) in sol.items():
        g = zl <= isol[leg][1][0] + 0.002          # at its own Idle f0 height (rest paws float 0-9 mm, r01 model)
        f_on = None
        for f in range(int(s["t"][-1]) + 1):
            if all(g[idx(s, x)] for x in np.arange(f, min(f + 3, s["t"][-1]) + 1e-9, 1.0)):
                f_on = f
                break
        land[leg] = f_on
    rr["paw_first_grounded_frame"] = land
    # slide while grounded (XY drift of the sole centroid inside each ground contact, z <= 6 mm)
    sl, skid = {}, {}
    for leg, (P, zl) in sol.items():
        on = zl <= isol[leg][1][0] + 0.002
        near = zl <= isol[leg][1][0] + 0.008
        worst, cur = 0.0, []
        for i in list(range(len(zl))) + [None]:
            if i is not None and on[i]:
                cur.append(i)
                continue
            if len(cur) > 1:
                worst = max(worst, float(np.linalg.norm(P[cur, :2] - P[cur[0], :2], axis=1).max()))
            cur = []
        sl[leg] = round(worst * 1000, 2)
        # skid: XY path length of the sole while it is within 8 mm of its standing height (landing scrape)
        path = 0.0
        for i in range(1, len(zl)):
            if near[i] and near[i - 1]:
                path += float(np.linalg.norm(P[i, :2] - P[i - 1, :2]))
        skid[leg] = round(path * 1000, 1)
    rr["slide_while_planted_mm"] = sl
    rr["skid_path_near_ground_mm"] = skid
    c5[lab] = rr
r = c5["RollUncurl"]
s = tk[U]
mz = s["S"][:, :, 2].min(axis=1)
bz = s["B"]["body"][:, 2, 3]
rising = np.gradient(bz) > 1e-4
r["support_gap_while_body_rises_mm"] = round(float(np.where(rising, mz, 0).max() * 1000), 1)
r["paws_planted_by_frame"] = max((v if v is not None else 99) for v in r["paw_first_grounded_frame"].values())
r["ok"] = (r["f0_equals_ball_mm"] < 0.1 and r["end_equals_idle0_mm"] < 0.1 and r["paws_planted_by_frame"] <= 20
           and r["support_gap_while_body_rises_mm"] <= 10.0 and max(r["skid_path_near_ground_mm"].values()) <= 30.0
           and max(r["slide_while_planted_mm"].values()) <= 13.0)
r = c5["RollDizzy"]
s = tk[D]
by = s["B"]["body"][:, 1, 3] - s["B"]["body"][0, 1, 3]
bz = s["B"]["body"][:, 2, 3] - s["B"]["body"][0, 2, 3]
r["bump"] = {"body_back_peak_frame": float(s["t"][int(by[:idx(s, 6)].argmax())]),
             "body_back_peak_mm": round(float(by[:idx(s, 6)].max() * 1000), 1),
             "body_up_peak_frame": float(s["t"][int(bz[:idx(s, 6)].argmax())]),
             "body_up_peak_mm": round(float(bz[:idx(s, 6)].max() * 1000), 1)}
# dizzy stance 13-36: all four paws down?
st = {leg: [f for f in range(13, 37) if zl[idx(s, f)] > 0.006] for leg, (P, zl) in ct.soles(s, feet).items()}
r["paws_off_ground_13_36_(>6mm, rest paws float 6-9 mm)"] = st
mz = s["S"][:, :, 2].min(axis=1)
rising = (np.gradient(s["B"]["body"][:, 2, 3]) > 1e-4) & (s["t"] >= 5)      # after the wall bounce (0-4)
r["support_gap_while_body_rises_mm"] = round(float(np.where(rising, mz, 0).max() * 1000), 1)
r["ok"] = (r["f0_equals_ball_mm"] < 0.1 and r["end_equals_idle0_mm"] < 0.1
           and 1.5 <= r["bump"]["body_back_peak_frame"] <= 2.5 and 1.5 <= r["bump"]["body_up_peak_frame"] <= 2.5
           and r["support_gap_while_body_rises_mm"] <= 10.0 and max(r["skid_path_near_ground_mm"].values()) <= 30.0)
c5["pass"] = all(c5[k]["ok"] for k in ("RollCurl", "RollLoop", "RollUncurl", "RollDizzy"))
B["5_contract_frames"] = c5

# ---------- 6. rigid parts, deformation, ground ----------
c6 = {}
ok6 = True
for t in NEWT:
    s = tk[t]
    st = cm.elongation(s, E, rest_len, dom)
    gr = cm.ground(s)
    minz = s["S"][:, :, 2].min(axis=1)
    r = {"rigid_edges_max_change_mm": cm.rigid_dev(s, rig_e, rig_len),
         "edge_grow_max_mm": st["grow_max_mm"], "at": st["grow_max_at"], "pair": st["grow_max_pair"],
         "edges_grow_over_5cm": st["edges_grow_over_5cm"], "pairs_over_2cm": st["pairs_over_2cm"],
         "ground": gr, "frames_floating_over_10mm": [float(x) for x in s["t"][minz > 0.010]],
         "finite": bool(np.isfinite(s["S"]).all())}
    r["ok"] = (r["rigid_edges_max_change_mm"] < 0.01 and r["finite"] and gr["skin_min_z_mm"] > -10
               and gr["verts_below_minus_1cm"] == 0)
    c6[t] = r
    ok6 = ok6 and r["ok"]
c6["pass"] = ok6
B["6_rigid_ground_deform"] = c6

# ---------- 7. ball / spin data in export.json ----------
S = tk[L]["S"]
mn, mx = ball_S.min(axis=0), ball_S.max(axis=0)
piv = (mn + mx) / 2
rad = float(np.sqrt((S[:, :, 1] - piv[1]) ** 2 + (S[:, :, 2] - piv[2]) ** 2).max())
exp_roll = EXP["takes"][L]["roll"]
unity = [round(float(-piv[0]), 3), round(float(piv[2]), 3), round(float(-piv[1]), 3)]
c7 = {"pivot_unity_local": unity, "export_pivot": exp_roll["spin_pivot_unity_local"],
      "spin_radius_m": round(rad, 3), "export_radius": exp_roll["spin_radius_m"],
      "lift_m": round(rad - float(piv[2]), 3), "export_lift": exp_roll["lift_while_spinning_m"],
      "deg_per_tick_for_0.40m": round(math.degrees(0.40 / rad), 2), "export_deg_per_tick": exp_roll["angle_per_tick_deg"],
      "ball_size_m": [round(float(x), 3) for x in (mx - mn)],
      "export_size": [exp_roll["ball_size_m"][k] for k in ("width_x", "length_y", "height_z")]}
c7["pass"] = (np.allclose(c7["pivot_unity_local"], c7["export_pivot"], atol=0.002)
              and abs(c7["spin_radius_m"] - c7["export_radius"]) <= 0.002
              and abs(c7["lift_m"] - c7["export_lift"]) <= 0.002
              and abs(c7["deg_per_tick_for_0.40m"] - c7["export_deg_per_tick"]) <= 0.2)
B["7_ball_spin_data"] = c7

# ---------- 8. budget ----------
c8 = {"triangles_fbx": rep["A_raw_fbx"]["triangles"], "triangles_imported": B["mesh"]["triangles"],
      "max_influences_fbx": rep["A_raw_fbx"]["max_influences"], "max_influences_imported": B["mesh"]["max_influences"],
      "unweighted": B["mesh"]["unweighted"]}
c8["pass"] = (c8["triangles_fbx"] <= 25000 and c8["triangles_imported"] <= 25000 and c8["max_influences_fbx"] <= 4
              and c8["max_influences_imported"] <= 4 and c8["unweighted"] == 0)
B["8_budget"] = c8

rep["B_import"] = B
rep["all_pass"] = all(v["pass"] for k, v in B.items() if isinstance(v, dict) and "pass" in v) and rep["A_raw_fbx"]["pass"]
OUT_JSON.write_text(json.dumps(rep, indent=1, default=float), encoding="utf-8")
for k, v in B.items():
    if isinstance(v, dict) and "pass" in v:
        print("CHECK", k, v["pass"], flush=True)
print("INDEP_ALL", rep["all_pass"], flush=True)
