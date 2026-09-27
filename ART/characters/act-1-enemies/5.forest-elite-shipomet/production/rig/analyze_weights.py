"""Per-bone weight regions of a rig + dominant-bone renders with joint markers.

usage: blender -b -P analyze_weights.py -- <glb|blend> <out_dir>
"""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_common as rc  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
src = Path(args[0]).resolve()
out = Path(args[1]).resolve()
out.mkdir(parents=True, exist_ok=True)

if src.suffix.lower() == ".glb":
    arm, mesh = rc.load_meshy(src)
else:
    bpy.ops.wm.open_mainfile(filepath=str(src))
    arm, mesh = rc.find_rig()

mw = mesh.matrix_world
names = [g.name for g in mesh.vertex_groups]
pal = rc.bone_palette(names)
stats = {n: {"dominant": 0, "any": 0, "min": [9, 9, 9], "max": [-9, -9, -9], "sum": [0, 0, 0]} for n in names}
dom_idx = []
for v in mesh.data.vertices:
    p = mw @ v.co
    best, bw = None, -1
    for g in v.groups:
        if g.weight <= 1e-5:
            continue
        name = names[g.group]
        stats[name]["any"] += 1
        if g.weight > bw:
            best, bw = name, g.weight
    dom_idx.append(best)
    if best:
        s = stats[best]
        s["dominant"] += 1
        for i in range(3):
            s["min"][i] = min(s["min"][i], p[i])
            s["max"][i] = max(s["max"][i], p[i])
            s["sum"][i] += p[i]
for n, s in stats.items():
    if s["dominant"]:
        s["centroid"] = [round(c / s["dominant"], 3) for c in s["sum"]]
    s["min"] = [round(c, 3) for c in s["min"]]
    s["max"] = [round(c, 3) for c in s["max"]]
    del s["sum"]
joints = {b.name: [round(c, 3) for c in (arm.matrix_world @ b.head_local)] for b in arm.data.bones}
(out / "weight_regions.json").write_text(json.dumps({"joints": joints, "regions": stats}, indent=1), encoding="utf-8")
for n in names:
    s = stats[n]
    print("REGION", n, "dom", s["dominant"], "any", s["any"], "min", s["min"], "max", s["max"], "c", s.get("centroid"))

# colour by dominant bone
me = mesh.data
attr = me.color_attributes.get("dominant") or me.color_attributes.new("dominant", "BYTE_COLOR", "POINT")
for i, n in enumerate(dom_idx):
    attr.data[i].color = pal.get(n, (0.2, 0.2, 0.2, 1))
me.color_attributes.active_color = attr

scene, cam = rc.setup_scene(900)
scene.display.shading.color_type = "VERTEX"
scene.display.shading.show_cavity = False
for b in arm.data.bones:
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=arm.matrix_world @ b.head_local)
    s = bpy.context.active_object
    s.name = "J_" + b.name
    s.show_in_front = True
    m = bpy.data.materials.new("J_" + b.name)
    m.diffuse_color = (1, 1, 1, 1)
    s.data.materials.append(m)
    s.color = (1, 1, 1, 1)
# mesh in X-ray so joints read through
scene.display.shading.show_xray = True
scene.display.shading.xray_alpha = 0.75
for view in ("front", "side"):
    rc.place_camera(cam, view, ortho=3.0)
    scene.render.filepath = str(out / f"dominant_{view}.png")
    bpy.ops.render.render(write_still=True)
print("LEGEND", json.dumps({n: [round(c, 2) for c in pal[n][:3]] for n in names}))
