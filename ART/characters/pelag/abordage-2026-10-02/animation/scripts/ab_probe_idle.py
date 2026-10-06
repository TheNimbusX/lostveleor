"""Абордаж v2: где стоят стопы в Idle (Pelag_MX_Idle, кадр 0) на v6 против стойки серии сабли (конец рывка/Шквала).
Unity играет Pelag_MX_Idle как Generic: локальные повороты костей тейка на костях v6, таз — значение клипа.
blender -b --factory-startup -P ab_probe_idle.py
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab_rig import Rig, M, SIDES, yaw_of
from s_lib import import_rig, hierarchy

CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
IDLE = CH + r"\Pelag_v5\Mixamo\Pelag_MX_Idle.fbx"
rig = Rig()
d = rig.dst
out = {}


def feet():
    r = {}
    hp = rig.P("Hips")
    for s in SIDES:
        a = rig.P(s + "Foot"); t = rig.P(s + "ToeBase")
        r[s] = dict(ankle=[round(c, 3) for c in a], toe=[round(c, 3) for c in t], toe_z=round(rig.toe_z(s), 4),
                    yaw=round(yaw_of(t - a), 1))
    r["hips"] = [round(c, 3) for c in hp]
    r["pelvis_yaw"] = round(rig.pelvis_yaw(), 1); r["chest_yaw"] = round(rig.chest_yaw(), 1)
    for s in SIDES:
        r[s + "Hand"] = [round(c, 3) for c in rig.P(s + "Hand")]
    return r


out["stance_S"] = feet()
src, sm = import_rig(IDLE)
sc = bpy.context.scene
fr = src.animation_data.action.frame_range if src.animation_data and src.animation_data.action else (0, 0)
print("idle frames", fr, "src scale", tuple(src.scale), "dst export scale", tuple(rig.export_scale))
rest_src_h = (src.data.bones[M("Head")].head_local - src.data.bones[M("Hips")].head_local).length
rest_dst_h = (d.data.bones[M("Head")].head_local - d.data.bones[M("Hips")].head_local).length
print("rest hips->head src %.4f dst %.4f" % (rest_src_h, rest_dst_h))
order = hierarchy(d)
samples = []
for f in (int(fr[0]), int((fr[0] + fr[1]) / 2), int(fr[1])):
    sc.frame_set(f); bpy.context.view_layer.update()
    for n in order:
        if n not in src.pose.bones: continue
        pb = d.pose.bones[n]; sb = src.pose.bones[n]
        if pb.parent is None:
            R = sb.matrix.to_3x3().normalized()
            # таз: значение клипа в единицах тейка, приведённое к росту v6 (Unity берёт его как есть)
            pos = sb.matrix.translation * (rest_dst_h / rest_src_h)
            pb.matrix = Matrix.Translation(pos) @ R.to_4x4()
        else:
            Lr = (sb.parent.matrix.inverted() @ sb.matrix).to_3x3().normalized()
            off = (pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local).translation
            pb.matrix = pb.parent.matrix @ Matrix.Translation(off) @ Lr.to_4x4()
        bpy.context.view_layer.update()
    r = feet(); r["frame"] = f
    # то же без таза клипа: таз v6 в покое по XY (как если бы Unity держал таз)
    samples.append(r)
    print("idle f%d" % f, json.dumps(r))
out["idle"] = samples
print("S", json.dumps(out["stance_S"]))
for s in SIDES:
    a = Vector(out["stance_S"][s]["ankle"]); b = Vector(samples[0][s]["ankle"])
    print("%s ankle S->Idle f0: dxy %.3f m (dx %.3f dy %.3f) dz %.3f" % (s, (a - b).xy.length, b.x - a.x, b.y - a.y, b.z - a.z))
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "_idle_probe.json")
open(p, "a").close()
fh = open(p, "r+", encoding="utf-8", newline=""); fh.seek(0); fh.write(json.dumps(out, indent=1)); fh.truncate(); fh.close()
