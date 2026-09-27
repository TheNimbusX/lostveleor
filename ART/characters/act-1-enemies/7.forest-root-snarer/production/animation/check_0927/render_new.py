"""Render the NEW take of the re-imported FBX at the game camera (52 deg down, 3/4 front) + side / front.
Fresh empty scene, textured Workbench, checker ground exactly at z = 0 (0.5 m cells).
blender -b --factory-startup -P render_new.py -- <fbx> <out_dir> <take> <frames comma> [views comma]"""
import math, sys
from pathlib import Path
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT, TAKE = argv[0], Path(argv[1]), argv[2]
FRAMES = [int(x) for x in argv[3].split(",")]
VIEWS = argv[4].split(",") if len(argv) > 4 else ["game"]
DIRS = {"game": (52, 35), "side": (6, 90), "front": (8, 0), "back": (30, 170), "low": (12, 40)}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX, use_anim=True, automatic_bone_orientation=False, anim_offset=0.0)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == TAKE)
ad = arm.animation_data or arm.animation_data_create()
ad.action = act
if hasattr(ad, "action_slot") and ad.action_slot is None:
    ad.action_slot = act.slots[0]
arm.hide_render = True
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.color_type, sh.show_cavity, sh.show_shadows = "STUDIO", "TEXTURE", True, True
sh.shadow_intensity = 0.5
sh.background_type = "VIEWPORT"
sh.background_color = (0.5, 0.54, 0.52)
sc.display.light_direction = (0.35, -0.45, 0.82)
sc.render.resolution_x, sc.render.resolution_y = 520, 400
sc.render.image_settings.file_format = "PNG"
bpy.ops.mesh.primitive_plane_add(size=10, location=(0, 0, 0))
g = bpy.context.object
img = bpy.data.images.new("ck", 20, 20)
px = []
for y in range(20):
    for x in range(20):
        c = 0.52 if (x + y) % 2 else 0.70
        px += [c, c * 1.04, c * 0.96, 1.0]
img.pixels = px
m = bpy.data.materials.new("ck")
m.use_nodes = True
t = m.node_tree.nodes.new("ShaderNodeTexImage")
t.image, t.interpolation = img, "Closest"
m.node_tree.links.new(t.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
g.data.materials.append(m)
cd = bpy.data.cameras.new("c")
cd.type, cd.ortho_scale, cd.clip_end = "ORTHO", 3.0, 200
cam = bpy.data.objects.new("c", cd)
sc.collection.objects.link(cam)
sc.camera = cam
OUT.mkdir(parents=True, exist_ok=True)
for v in VIEWS:
    el, az = (math.radians(a) for a in DIRS[v])
    d = Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))
    cam.location = Vector((0, -0.15, 0.55)) + d * 40
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    for f in FRAMES:
        sc.frame_set(f)
        sc.render.filepath = str(OUT / f"{TAKE.split('_')[-1]}_{v}_{f:03d}.png")
        bpy.ops.render.render(write_still=True)
print("RENDER_DONE", flush=True)
