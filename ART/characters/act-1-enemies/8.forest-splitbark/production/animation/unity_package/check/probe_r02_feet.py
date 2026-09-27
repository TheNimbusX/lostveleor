"""Per-frame paw contact probe of the r02 FBX roll takes (sole centroid XY / lowest z, per paw).

blender -b --factory-startup -P probe_r02_feet.py -> prints FEET lines
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np  # noqa: E402

import check_common as cc  # noqa: E402
import check_takes as ct  # noqa: E402

arm, mesh = cc.import_fbx()
arm.data.pose_position = "REST"
cc.goto(0)
rest = cc.skin(mesh)
arm.data.pose_position = "POSE"
dom, _, _ = cc.dominant_bone(mesh)
feet = ct.foot_sets(rest, dom)
out = {}
for take, frames in (("ForestSplitter_RollUncurl", np.arange(8, 30.01, 0.5)),
                     ("ForestSplitter_RollDizzy", np.arange(4, 45.01, 0.5)),
                     ("ForestSplitter_RollCurl", np.arange(0, 12.01, 0.5))):
    cc.use(arm, cc.take_action(take))
    rows = {}
    for f in frames:
        cc.goto(float(f))
        S = cc.skin(mesh)
        r = {}
        for leg, fs in feet.items():
            P = S[fs["sole"]].mean(axis=0)
            z = S[fs["all"], 2].min()
            r[leg] = [round(float(P[0]) * 1000, 1), round(float(P[1]) * 1000, 1), round(float(z) * 1000, 1)]
        r["min_z"] = round(float(S[:, 2].min()) * 1000, 1)
        r["body_z"] = round(float((arm.matrix_world @ arm.pose.bones["body"].matrix).translation.z) * 1000, 1)
        rows[float(f)] = r
    out[take] = rows
    for f, r in rows.items():
        print("FEET", take[15:], f, json.dumps(r), flush=True)
(Path(__file__).resolve().parent / "probe_r02_feet.json").write_text(json.dumps(out), encoding="utf-8")
