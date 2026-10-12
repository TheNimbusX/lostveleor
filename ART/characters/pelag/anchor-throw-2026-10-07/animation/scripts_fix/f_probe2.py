import bpy, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import t_lib as T
from wk_rig import Rig, SIDES, M
from s_lib import sagittal_lean
import f_unitypose as U
rig = Rig()
data = U.load(r"C:\Users\d.grab\Desktop\the-game\artifacts\throw-0710\animfix\measure_before.json")
bpy.ops.wm.open_mainfile  # noqa
# сверка: Throw0 из Unity против кадра 0 работы (Pelag_AnchorThrow2_Work.blend не открываем — B из author.log)
def meas(tag):
    P = rig.P
    print(tag, "hips", tuple(round(x, 3) for x in P("Hips")), "Lfoot", tuple(round(x, 3) for x in P("LeftFoot")), "Rfoot", tuple(round(x, 3) for x in P("RightFoot")),
          "Lhand", tuple(round(x, 3) for x in P("LeftHand")), "Rhand", tuple(round(x, 3) for x in P("RightHand")),
          "py %.1f cy %.1f lean %.1f" % (rig.pelvis_yaw(), rig.chest_yaw(), sagittal_lean(rig.dst)), rig.head_m())
for name in ("Throw0", "KnifeIdle", "MXIdle", "Catch8"):
    r = data["ref"][name]
    rig.reset(); U.apply(rig, data, r["wq"], r["p"], ground_z=0.004); meas(name)
