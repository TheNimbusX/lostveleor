"""Render frames of the re-imported FBX (Workbench, textured, checker ground at z=0).
blender -b --factory-startup -P render_check.py -- <fbx> <out_dir> <spec.json>
spec: {"<take suffix>": {"frames": [..], "views": ["game", ...], "ortho": 3.4, "center": [x,y,z]}}"""
import json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_lib as L  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT, SPEC = argv[0], Path(argv[1]), json.loads(Path(argv[2]).read_text())
VIEWS = {"game": (52, 35), "game_r": (52, -35), "side": (8, 90), "front": (10, 0), "top": (80, 20),
         "back": (35, 160), "low": (15, 30)}

arm, mesh, acts = L.import_fbx(FBX)
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.color_type, sh.show_cavity, sh.show_shadows = "STUDIO", "TEXTURE", True, True
sh.shadow_intensity = 0.4
sh.background_type = "VIEWPORT"
sh.background_color = (0.55, 0.59, 0.57)
sc.display.light_direction = (0.35, -0.45, 0.82)
sc.render.resolution_x, sc.render.resolution_y = 560, 420
sc.render.image_settings.file_format = "PNG"
bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
g = bpy.context.object
m = bpy.data.materials.new("chk")
m.use_nodes = True
nt = m.node_tree
img = bpy.data.images.new("chk", 64, 64)          # Workbench shows image textures only
px = []
for y in range(64):
    for x in range(64):
        c = 0.60 if ((x // 32) + (y // 32)) % 2 else 0.76
        px += [c, c * 1.03, c * 0.97, 1.0]
img.pixels = px
ck = nt.nodes.new("ShaderNodeTexImage")
ck.image = img
ck.interpolation = "Closest"
nt.links.new(ck.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
for loop in g.data.uv_layers.active.data:          # 0.5 m cells on the 12 m plane
    loop.uv = loop.uv * 12
g.data.materials.append(m)
g.display.show_shadows = True
cd = bpy.data.cameras.new("cam")
cd.type = "ORTHO"
cam = bpy.data.objects.new("cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam
arm.hide_render = True
OUT.mkdir(parents=True, exist_ok=True)
for take, s in SPEC.items():
    act = acts["ForestRootSnarer_" + take]
    L.set_action(arm, act)
    for view in s.get("views", ["game"]):
        el, az = (math.radians(a) for a in VIEWS[view])
        d = Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))
        c = Vector(s.get("center", (0, 0, 0.7)))
        cam.location = c + d * 30
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        cd.ortho_scale = s.get("ortho", 3.6)
        cd.clip_end = 100
        for f in s["frames"]:
            sc.frame_set(f)
            sc.render.filepath = str(OUT / f"{take}_{view}_{f:03d}.png")
            bpy.ops.render.render(write_still=True)
print("RENDER_DONE", flush=True)
