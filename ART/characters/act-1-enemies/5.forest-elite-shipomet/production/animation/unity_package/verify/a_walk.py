"""Check 3 (system python): planted-foot speed and slide in Walk from the re-imported FBX samples -> out/walk.json.

Blender -Y is forward, so a planted foot must move +Y at the walk speed (the sim moves the entity).
"""
import json
from pathlib import Path

import numpy as np

O = Path(__file__).resolve().parent / "out"
EXPORT = json.loads((O.parents[1] / "export.json").read_text(encoding="utf-8"))
spec = EXPORT["takes"]["ForestThorncaster_Walk"]
V = spec["speed_mps"]
co = np.load(O / "co_ForestThorncaster_Walk.npy").astype(np.float64)   # frames x verts x 3
rest = np.load(O / "rest.npy")
W = np.load(O / "weights.npy")
G = json.loads((O / "groups.json").read_text())["groups"]
n = co.shape[0] - 1                 # 24 (frame n == frame 0)
step = V / 30.0
res = {"target_speed_mps": V, "frames": n, "feet": {}}
for side in ("Left", "Right"):
    foot = W[:, G.index(side + "Foot")] + W[:, G.index(side + "ToeBase")]
    sole = np.nonzero((foot > 0.5) & (rest[:, 2] < 0.03))[0]
    # the sole vertices that are on the ground in each frame
    low = co[:n, sole, 2]
    planted = low.min(1) < 0.01
    # contact set per frame: sole vertices within 1 cm of the ground
    frames = [f for f in range(n) if planted[f]]
    # stance = longest cyclic run of planted frames
    runs, cur = [], []
    for k in range(2 * n):
        f = k % n
        if planted[f]:
            cur.append(f)
        else:
            if cur:
                runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)
    run = max(runs, key=len)[:n]
    # track the vertices that stay on the ground through the whole stance
    stay = sole[np.all(co[run][:, sole, 2] < 0.012, axis=0)]
    if len(stay) == 0:
        stay = sole[np.argsort(co[run][:, sole, 2].max(0))[:5]]
    p = co[run][:, stay, :]                              # stance frames x verts x 3
    t = np.arange(len(run))
    dy = np.diff(p[:, :, 1], axis=0)                     # per-frame +Y motion
    speed = float(dy.mean() * 30)
    ideal = p - np.stack([np.zeros_like(t), t * step, np.zeros_like(t)], 1)[:, None, :].astype(float)
    slide_xy = np.linalg.norm(ideal[:, :, :2] - ideal[:1, :, :2], axis=2)   # drift from ideal scroll, per vertex
    per_frame = np.linalg.norm(np.diff(ideal[:, :, :2], axis=0), axis=2)
    res["feet"][side] = {
        "stance_frames": [int(run[0]), int(run[-1])], "stance_len": len(run), "planted_frames_total": len(frames),
        "tracked_ground_vertices": int(len(stay)),
        "measured_planted_speed_mps": round(speed, 3),
        "speed_error_pct": round((speed - V) / V * 100, 2),
        "slide_over_stance_max_m": round(float(slide_xy.max()), 4),
        "slide_per_frame_max_m": round(float(per_frame.max()), 4),
        "sideways_x_drift_max_m": round(float(np.abs(p[:, :, 0] - p[:1, :, 0]).max()), 4),
        "ground_z_range_m": [round(float(p[:, :, 2].min()), 4), round(float(p[:, :, 2].max()), 4)],
    }
res["cycle_m_measured"] = round(float(np.mean([res["feet"][s]["measured_planted_speed_mps"] for s in res["feet"]]) * n / 30), 3)
res["pass"] = all(abs(f["speed_error_pct"]) < 5 and f["slide_over_stance_max_m"] < 0.02 for f in res["feet"].values())
(O / "walk.json").write_text(json.dumps(res, indent=1))
print(json.dumps(res, indent=1))
