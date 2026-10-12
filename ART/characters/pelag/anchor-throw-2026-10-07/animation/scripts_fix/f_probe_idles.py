"""animfix: стойки Unity (KnifeIdle — слой Saber Stance, MX_Idle — CombatIdle_v5) на риге броска Blender — перенос
transfer_stance как сабли; замер для сверки с Unity (measure_before.json ref)."""
import bpy, sys, os, json, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import t_lib as T
from mathutils import Vector
from wk_rig import Rig, SIDES, M, fl
from s_lib import import_rig, transfer_stance, sagittal_lean
rig = Rig()
MX = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v5\Mixamo"
def meas(tag):
    bpy.context.view_layer.update()
    P = lambda n: rig.P(n)
    lf, rf = P("LeftFoot"), P("RightFoot")
    gz = min(P(s + "ToeBase").z for s in SIDES)
    print(tag, "hips", tuple(round(x, 3) for x in P("Hips")), "Lfoot", tuple(round(x, 3) for x in lf), "Rfoot", tuple(round(x, 3) for x in rf),
          "Lhand", tuple(round(x, 3) for x in P("LeftHand")), "Rhand", tuple(round(x, 3) for x in P("RightHand")),
          "head", tuple(round(x, 3) for x in P("Head")), "py %.1f cy %.1f lean %.1f" % (rig.pelvis_yaw(), rig.chest_yaw(), sagittal_lean(rig.dst)),
          "hm", rig.head_m(), "groundZ %.3f" % gz)
rig.reset(); meas("STANCE")
for name in ("Pelag_MX_KnifeIdle", "Pelag_MX_Idle"):
    src, sm = import_rig(os.path.join(MX, name + ".fbx"))
    bpy.context.scene.frame_set(int(src.animation_data.action.frame_range[0]) if src.animation_data and src.animation_data.action else 0)
    rig.reset()
    transfer_stance(src, rig.dst, rig.rest)
    meas(name)
    for o in (src, sm):
        if o: bpy.data.objects.remove(o, do_unlink=True)
