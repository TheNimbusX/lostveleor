"""Ortho views of the candidate (front/side/top/bottom) + vertex dump for slice plots.
Args: -- <blend> <out_dir>
"""
import sys, json, math
from pathlib import Path
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = Path(argv[0]), Path(argv[1])
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
mesh = next(o for o in scene.objects if o.type == "MESH")

scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_cavity = True
scene.render.resolution_x = scene.render.resolution_y = 1000
scene.render.image_settings.file_format = "PNG"
cam_data = bpy.data.cameras.new("C")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 2.0
cam = bpy.data.objects.new("C", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.clip_end = 100

CZ = 0.675
views = {
    # name: (location, look target) ; front = creature faces -Y so camera at -Y
    "front": ((0, -10, CZ), (0, 0, CZ)),
    "back": ((0, 10, CZ), (0, 0, CZ)),
    "left_side": ((10, 0, CZ), (0, 0, CZ)),   # creature's left is +X
    "right_side": ((-10, 0, CZ), (0, 0, CZ)),
    "top": ((0, 0, 10), (0, 0, 0)),
    "bottom": ((0, 0, -10), (0, 0, 0)),
}
meta = {}
for name, (loc, tgt) in views.items():
    cam.location = loc
    d = Vector(tgt) - Vector(loc)
    up = "Y" if name not in ("top", "bottom") else "Y"
    if name in ("top", "bottom"):
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    else:
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(out / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    meta[name] = {"loc": list(loc), "mat": [list(r) for r in cam.matrix_world]}

# vertex dump
import array
co = [c for v in mesh.data.vertices for c in (mesh.matrix_world @ v.co)]
(out / "verts.json").write_text(json.dumps({"co": co, "meta": meta, "ortho": 2.0, "res": 1000}))
print("VIEWS_DONE")
