import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r"C:\Users\d.grab\Desktop\the-game\artifacts\tools\pelag-combo-blender\v5\Pelag_Sabre_Work.blend")
dst = bpy.data.objects["PelagSabre"]
sc = bpy.context.scene
up = Vector((0, 0, 1))
for name in ("Sabre1", "Sabre2", "Sabre3"):
    act = bpy.data.actions[name]
    dst.animation_data.action = act
    out = []
    for f in range(int(act.frame_range[0]), int(act.frame_range[1]) + 1):
        sc.frame_set(f)
        mw = dst.matrix_world
        hips = mw @ dst.pose.bones["mixamorig:Hips"].head
        neck = mw @ dst.pose.bones["mixamorig:Neck"].head
        head = mw @ dst.pose.bones["mixamorig:Head"].head
        v = (neck - hips).normalized()
        out.append("%d:%.0f/%.2f" % (f, math.degrees(v.angle(up)), head.z))
    print(name, " ".join(out))
