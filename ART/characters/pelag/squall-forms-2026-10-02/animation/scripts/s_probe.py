"""Замер стойки серии сабли на v6 (как в d_author.py): геометрия для таблиц Шквала. Только чтение.
blender -b --factory-startup -P s_probe.py
"""
import bpy, sys, os, math
from mathutils import Vector, Quaternion, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import wpos, blade
from s_lib import import_rig, transfer_stance, ground_stance, sagittal_lean, foot_pitch

CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"
SRC = CH + r"\Pelag_v5\Mixamo\Pelag_MX_SaberCombo.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
dst, mesh = import_rig(V6)
if dst.animation_data: dst.animation_data.action = None
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
dst.scale = (1, 1, 1)
for pb in dst.pose.bones:
    pb.rotation_mode = 'QUATERNION'; pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
bpy.context.view_layer.update()
rest = {n: (dst.matrix_world @ dst.data.bones[n].matrix_local) for n in dst.data.bones.keys()}
dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
zs = [(mesh.matrix_world @ v.co).z for v in me.vertices]; print("mesh height rest", max(zs) - min(zs), min(zs)); ev.to_mesh_clear()
src, sm = import_rig(SRC)
bpy.context.scene.frame_set(2); bpy.context.view_layer.update()
K = transfer_stance(src, dst, rest)
for o in (src, sm): bpy.data.objects.remove(o, do_unlink=True)
ground_stance(dst, rest)
P = lambda n: wpos(dst, "mixamorig:" + n)
def yaw(v): return math.degrees(math.atan2(v.x, -v.y))  # 0 = -Y (вперёд), + к левому боку героя (+X)
for n in ("Hips", "Spine2", "Neck", "Head", "LeftUpLeg", "RightUpLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase",
          "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand"):
    print("%-14s" % n, tuple(round(c, 3) for c in P(n)))
up = Vector((0, 0, 1))
pel = (P("LeftUpLeg") - P("RightUpLeg")).cross(up); ch = (P("LeftArm") - P("RightArm")).cross(up)
print("pelvis fwd yaw", round(yaw(pel), 1), "chest fwd yaw", round(yaw(ch), 1), "lean", round(sagittal_lean(dst), 1))
r, t = blade(dst); d = t - r
print("blade root", tuple(round(c, 3) for c in r), "tip", tuple(round(c, 3) for c in t), "len", round(d.length, 3), "dir", tuple(round(c, 3) for c in d.normalized()), "yaw", round(yaw(d), 1))
for s in ("Left", "Right"):
    ua = (P(s + "ForeArm") - P(s + "Arm")).length; fa = (P(s + "Hand") - P(s + "ForeArm")).length
    th = (P(s + "Leg") - P(s + "UpLeg")).length; sh = (P(s + "Foot") - P(s + "Leg")).length
    print(s, "upper arm", round(ua, 3), "forearm", round(fa, 3), "thigh", round(th, 3), "shin", round(sh, 3), "foot pitch", round(foot_pitch(dst, s)[0], 1))
    for b in ("Arm", "ForeArm", "Hand"):
        q = dst.pose.bones["mixamorig:" + s + b].matrix_basis.to_quaternion()
        tw = 2 * math.degrees(math.atan2(q.y, q.w))
        print("   basis twist", b, round((tw + 180) % 360 - 180, 1), "total angle", round(math.degrees(q.angle), 1))
print("K", K)
