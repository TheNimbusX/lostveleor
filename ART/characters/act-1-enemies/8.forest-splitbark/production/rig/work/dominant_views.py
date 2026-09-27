"""Colour each vertex by its dominant deform bone (tinted by texture) and render views."""
import sys
from pathlib import Path
import bpy
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import render_util as ru
import rig_spec as spec
out = Path(sys.argv[sys.argv.index("--") + 1]); out.mkdir(parents=True, exist_ok=True)
mesh = bpy.data.objects[spec.MESH_NAME]
names = [g.name for g in mesh.vertex_groups]
rng = np.random.default_rng(3)
pal = {n: rng.uniform(0.15, 1.0, 3) for n in names}
pal.update({"shell_L": np.array([0.2, 0.4, 1.0]), "shell_R": np.array([0.1, 0.9, 0.3]), "body": np.array([0.9, 0.2, 0.2]),
            "spine": np.array([1.0, 0.6, 0.1]), "neck": np.array([0.9, 0.9, 0.2]), "head": np.array([1, 1, 1])})
cols = np.ones((len(mesh.data.vertices), 4), np.float32)
for v in mesh.data.vertices:
    c = np.zeros(3)
    for g in v.groups:
        c += g.weight * pal[names[g.group]]
    cols[v.index, :3] = c
attr = mesh.data.color_attributes.new("Dom", "FLOAT_COLOR", "POINT")
attr.data.foreach_set("color", cols.ravel())
mesh.data.color_attributes.active_color = attr
scene = ru.setup_scene("WORKBENCH", 700)
scene.display.shading.color_type = "VERTEX"
for v in ("front", "right", "back34", "below", "top", "front34_L"):
    e, t = ru.VIEWS[v]
    ru.shot(out / f"{v}.png", e, t, ortho=1.9 if v in ("front", "right", "below", "top") else None, lens=62)
print("legend", {n: [round(x, 2) for x in pal[n]] for n in names})
