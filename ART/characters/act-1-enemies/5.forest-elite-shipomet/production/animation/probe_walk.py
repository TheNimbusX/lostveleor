"""Print leg extension (hip-ankle / leg length) per Walk frame: blender -b -P probe_walk.py"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import anim_apply as aa  # noqa: E402

arm, mesh, sk = aa.open_rig()
take = aa.takes(sk)["Walk"]
for f in [x * 0.5 for x in range(0, 49)]:
    pose, info = aa.solve(sk, take, f)
    row = []
    for s in ("Left", "Right"):
        a, b = sk.length[s + "UpLeg"], sk.length[s + "Leg"]
        d = (pose.head(s + "Foot") - pose.head(s + "UpLeg")).length
        row.append(round(d / (a + b), 4))
    print("EXT", f, row, round(pose.head("Hips").z, 3))
