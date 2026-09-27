"""Render frames of the RE-IMPORTED FBX at the game angle (52 deg down, 3/4 front) into check/renders.

blender -b --factory-startup -P check_render.py -- [take:frame,frame ...] [--close] [--side] [--wire]
Default: 5-6 frames per take. --close = tight head/shell framing, --side = orthographic side view.
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import check_common as cc  # noqa: E402

OUT = Path(__file__).resolve().parent / "renders"
OUT.mkdir(exist_ok=True)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
CLOSE, SIDE, WIRE = "--close" in argv, "--side" in argv, "--wire" in argv
DEFAULT = {"Idle": [0, 15, 30, 45, 60], "Walk": [0, 2, 4, 6, 8, 10], "Bite": [0, 10, 14, 17, 18, 22],
           "Hit": [0, 3, 6, 9, 12], "Death": [0, 3, 6, 9, 12], "Pop": [0, 2, 5, 8, 10]}
jobs = {}
for a in argv:
    if ":" in a:
        k, v = a.split(":")
        jobs[k] = [float(x) for x in v.split(",")]
jobs = jobs or DEFAULT

arm, mesh = cc.import_fbx()
s = cc.scene
s.render.engine = "BLENDER_EEVEE"
s.render.resolution_x = s.render.resolution_y = 560
s.render.image_settings.file_format = "PNG"
s.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("W")
s.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.66, 0.66, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.0
s.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(12), math.radians(-30))
bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
g = bpy.context.active_object
gm = bpy.data.materials.new("G")
gm.use_nodes = True
nt = gm.node_tree
chk = nt.nodes.new("ShaderNodeTexChecker")
chk.inputs["Scale"].default_value = 24.0
chk.inputs["Color1"].default_value = (0.5, 0.56, 0.47, 1)
chk.inputs["Color2"].default_value = (0.42, 0.48, 0.40, 1)
nt.links.new(chk.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
g.data.materials.append(gm)
if WIRE:
    w = mesh.modifiers.new("wire", "WIREFRAME")
    w.thickness = 0.002
    w.use_replace = False

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
s.collection.objects.link(cam)
s.camera = cam
if SIDE:
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.9 if not CLOSE else 1.0
    t = Vector((0.0, -0.3, 0.55)) if not CLOSE else Vector((0.0, -0.65, 0.5))
    eye = t + Vector((6.0, 0.0, 0.3))
else:
    el, az = math.radians(52), math.radians(45)
    t = Vector((0.0, -0.1, 0.45)) if not CLOSE else Vector((0.0, -0.55, 0.5))
    dist = 2.5 if not CLOSE else 1.6
    eye = t + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * dist
    cam.data.lens = 50
cam.location = eye
cam.rotation_euler = (t - eye).to_track_quat("-Z", "Y").to_euler()
tag = ("close_" if CLOSE else "") + ("side_" if SIDE else "game_") + ("wire_" if WIRE else "")
for short, frames in jobs.items():
    act = cc.take_action("ForestSplitter_" + short)
    cc.use(arm, act)
    for f in frames:
        cc.goto(f)
        s.render.filepath = str(OUT / ("%s%s_%05.2f.png" % (tag, short, f)))
        bpy.ops.render.render(write_still=True)
print("RENDER_DONE", flush=True)
