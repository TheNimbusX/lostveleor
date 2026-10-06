"""Крушение v2: замер стойки серии сабли на v6 (кости, кисти, кулак) — для раскладки ключей.
blender -b --factory-startup -P wk_probe.py"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wk_rig import Rig, M, SIDES
from mathutils import Vector

rig = Rig()
B = rig.B
r3 = lambda v: [round(c, 3) for c in v]
root = lambda v: r3((-v.y, v.x, v.z))   # (f, l, u)
print("PROBE pyaw %.1f cyaw %.1f lean %.1f relP %.2f faceYaw %.2f" % (B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"], B["head"]["faceYaw"]))
for n in ("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "HeadTop_End", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm",
          "LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot",
          "RightFoot", "LeftToeBase", "RightToeBase", "LeftToe_End", "RightToe_End", "LeftHandMiddle1", "RightHandMiddle1"):
    try:
        print("PROBE bone %-16s fl u %s  rest %s" % (n, root(rig.P(n)), root(rig.rest[M(n)].translation)))
    except Exception as e:
        print("PROBE miss", n, e)
print("PROBE arm_len", {s: round(B["arm_len"][s], 3) for s in SIDES})
print("PROBE foot_yaw", B["foot_yaw"], "pitch", B["pitch"])
print("PROBE toe_floor", rig.toe_floor, "export_scale", rig.export_scale, "obj rot", tuple(rig.dst.rotation_euler))
print("PROBE fore twist", B["twist_fore"])
L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
print("PROBE body len %.3f hips offset ratio stance %.3f" % (L, (rig.P("Hips") - rig.rest[M("Hips")].translation).length / L))
for s in SIDES:
    hm = rig.dst.matrix_world @ rig.dst.pose.bones[M(s + "Hand")].matrix
    print("PROBE hand %s axes x %s y %s z %s" % (s, r3(hm.to_3x3().col[0].normalized()), r3(hm.to_3x3().col[1].normalized()),
                                               r3(hm.to_3x3().col[2].normalized())))
    for f in ("Index1", "Middle1", "Ring1", "Pinky1", "Index2", "Pinky2", "Thumb1", "Thumb2"):
        print("PROBE  %s %-7s %s" % (s, f, root(rig.P(s + "Hand" + f))))
print("PROBE bones", len(rig.dst.pose.bones), [b.name for b in rig.dst.pose.bones][:70])
