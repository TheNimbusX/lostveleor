"""Абордаж v2: замер стойки серии сабли на v6 для таблицы ключей (только чтение).
blender -b --factory-startup -P ab_probe.py"""
import bpy, sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M, SIDES, yaw_of
import ab_arm

rig = Rig()
ab_arm.prepare(rig)
B = rig.B
r = lambda v: [round(c, 3) for c in v]
froot = lambda v: [round(-v.y, 3), round(v.x, 3), round(v.z, 3)]   # (вперёд, влево, вверх)
out = {}
for n in ("Hips", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm",
          "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase"):
    out[n] = froot(rig.P(n))
out["B"] = dict(lean=round(B["lean"], 2), pyaw=round(B["pyaw"], 2), cyaw=round(B["cyaw"], 2), head=B["head"],
                foot_yaw={s: round(v, 2) for s, v in B["foot_yaw"].items()}, pitch={s: round(v, 2) for s, v in B["pitch"].items()},
                hand_relL=r(B["hand_relL"]), arm_len={s: round(v, 3) for s, v in B["arm_len"].items()},
                twist_fore={s: round(v, 2) for s, v in B["twist_fore"].items()}, toe_floor=round(rig.toe_floor, 4))
out["ARM"] = {s: dict(L1=round(a["L1"], 3), L2=round(a["L2"], 3), pole0=froot(a["pole0"]), ax0=froot(a["ax0"])) for s, a in rig.ARM.items()}
rest_h = rig.rest[M("Hips")].translation
out["rest_hips"] = froot(rest_h)
out["rest_head"] = froot(rig.rest[M("Head")].translation)
for s in SIDES:
    for b in ("Hand", "HandIndex1", "HandIndex2", "HandMiddle1", "HandMiddle2", "HandThumb1", "HandThumb2"):
        pb = rig.dst.pose.bones.get(M(s + b))
        if pb is None: continue
        q = pb.matrix_basis.to_quaternion()
        out["basis_" + s + b] = [round(math.degrees(q.angle), 1)] + r(q.axis)
print("PROBE", json.dumps(out, ensure_ascii=False))
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "_stance_probe.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(out, indent=1, ensure_ascii=False)); f.truncate(); f.close()
