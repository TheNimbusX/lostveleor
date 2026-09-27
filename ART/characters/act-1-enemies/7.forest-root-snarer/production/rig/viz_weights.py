"""Render dominant-bone colouring (or one bone's weight map) in ortho views.
Args: -- <rig.blend> <out_dir> [bone_name ...]   (no bone names = dominant-bone map)
"""
import sys, colorsys
from pathlib import Path
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = Path(argv[0]).resolve(), Path(argv[1]).resolve()
bones = argv[2:]
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
mesh = next(o for o in scene.objects if o.type == "MESH")
for o in scene.objects:
    if o.type == "ARMATURE":
        o.hide_render = True
me = mesh.data
names = [g.name for g in mesh.vertex_groups]
FIXED = {"pelvis": (0.9, 0.1, 0.1), "spine_01": (1.0, 0.55, 0.0), "spine_02": (1.0, 0.95, 0.1),
         "neck": (0.5, 0.9, 0.3), "head": (0.1, 0.9, 0.9), "jaw": (1, 1, 1),
         "clavicle": (0.6, 0.2, 0.9), "arm_upper": (1.0, 0.2, 0.8), "arm_lower": (0.1, 0.7, 0.1),
         "hand": (0.15, 0.3, 1.0), "leg_upper": (1.0, 0.6, 0.7), "leg_lower": (0.55, 0.35, 0.15),
         "foot": (0.6, 0.6, 0.6)}
palette = {}
for i, n in enumerate(names):
    key = n[2:] if n[:2] in ("L_", "R_") else n
    c = FIXED.get(key, colorsys.hsv_to_rgb((i * 0.618034) % 1.0, 0.85, 1.0))
    if n.startswith("R_"):
        c = tuple(0.7 * x for x in c)
    palette[n] = c

attr = me.color_attributes.new("viz", "FLOAT_COLOR", "POINT")


def paint(bone):
    gi = mesh.vertex_groups[bone].index if bone else None
    for v in me.vertices:
        if bone:
            w = next((g.weight for g in v.groups if g.group == gi), 0.0)
            c = (w, 0.1, 1.0 - w) if w > 0 else (0.05, 0.05, 0.3)
        else:
            tot = sum(g.weight for g in v.groups) or 1.0
            c = tuple(sum(palette[names[g.group]][k] * g.weight for g in v.groups) / tot for k in range(3))
            if not v.groups:
                c = (0, 0, 0)
        attr.data[v.index].color = (*c, 1.0)


scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"; sh.color_type = "VERTEX"; sh.show_cavity = False
scene.render.resolution_x = scene.render.resolution_y = 700
cd = bpy.data.cameras.new("C"); cd.type = "ORTHO"; cd.ortho_scale = 2.0
cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
me.color_attributes.active_color = attr
CZ = 0.675
views = {"front": (0, -10, CZ), "left": (10, 0, CZ), "right": (-10, 0, CZ), "back": (0, 10, CZ),
         "top": (0, 0.001, 10), "bottom": (0, 0.001, -10)}
for bone in (bones or [None]):
    paint(bone)
    tag = bone or "dominant"
    for vn, loc in views.items():
        cam.location = loc
        tgt = Vector((0, 0, CZ)) if vn not in ("top", "bottom") else Vector((0, 0, 0.5))
        cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(out / f"{tag}_{vn}.png")
        bpy.ops.render.render(write_still=True)
# legend
(out / "legend.txt").write_text("\n".join(f"{n} {tuple(round(c,2) for c in palette[n])}" for n in names))
print("VIZ_DONE")
