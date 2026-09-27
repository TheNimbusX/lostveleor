"""Render weight heatmaps for chosen bones + mesh island stats.

usage: blender -b -P weight_heatmaps.py -- <glb|blend> <out_dir> <view> <ortho> <tz> bone1 bone2 ...
"""
import sys
from pathlib import Path

import bmesh
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_common as rc  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
src, out, view, ortho, tz = Path(args[0]).resolve(), Path(args[1]).resolve(), args[2], float(args[3]), float(args[4])
bones = args[5:]
out.mkdir(parents=True, exist_ok=True)
if src.suffix.lower() == ".glb":
    arm, mesh = rc.load_meshy(src)
else:
    bpy.ops.wm.open_mainfile(filepath=str(src))
    arm, mesh = rc.find_rig()

bm = bmesh.new()
bm.from_mesh(mesh.data)
bm.verts.ensure_lookup_table()
seen = set()
islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack = [v]
    seen.add(v.index)
    comp = []
    while stack:
        cur = stack.pop()
        comp.append(cur.index)
        for e in cur.link_edges:
            o = e.other_vert(cur)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    islands.append(comp)
islands.sort(key=len, reverse=True)
print("ISLANDS", len(islands), [len(c) for c in islands[:12]])
bm.free()

me = mesh.data
attr = me.color_attributes.get("heat") or me.color_attributes.new("heat", "BYTE_COLOR", "POINT")
me.color_attributes.active_color = attr
scene, cam = rc.setup_scene(700)
scene.display.shading.color_type = "VERTEX"
scene.display.shading.show_cavity = False
rc.place_camera(cam, view, target=(0, 0, tz), ortho=ortho)
for bone in bones:
    gi = mesh.vertex_groups[bone].index
    for v in me.vertices:
        w = 0.0
        for g in v.groups:
            if g.group == gi:
                w = g.weight
        # blue -> red ramp
        attr.data[v.index].color = (w, 0.15 + 0.5 * w * (1 - w), 1 - w, 1)
    scene.render.filepath = str(out / f"heat_{bone}_{view}.png")
    bpy.ops.render.render(write_still=True)
