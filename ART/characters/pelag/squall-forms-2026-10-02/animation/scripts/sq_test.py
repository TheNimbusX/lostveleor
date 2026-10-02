import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sq_rig import Rig, fl
import sq_arm, sq_pose
from sq_keys import FORE, BACK, F0
from b_common import blade
from mathutils import Vector
rig = Rig(); sq_arm.prepare(rig)
for nm, key in (("F6", FORE[6]), ("B6", BACK[6]), ("F0", F0), ("F3", FORE[3])):
    sq_pose.apply(rig, dict(key, fixed=False))
    S = rig.P("RightArm"); H = fl(*key["hands"]["Right"])
    out = rig.P("RightArm") - rig.P("Spine2"); out.z = 0
    pole = (Vector((0, 0, -1)) + out.normalized() * 0.7).normalized()
    line = []
    for el in (-30, 0, 30):
        for yw in range(-150, 181, 30):
            D = fl(math.cos(math.radians(yw)) * math.cos(math.radians(el)), math.sin(math.radians(yw)) * math.cos(math.radians(el)), math.sin(math.radians(el)))
            sq_arm.LAST.clear()
            r = sq_arm.solve(rig, "Right", H, pole, D)
            line.append("%d/%d:%d(%d)" % (yw, el, r["miss"], round(rig.forearm_twist("Right"))))
    print(nm, " ".join(line))
