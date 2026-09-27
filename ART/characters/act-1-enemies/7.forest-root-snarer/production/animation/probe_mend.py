"""Probe set for ForestRootSnarer_Mend key frames (probe_pose.py module). PROBE_FRAMES=3,5,8 overrides."""
import json
import os
from pathlib import Path
import clip_mend

FRAMES = [3, 5, 7, 8, 10, 20, 28, 30, 38, 42, 46]


def poses(rig):
    clip_mend.prepare(rig)
    fr = [float(x) for x in os.environ.get("PROBE_FRAMES", ",".join(map(str, FRAMES))).split(",")]
    out = {f"f{int(f):02d}": clip_mend.mend_pose(f) for f in fr}
    (Path(__file__).resolve().parent / "work" / "probe_mend_info.json").write_text(json.dumps(clip_mend.INFO, indent=1))
    return out
