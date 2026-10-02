import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import *
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
dst = bpy.data.objects["PelagSabre"]
sc = bpy.context.scene
lunge = {"Sabre3": (3, 7)}
for name in ["Sabre1", "Sabre2", "Sabre3"]:
    act = bpy.data.actions[name]
    dst.animation_data.action = act
    n = {"Sabre1": 14, "Sabre2": 14, "Sabre3": 22}[name]
    prev = None
    print(name)
    for t in range(n + 1):
        sc.frame_set(t)
        shift = 0.0
        if name in lunge:
            a, b = lunge[name]; shift = 0.30 * min(1.0, max(0.0, (t - a) / float(b - a)))
        dst.location = (0, -shift, 0)
        bpy.context.view_layer.update()
        lf = wpos(dst, "mixamorig:LeftToeBase"); rf = wpos(dst, "mixamorig:RightToeBase")
        h = wpos(dst, "mixamorig:Hips")
        s = ""
        if prev:
            dl = (lf - prev[0]); dr = (rf - prev[1])
            s = "  dL %.3f (z%.3f)  dR %.3f (z%.3f)" % (Vector((dl.x, dl.y)).length, lf.z, Vector((dr.x, dr.y)).length, rf.z)
        print("  t%02d hips %.3f,%.3f,%.3f%s" % (t, h.x, h.y, h.z, s))
        prev = (lf, rf)
    dst.location = (0, 0, 0)
