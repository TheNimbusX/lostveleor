import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import v4_lib as L
from wk_rig import Rig
import wk_grip
rig = Rig(); wk_grip.prepare(rig); rig.reset()
P = lambda n: tuple(round(c, 3) for c in rig.P(n))
print("STANCE hips", P("Hips"), "head", P("Head"), "Rhand", P("RightHand"), "Lfoot", P("LeftFoot"), "mw", [round(x,3) for x in rig.dst.matrix_world.to_euler()], rig.dst.scale[:])
src = L.SabreSource(rig.dst)
for c, a in src.src.items(): print("SRC", c, [round(x,3) for x in a.matrix_world.to_euler()], a.scale[:], a.animation_data.action.frame_range[:])
print("BIND", [round(x,3) for x in src.bind.matrix_world.to_euler()], src.bind.scale[:], "hip_scale", src.hip_scale, "bindHips", src.bpos["mixamorig:Hips"], "tHips", src.tpos["mixamorig:Hips"])
src.apply("Pelag_AN_Sabre1", 0)
print("S1t0 hips", P("Hips"), "head", P("Head"), "Rhand", P("RightHand"), "Lfoot", P("LeftFoot"))
sp = L._pose(src.src["Pelag_AN_Sabre1"], "mixamorig:Hips")[0]; print("src hips world", sp, "src head", L._pose(src.src["Pelag_AN_Sabre1"], "mixamorig:Head")[0])
