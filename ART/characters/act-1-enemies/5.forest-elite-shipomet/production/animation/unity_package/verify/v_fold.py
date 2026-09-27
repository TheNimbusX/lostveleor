"""Geometric wrist fold: angle between forearm (elbow->wrist) and spike (wrist->tip vertex) per frame -> out/fold.json"""
import math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402

arm, mesh = vc.load()
acts = vc.arm_actions(arm)
tip = {s: vc.spike_tip(mesh, arm, s + "Hand")[0] for s in ("Left", "Right")}
def ang(a, b):
    return math.degrees(math.acos(max(-1, min(1, float(np.dot(a, b) / np.linalg.norm(a) / np.linalg.norm(b))))))
R = {}
for take, spec in [("Rest", None)] + list(vc.EXPORT["takes"].items()):
    rows = {}
    frames = [0] if spec is None else range(spec["frames"][0], spec["frames"][1] + 1)
    if spec is None:
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        arm.animation_data_create(); arm.animation_data.action = None
    else:
        vc.use(arm, acts[take])
    for f in frames:
        vc.goto(f)
        co = vc.mesh_co(mesh)
        row = []
        for s in ("Left", "Right"):
            el = np.array(arm.matrix_world @ arm.pose.bones[s + "ForeArm"].head)
            wr = np.array(arm.matrix_world @ arm.pose.bones[s + "Hand"].head)
            row.append(round(ang(wr - el, co[tip[s]] - wr), 1))
        rows[f] = row
    R[take.split("_")[-1]] = rows
    print(take, "fold max L/R", max(r[0] for r in rows.values()), max(r[1] for r in rows.values()), flush=True)
vc.dump("fold.json", R)
