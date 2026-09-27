"""r02 (27.09) independent check of ForestSplitter.fbx: the r01 takes must move exactly as in the approved r01 FBX,
the new roll takes must follow the package conventions.

blender -b --factory-startup -P check_r02.py  -> check/r02.json
Both FBX files are imported into an emptied scene and sampled every 0.25 frame (bone world matrices + skin).
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import check_common as cc  # noqa: E402
import check_metrics as cm  # noqa: E402

HERE = Path(__file__).resolve().parent
NEW = cc.PKG / "ForestSplitter.fbx"
OLD = cc.PKG / "backup_r01_2026-09-26" / "ForestSplitter.fbx"
EXP = json.loads(cc.EXPORT.read_text(encoding="utf-8"))
OLD_EXP = json.loads((OLD.parent / "export.json").read_text(encoding="utf-8"))
R01 = OLD_EXP["fbx_take_names"]
ALL = EXP["fbx_take_names"]
NEWT = [t for t in ALL if t not in R01]
STEP = 0.25


def header(path):
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(path))
    out = {"stacks": {}, "custom_fps": None}
    for el in root.elems:
        if el.id == b"GlobalSettings":
            for p in (q for e in el.elems if e.id == b"Properties70" for q in e.elems):
                if p.props[0] == b"CustomFrameRate":
                    out["custom_fps"] = p.props[-1]
        if el.id == b"Objects":
            for ob in (o for o in el.elems if o.id == b"AnimationStack"):
                name = ob.props[1].split(b"\x00")[0].decode()
                stop = next((q.props[-1] for e in ob.elems if e.id == b"Properties70" for q in e.elems
                             if q.props[0] == b"LocalStop"), 0)
                out["stacks"][name] = round(stop / 46186158000 * 30, 4)
    return out


def load(path):
    cc.empty_scene()
    bpy.ops.import_scene.fbx(filepath=str(path), anim_offset=0.0)
    arm = next(o for o in cc.scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in cc.scene.objects if o.type == "MESH")
    return arm, mesh


def sample(arm, mesh, take, n):
    cc.use(arm, cc.take_action(take))
    ts = np.arange(0, n + 1e-9, STEP)
    B = {pb.name: np.empty((len(ts), 4, 4)) for pb in arm.pose.bones}
    S = np.empty((len(ts), len(mesh.data.vertices), 3))
    for i, f in enumerate(ts):
        cc.goto(f)
        for name, M in cc.bones(arm).items():
            B[name][i] = np.array(M)
        S[i] = cc.skin(mesh)
    return {"t": ts, "B": B, "S": S}


H_OLD, H_NEW = header(OLD), header(NEW)
arm, mesh = load(OLD)
old = {t: sample(arm, mesh, t, OLD_EXP["takes"][t]["frames"][1]) for t in R01}
arm, mesh = load(NEW)
report = {"fbx": NEW.name, "fbx_bytes": NEW.stat().st_size, "old_fbx": str(OLD.relative_to(cc.PKG)),
          "stacks": H_NEW["stacks"], "custom_fps": H_NEW["custom_fps"],
          "stack_names_exact": list(H_NEW["stacks"]) == ALL,
          "old_stacks_same_length": all(H_NEW["stacks"][t] == H_OLD["stacks"][t] for t in R01),
          "r01_vs_old_fbx": {}, "new_takes": {}, "handoffs": {}}
ok = report["stack_names_exact"] and report["old_stacks_same_length"] and H_NEW["custom_fps"] == 30.0
for t in R01:
    a, b = old[t], sample(arm, mesh, t, EXP["takes"][t]["frames"][1])
    bone = max(float(np.abs(a["B"][n] - b["B"][n]).max()) for n in a["B"])
    skin = float(np.abs(a["S"] - b["S"]).max())
    same = a["S"].shape == b["S"].shape and bone == 0.0 and skin == 0.0
    report["r01_vs_old_fbx"][t] = {"samples": len(a["t"]), "max_bone_matrix_diff": bone, "max_skin_diff_m": skin,
                                   "identical": same}
    ok = ok and same
    print("R01", t, report["r01_vs_old_fbx"][t], flush=True)

# ---- new takes ----
arm.data.pose_position = "REST"
cc.goto(0)
rest_S = cc.skin(mesh)
arm.data.pose_position = "POSE"
dom, counts, wmax = cc.dominant_bone(mesh)
E = cc.edges(mesh)
rest_len = np.linalg.norm(rest_S[E[:, 0]] - rest_S[E[:, 1]], axis=1)
solid = wmax >= 0.999
rig_e = E[solid[E[:, 0]] & solid[E[:, 1]] & (dom[E[:, 0]] == dom[E[:, 1]])]
rig_len = np.linalg.norm(rest_S[rig_e[:, 0]] - rest_S[rig_e[:, 1]], axis=1)
report["budget"] = {"triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
                    "max_influences": int(counts.max()), "unweighted": int((counts == 0).sum())}
ok = ok and report["budget"]["triangles"] <= 25000 and report["budget"]["max_influences"] <= 4
idle = sample(arm, mesh, "ForestSplitter_Idle", 60)
tk = {}
for t in NEWT:
    n = EXP["takes"][t]["frames"][1]
    s = sample(arm, mesh, t, n)
    tk[t] = s
    r = {"frames": n, "root": cm.root_motion(s), "scale": cm.scale_dev(s), "ground": cm.ground(s),
         "stretch": {k: v for k, v in cm.edge_stretch(s, E, rest_len, dom).items()
                     if k in ("grow_max_mm", "grow_max_at", "grow_max_pair", "edges_grow_over_5cm")},
         "rigid_edges_max_change_mm": cm.rigid_dev(s, rig_e, rig_len), "finite": bool(np.isfinite(s["S"]).all())}
    if EXP["takes"][t]["loop"]:
        r["loop_seam"] = cm.seam(s)
    r["pass"] = (r["root"]["root_travel_xy_mm"] < 0.01 and r["root"]["root_travel_z_mm"] < 0.01
                 and r["scale"]["max_bone_scale_dev"] < 1e-4 and r["ground"]["skin_min_z_mm"] > -12
                 and r["finite"] and r["rigid_edges_max_change_mm"] < 0.1
                 and (not EXP["takes"][t]["loop"] or r["loop_seam"]["skin_mm"] < 0.1))
    report["new_takes"][t] = r
    ok = ok and r["pass"]
    print("NEW", t, json.dumps(r, default=float), flush=True)


def seam(a, ia, b, ib):
    pos = max(float(np.linalg.norm(a["B"][n][ia, :3, 3] - b["B"][n][ib, :3, 3])) for n in a["B"])
    skin = float(np.linalg.norm(a["S"][ia] - b["S"][ib], axis=1).max())
    return {"bone_pos_mm": round(pos * 1000, 3), "skin_mm": round(skin * 1000, 3)}


C, L, U, D = NEWT
for name, (a, ia, b, ib) in {"Idle f0 -> RollCurl f0": (idle, 0, tk[C], 0),
                             "RollCurl end -> RollLoop f0": (tk[C], -1, tk[L], 0),
                             "RollLoop f0 -> RollUncurl f0": (tk[L], 0, tk[U], 0),
                             "RollLoop f0 -> RollDizzy f0": (tk[L], 0, tk[D], 0),
                             "RollUncurl end -> Idle f0": (tk[U], -1, idle, 0),
                             "RollDizzy end -> Idle f0": (tk[D], -1, idle, 0)}.items():
    report["handoffs"][name] = seam(a, ia, b, ib)
    ok = ok and report["handoffs"][name]["skin_mm"] < 0.5
report["all_pass"] = ok
(HERE / "r02.json").write_text(json.dumps(report, indent=2, default=float), encoding="utf-8")
print("CHECK_R02", ok, flush=True)
