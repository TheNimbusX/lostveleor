"""Render the +X half (x > cut) seen from -X (inner side of the +X arm/leg), ortho.
Args: -- <blend> <out_png> <cut_x>
"""
import sys
from pathlib import Path
import bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_png, cut = Path(argv[0]), Path(argv[1]), float(argv[2])
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
mesh = next(o for o in scene.objects if o.type == "MESH")
bm = bmesh.new(); bm.from_mesh(mesh.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.x < cut], context="VERTS")
bm.to_mesh(mesh.data); bm.free()
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
sh.show_backface_culling = False
scene.render.resolution_x = scene.render.resolution_y = 1000
cd = bpy.data.cameras.new("C"); cd.type = "ORTHO"; cd.ortho_scale = 2.0
cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (-10, 0, 0.675)
cam.rotation_euler = (Vector((0, 0, 0.675)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = str(out_png)
bpy.ops.render.render(write_still=True)
print("CUT_DONE")
