import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import *
argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
sc = bpy.context.scene
act = arm.animation_data.action
rows = []
print("arm rot", tuple(arm.rotation_euler), "scale", tuple(arm.scale))
for f in range(int(act.frame_range[0]), int(act.frame_range[1]) + 1):
    sc.frame_set(f)
    r, t = blade(arm)
    hips = wpos(arm, "mixamorig:Hips")
    lf = wpos(arm, "mixamorig:LeftFoot"); rf = wpos(arm, "mixamorig:RightFoot")
    # куда смотрит таз: перпендикуляр к линии бёдер в горизонтали
    lu = wpos(arm, "mixamorig:LeftUpLeg"); ru = wpos(arm, "mixamorig:RightUpLeg")
    side = (lu - ru); fwd = Vector((side.y, -side.x, 0)).normalized()  # left × up
    ls = wpos(arm, "mixamorig:LeftArm"); rs = wpos(arm, "mixamorig:RightArm")
    sh = ls - rs; sfwd = Vector((sh.y, -sh.x, 0)).normalized()
    rows.append(dict(f=f, root=list(r), tip=list(t), hips=list(hips), lf=list(lf), rf=list(rf), hipfwd=list(fwd), shfwd=list(sfwd)))
json.dump(rows, open(out, "w"))
print("ok", len(rows))
