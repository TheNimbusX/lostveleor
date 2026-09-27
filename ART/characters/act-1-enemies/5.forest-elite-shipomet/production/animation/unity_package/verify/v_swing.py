"""Swing (bend) vs twist of the wrists per frame on the re-imported FBX -> out/swing.json"""
import math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402
from mathutils import Vector

arm, mesh = vc.load()
acts = vc.arm_actions(arm)
R = {}
for take, spec in vc.EXPORT["takes"].items():
    vc.use(arm, acts[take]); f0, f1 = spec["frames"]
    rows = {}
    for f in range(f0, f1 + 1):
        vc.goto(f)
        row = []
        for j in ("LeftHand", "RightHand"):
            pb = arm.pose.bones[j]; par = pb.parent
            rel = par.matrix.to_3x3().inverted() @ pb.matrix.to_3x3()
            rest = par.bone.matrix_local.to_3x3().inverted() @ pb.bone.matrix_local.to_3x3()
            d = (rest.inverted() @ rel).to_quaternion()
            y = d @ Vector((0, 1, 0))
            swing = math.degrees(math.acos(max(-1, min(1, y.y))))
            # twist = rotation about the bone's own Y after removing the swing
            sw = Vector((0, 1, 0)).rotation_difference(y)
            tw = sw.inverted() @ d
            twist = math.degrees(2 * math.atan2(tw.y, tw.w))
            twist = (twist + 180) % 360 - 180
            row += [round(swing, 1), round(twist, 1)]
        rows[f] = row
    R[take.split("_")[1]] = rows
    mx = [max(r[i] if i % 2 == 0 else abs(r[i]) for r in rows.values()) for i in range(4)]
    print(take, "L swing/twist max", mx[0], mx[1], "R swing/twist max", mx[2], mx[3], flush=True)
vc.dump("swing.json", R)
