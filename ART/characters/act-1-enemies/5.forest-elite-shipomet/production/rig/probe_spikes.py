"""Find the long arm spikes (red texture, largest red component in each hand region)."""
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_common as rc  # noqa: E402
import rig_geo as rg  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
arm, mesh = rc.load_meshy(Path(args[0]).resolve())
names = [g.name for g in mesh.vertex_groups]
co = rg.world_coords(mesh)
img = next(i for i in bpy.data.images if i.size[0] > 0)
col = rg.vertex_colors_from_image(mesh, img)
red = rg.is_red(col)
print("RED verts", int(red.sum()), "of", len(red))
weld = rg.weld_ids(co)
for side in ("Left", "Right"):
    bones = {side + "ForeArm", side + "Hand"}
    memb = np.zeros(len(co))
    for v in mesh.data.vertices:
        memb[v.index] = sum(g.weight for g in v.groups if names[g.group] in bones)
    mask = red & (memb > 0.5)
    roots = rg.welded_components(mesh, weld, mask)
    ids, counts = np.unique(roots[mask], return_counts=True)
    order = np.argsort(-counts)
    for r in order[:6]:
        sel = mask & (roots == ids[r])
        p = co[sel]
        top = p[np.argmax(p[:, 2])]
        low = p[np.argmin(p[:, 2])]
        print(f"SPIKE {side} n{counts[r]} min{np.round(p.min(0),3)} max{np.round(p.max(0),3)} top{np.round(top,3)} low{np.round(low,3)} mean{np.round(p.mean(0),3)}")
