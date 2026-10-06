"""Замер: сабельная поза на v6 по тикам — правый кулак, клинок, досягаемость двух рук, голова к груди."""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import v4_lib as L
from wk_rig import Rig
import wk_grip
from b_common import blade
rig = Rig(); print("FIST", wk_grip.prepare(rig))
rig.reset(); base_relP = rig.head_m()["relP"]
src = L.SabreSource(rig.dst)
root = lambda v: "(%.2f %.2f %.2f)" % (-v.y, v.x, v.z)
for name, T in L.TIMING.items():
    for t in range(T["frames"] + 1):
        src.apply(T["sabre"], L.warp(name, t))
        rf = wk_grip.fist_center(rig, "Right"); r, tip = blade(rig.dst); d = (tip - r).normalized()
        dl = (rf - rig.P("LeftArm")).length / rig.ARM["Left"]["L1"] / 1 
        armL = rig.ARM["Left"]["L1"] + rig.ARM["Left"]["L2"]
        print("%s t%2d s%5.2f Rfist %s blade %s Ldist %.2f relP %+.1f cy %.0f py %.0f" % (
            name, t, L.warp(name, t), root(rf), root(d), (rf - rig.P("LeftArm")).length / armL,
            rig.head_m()["relP"] - base_relP, rig.chest_yaw(), rig.pelvis_yaw()))
