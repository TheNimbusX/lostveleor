import bpy, sys, math, os
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
src, out, step = argv[0], argv[1], int(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
act = arm.animation_data.action
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.display.shading.color_type = 'MATERIAL'
sc.render.resolution_x = 360; sc.render.resolution_y = 360
sc.render.film_transparent = False
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.55,0.55,0.55)
# камера спереди (персонаж смотрит на -Y)
h = 0.9
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.6
os.makedirs(out, exist_ok=True)
views = {"front": (Vector((0,-3,0.45)), (math.radians(90),0,0)), "top": (Vector((0,-2.2,2.5)), (math.radians(42),0,0))}
for f in range(f0, f1+1, step):
    sc.frame_set(f)
    for name,(loc,rot) in views.items():
        cam.location = loc; cam.rotation_euler = rot
        sc.render.filepath = os.path.join(out, f"{name}_{f:03d}.png")
        bpy.ops.render.render(write_still=True)
print("done", f0, f1)
