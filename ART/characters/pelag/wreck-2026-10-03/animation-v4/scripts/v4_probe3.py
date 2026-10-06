import bpy, sys
sys.path.insert(0, r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\wreck-2026-10-03\animation-v4\scripts")
import v4_lib as L
W = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.open_mainfile(filepath=W + "/Pelag_Wreck4_Work.blend")
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
P = lambda n: arm.matrix_world @ arm.pose.bones["mixamorig:" + n].head
for c in ("Swing1", "Swing2", "Lunge"):
    arm.animation_data.action = bpy.data.actions["Pelag_AN_Wreck4_" + c]
    for f in range(0, L.TIMING[c]["frames"] + 1):
        bpy.context.scene.frame_set(f); r = L.lunge_root(c, f)
        q = lambda n: "(%.2f %.2f %.3f)" % (P(n).x, P(n).y - r, P(n).z)
        print("%s F%2d L %s %s R %s %s" % (c, f, q("LeftToeBase"), q("LeftFoot"), q("RightToeBase"), q("RightFoot")))
