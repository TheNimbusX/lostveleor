"""Бросок якоря: замер стойки серии сабли на v6 (оси корня f/l/u) — исходные числа для ключей at_keys.py.
blender -b --factory-startup -P at_probe.py"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from at_rig import Rig, M
import at_arm, at_fingers

rig = Rig()
at_arm.prepare(rig)
at_fingers.prepare(rig)
B = rig.B
f = lambda v: "(%.3f, %.3f, %.3f)" % (-v.y, v.x, v.z)
print("PROBE pyaw %.1f cyaw %.1f lean %.1f relP %.1f faceYaw %.1f" % (B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"], B["head"]["faceYaw"]))
for n in ("Hips", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm",
          "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase"):
    print("PROBE %-14s %s" % (n, f(rig.P(n))))
print("PROBE hand_rootL", [round(c, 3) for c in B["hand_rootL"]], "hand_rootR", [round(c, 3) for c in B["hand_rootR"]])
print("PROBE foot_yaw", {k: round(v, 1) for k, v in B["foot_yaw"].items()}, "pitch", {k: round(v, 1) for k, v in B["pitch"].items()})
print("PROBE arm_len", {k: round(v, 3) for k, v in B["arm_len"].items()}, "toe_floor %.4f" % rig.toe_floor)
for s in ("Left", "Right"):
    a = rig.ARM[s]
    print("PROBE ARM %s L1 %.3f L2 %.3f" % (s, a["L1"], a["L2"]))
print("PROBE height head z %.3f" % rig.P("HeadTop_End").z if M("HeadTop_End") in rig.dst.pose.bones else "")
