import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from b_common import *
argv = sys.argv[sys.argv.index("--") + 1:]
src, out, f0, f1, step = argv[0], argv[1], int(argv[2]), int(argv[3]), int(argv[4])
views = argv[5].split(",") if len(argv) > 5 else ["front", "game"]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in bpy.data.objects:
    if o.type == 'MESH': o.color = (0.75, 0.72, 0.68, 1)
bl = make_blade_object()
cam = setup_render(320)
sc = bpy.context.scene
os.makedirs(out, exist_ok=True)
sc.frame_set(f0)
hips0 = wpos(arm, "mixamorig:Hips")
height = (wpos(arm, "mixamorig:Head") - wpos(arm, "mixamorig:LeftFoot")).length
for f in range(f0, f1 + 1, step):
    sc.frame_set(f)
    place_blade(bl, arm, height * 0.02)
    tgt = hips0.copy(); tgt.z = hips0.z * 0.9
    for v in views:
        aim_camera(cam, tgt, v, height, height * 2.6)
        sc.render.filepath = os.path.join(out, f"{v}_{f:03d}.png")
        bpy.ops.render.render(write_still=True)
print("ok")
