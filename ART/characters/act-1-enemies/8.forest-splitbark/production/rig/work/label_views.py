"""Render the candidate with per-vertex label colours (from a .npy) from several angles."""
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import render_util as ru  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
labels = np.load(args[0])
out = Path(args[1])
out.mkdir(parents=True, exist_ok=True)
views = args[2].split(",") if len(args) > 2 else ["front", "back", "left", "right", "top", "below", "front34", "back34"]
pal = np.array([(0.85, 0.25, 0.2), (0.2, 0.45, 0.95), (0.2, 0.8, 0.3), (0.95, 0.8, 0.15), (0.8, 0.3, 0.85),
                (0.2, 0.85, 0.85), (1, 0.55, 0), (0.6, 0.6, 0.6), (1, 1, 1), (0.45, 0.25, 0.1), (0.05, 0.05, 0.05),
                (1, 0.5, 0.7), (0.35, 0.8, 0.6), (0.5, 0.5, 1.0), (0.7, 1.0, 0.4), (1.0, 0.3, 0.5)])
obj = bpy.data.objects["SM_ForestSplitter_LOD0"]
me = obj.data
if labels.dtype.kind == "f" and labels.ndim == 2:
    vc = labels[:, :3]
else:
    vc = pal[labels.astype(int) % len(pal)]
attr = me.color_attributes.new("RigLabel", "FLOAT_COLOR", "POINT")
cols = np.ones((len(me.vertices), 4), np.float32)
cols[:, :3] = vc
attr.data.foreach_set("color", cols.ravel())
me.color_attributes.active_color = attr
scene = ru.setup_scene("WORKBENCH", int(__import__("os").environ.get("LV_RES", 700)))
scene.display.shading.color_type = "VERTEX"
for v in views:
    eye, tgt = ru.VIEWS[v]
    ru.shot(out / f"{v}.png", eye, tgt, ortho=1.9 if v in ("front", "back", "left", "right", "top", "below") else None, lens=50)
print("LABEL_VIEWS_DONE")
