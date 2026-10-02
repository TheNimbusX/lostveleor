import bpy, math, sys
from mathutils import Vector
path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
act = arm.animation_data.action
sc = bpy.context.scene
out = []
for f in range(int(act.frame_range[0]), int(act.frame_range[1]) + 1):
    sc.frame_set(f)
    mw = arm.matrix_world
    v = (mw @ arm.pose.bones["mixamorig:Neck"].head - mw @ arm.pose.bones["mixamorig:Hips"].head).normalized()
    out.append("%d:%.0f" % (f, math.degrees(v.angle(Vector((0, 0, 1))))))
print("LEAN", " ".join(out))
