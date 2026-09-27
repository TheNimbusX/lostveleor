"""Quick tuning probe: evaluate every take at integer frames on the rig (nothing saved).

blender -b ../rig/ForestSplitter_Rig.blend -P probe_takes.py -- [take ...]
Writes probe.json and prints a compact summary per take.
"""
import json
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import measure  # noqa: E402
import splitter_pose as sp  # noqa: E402
from takes import TAKES  # noqa: E402

only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
arm = bpy.data.objects[sp.ARM]
mesh = bpy.data.objects[sp.MESH]
report = {}
for name, spec in TAKES.items():
    if only and not any(o in name for o in only):
        continue
    rows = []
    for f in range(spec["frames"] + 1):
        pose = spec["fn"](float(f))
        pulled = sp.apply(arm, pose)
        rows.append(measure.frame_metrics(arm, mesh, pose, pulled))
    report[name] = rows
    pulled = [(f, r["pulled"]) for f, r in enumerate(rows) if r["pulled"]]
    maxreach = max(max(r["reach"].values()) for r in rows)
    ikerr = max(max(r["ik_err"].values()) for r in rows)
    minz = min((r["skin_min_z"], f, r["skin_min_at"]) for f, r in enumerate(rows))
    print("PROBE %s pulled=%s maxreach=%.3f ik_err=%.4f skin_min_z=%.4f@%d %s" % (name, pulled[:6], maxreach, ikerr, *minz))
    if "Bite" in name:
        print("  beak y/z:", [(f, r["beak"][1], r["beak"][2]) for f, r in enumerate(rows) if f % 2 == 0 or f == 18])
(HERE / "probe.json").write_text(json.dumps(report), encoding="utf-8")
print("PROBE_DONE")
