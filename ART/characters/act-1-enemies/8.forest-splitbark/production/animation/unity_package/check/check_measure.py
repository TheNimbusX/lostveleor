"""Step-3 independent check of ForestSplitter.fbx: re-import into an empty scene and measure every take.

blender -b --factory-startup -P check_measure.py  -> check/measure.json
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import check_common as cc  # noqa: E402
import check_metrics as cm  # noqa: E402
import check_takes as ct  # noqa: E402

EXP = json.loads(cc.EXPORT.read_text(encoding="utf-8"))
ORDER = EXP["fbx_take_names"]
STEP = 0.25


def fbx_header():
    """AnimationStack names + LocalStop and the file frame rate, straight from the FBX."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(cc.FBX))
    out = {"stacks": {}, "custom_fps": None, "time_mode": None}
    for el in root.elems:
        if el.id == b"GlobalSettings":
            for p in (q for e in el.elems if e.id == b"Properties70" for q in e.elems):
                if p.props[0] == b"CustomFrameRate":
                    out["custom_fps"] = p.props[-1]
                if p.props[0] == b"TimeMode":
                    out["time_mode"] = p.props[-1]
        if el.id == b"Objects":
            for ob in (o for o in el.elems if o.id == b"AnimationStack"):
                name = ob.props[1].split(b"\x00")[0].decode()
                stop = next((q.props[-1] for e in ob.elems if e.id == b"Properties70" for q in e.elems
                             if q.props[0] == b"LocalStop"), 0)
                out["stacks"][name] = round(stop / 46186158000 * 30, 4)
    return out


HEADER = fbx_header()
arm, mesh = cc.import_fbx()
guide = next(o for o in cc.scene.objects if o.name.startswith("FacingGuide"))


def fcurves(act):
    try:
        return list(act.fcurves)
    except AttributeError:
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for slot in act.slots:
                    cb = strip.channelbag(slot)
                    if cb:
                        out += list(cb.fcurves)
        return out


def sample(n):
    ts = np.arange(0, n + 1e-9, STEP)
    B = {pb.name: np.empty((len(ts), 4, 4)) for pb in arm.pose.bones}
    S = np.empty((len(ts), len(mesh.data.vertices), 3), dtype=np.float32)
    for i, f in enumerate(ts):
        cc.goto(f)
        for name, M in cc.bones(arm).items():
            B[name][i] = np.array(M)
        S[i] = cc.skin(mesh)
    return {"t": ts, "B": B, "S": S}


# ---- rest reference ----
arm.data.pose_position = "REST"
cc.goto(0)
rest = {"B": {k: np.array(v) for k, v in cc.bones(arm).items()}, "S": cc.skin(mesh)}
arm.data.pose_position = "POSE"
dom, counts, wmax = cc.dominant_bone(mesh)
E = cc.edges(mesh)
rest_len = np.linalg.norm(rest["S"][E[:, 0]] - rest["S"][E[:, 1]], axis=1)
wsum = np.array([sum(g.weight for g in v.groups) for v in mesh.data.vertices])
solid = wmax >= 0.999
rig_e = E[solid[E[:, 0]] & solid[E[:, 1]] & (dom[E[:, 0]] == dom[E[:, 1]])]
rig_len = np.linalg.norm(rest["S"][rig_e[:, 0]] - rest["S"][rig_e[:, 1]], axis=1)
report = {
    "fbx": dict(HEADER, scene_fps_after_import=cc.scene.render.fps / cc.scene.render.fps_base,
                bytes=cc.FBX.stat().st_size),
    "mesh": {"verts": len(mesh.data.vertices), "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
             "max_influences": int(counts.max()), "verts_over_4": int((counts > 4).sum()),
             "unweighted_verts": int((counts == 0).sum()), "weight_sum_dev_max": round(float(np.abs(wsum - 1).max()), 5),
             "rest_skin_min_z_mm": round(float(rest["S"][:, 2].min()) * 1000, 2),
             "rest_bbox_m": [[round(float(x), 3) for x in rest["S"].min(axis=0)],
                             [round(float(x), 3) for x in rest["S"].max(axis=0)]]},
    "armature_object": {"location": list(arm.location), "rotation": list(arm.rotation_euler), "scale": list(arm.scale)},
    "takes": {},
}
feet = ct.foot_sets(rest["S"], dom)
report["mesh"]["sole_rest_z_mm"] = {k: round(float(rest["S"][v["sole"], 2].mean()) * 1000, 2) for k, v in feet.items()}
report["mesh"]["dominant_bone_counts"] = {str(k): int((dom == k).sum()) for k in sorted(set(dom), key=str)}

for take in ORDER:
    spec = EXP["takes"][take]
    n = spec["frames"][1]
    act = cc.take_action(take)
    r = {"action": act.name if act else None}
    if act is None:
        report["takes"][take] = r
        continue
    cc.use(arm, act)
    gact = next((a for a in bpy.data.actions if a.name == "FacingGuide|" + take), None)
    cc.use(guide, gact)
    fcs = fcurves(act)
    keys = [k.co[0] for fc in fcs for k in fc.keyframe_points]
    r["frame_range"] = [round(x, 3) for x in act.frame_range]
    r["key_range"] = [round(min(keys), 3), round(max(keys), 3)]
    r["non_bone_fcurves"] = sorted({fc.data_path for fc in fcs if not fc.data_path.startswith("pose.bones")})
    obj = [fc for fc in fcs if not fc.data_path.startswith("pose.bones")]
    r["armature_object_channels"] = {
        "%s[%d]" % (fc.data_path, fc.array_index): round(max(k.co[1] for k in fc.keyframe_points)
                                                         - min(k.co[1] for k in fc.keyframe_points), 7)
        for fc in obj}
    r["armature_object_static"] = max(r["armature_object_channels"].values(), default=0.0) < 1e-6
    sc = [k.co[1] for fc in fcs if fc.data_path.endswith(".scale") for k in fc.keyframe_points]
    r["scale_keys"] = {"count": len(sc), "min": round(min(sc), 6) if sc else None, "max": round(max(sc), 6) if sc else None}
    spread = [max(k.co[1] for k in fc.keyframe_points) - min(k.co[1] for k in fc.keyframe_points)
              for fc in (fcurves(gact) if gact else []) if len(fc.keyframe_points)]
    r["facing_guide_static"] = (max(spread) if spread else 0.0) < 1e-6
    tk = sample(n)
    r["root"] = cm.root_motion(tk)
    r["scale"] = cm.scale_dev(tk)
    r["body"] = cm.body_drift(tk)
    r["ground"] = cm.ground(tk)
    r["stretch"] = cm.edge_stretch(tk, E, rest_len, dom)
    r["rigid_edges"] = {"count": int(len(rig_e)), "max_length_change_mm": cm.rigid_dev(tk, rig_e, rig_len)}
    r["finite"] = bool(np.isfinite(tk["S"]).all())
    if spec["loop"]:
        r["loop_seam"] = cm.seam(tk)
        r["loop_velocity_jump_mps"] = cm.velocity_seam(tk)
    r.update(ct.specific(take, tk, rest, feet, dom, spec))
    report["takes"][take] = r
    print("TAKE", take, json.dumps(r, default=float), flush=True)

out = Path(__file__).resolve().parent / "measure.json"
out.write_text(json.dumps(report, indent=2, default=float), encoding="utf-8")
print("MEASURE_DONE", out, flush=True)
