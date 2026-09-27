"""Dump verts, triangles, per-vertex UV and base-colour samples to .npz for offline analysis."""
import sys
from pathlib import Path

import bpy
import numpy as np

src = Path(sys.argv[sys.argv.index("--") + 1])
out = Path(sys.argv[sys.argv.index("--") + 2])
bpy.ops.wm.open_mainfile(filepath=str(src))
obj = bpy.data.objects["SM_ForestSplitter_LOD0"]
me = obj.data
me.calc_loop_triangles()
nv = len(me.vertices)
co = np.zeros(nv * 3, np.float32)
me.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
tris = np.zeros(len(me.loop_triangles) * 3, np.int32)
me.loop_triangles.foreach_get("vertices", tris)
tris = tris.reshape(-1, 3)
tri_loops = np.zeros(len(me.loop_triangles) * 3, np.int32)
me.loop_triangles.foreach_get("loops", tri_loops)
tri_loops = tri_loops.reshape(-1, 3)
uv = np.zeros(len(me.loops) * 2, np.float32)
me.uv_layers.active.data.foreach_get("uv", uv)
uv = uv.reshape(-1, 2)
img = next(i for i in bpy.data.images if i.name.startswith("Color_"))
w, h = img.size
px = np.zeros(w * h * 4, np.float32)
img.pixels.foreach_get(px)
px = px.reshape(h, w, 4)
tri_uv = uv[tri_loops].mean(axis=1)
xs = np.clip((tri_uv[:, 0] % 1.0) * (w - 1), 0, w - 1).astype(int)
ys = np.clip((tri_uv[:, 1] % 1.0) * (h - 1), 0, h - 1).astype(int)
tri_col = px[ys, xs, :3]
# per-vertex colour: sample each loop's uv, average per vertex
loop_v = np.zeros(len(me.loops), np.int32)
me.loops.foreach_get("vertex_index", loop_v)
lx = np.clip((uv[:, 0] % 1.0) * (w - 1), 0, w - 1).astype(int)
ly = np.clip((uv[:, 1] % 1.0) * (h - 1), 0, h - 1).astype(int)
lcol = px[ly, lx, :3]
vcol = np.zeros((nv, 3), np.float32)
cnt = np.zeros(nv, np.float32)
np.add.at(vcol, loop_v, lcol)
np.add.at(cnt, loop_v, 1)
vcol /= np.maximum(cnt, 1)[:, None]
np.savez(str(out), co=co, tris=tris, tri_col=tri_col, vcol=vcol, tri_uv=tri_uv)
# also save base colour png for the offline tool
img2 = img.copy()
img2.filepath_raw = str(out.with_name("basecolor.png"))
img2.file_format = "PNG"
img2.save()
print("DUMPED", co.shape, tris.shape)
