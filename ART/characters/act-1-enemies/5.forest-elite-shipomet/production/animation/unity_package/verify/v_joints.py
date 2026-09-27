"""Joint bend angles (relative to the bind pose) per take on the re-imported FBX -> out/joints.json.

blender -b -P v_joints.py
"""
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402

arm, mesh = vc.load()
acts = vc.arm_actions(arm)
JOINTS = ["LeftHand", "RightHand", "LeftForeArm", "RightForeArm", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot"]
R = {}
for take, spec in vc.EXPORT["takes"].items():
    vc.use(arm, acts[take])
    f0, f1 = spec["frames"]
    r = {j: (0.0, 0) for j in JOINTS}
    for f in range(f0, f1 + 1):
        vc.goto(f)
        for j in JOINTS:
            pb = arm.pose.bones[j]
            par = pb.parent
            rel = (par.matrix.to_3x3().inverted() @ pb.matrix.to_3x3())
            rest = (par.bone.matrix_local.to_3x3().inverted() @ pb.bone.matrix_local.to_3x3())
            d = rest.inverted() @ rel
            ang = math.degrees(d.to_quaternion().angle)
            if ang > r[j][0]:
                r[j] = (round(ang, 1), f)
    R[take.split("_")[1]] = r
    print(take, r, flush=True)
vc.dump("joints.json", R)
