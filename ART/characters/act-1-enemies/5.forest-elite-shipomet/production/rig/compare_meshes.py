"""Compare the Meshy char1 mesh with the approved 23k candidate (positions, UVs, image)."""
import sys
from pathlib import Path

import bpy
from mathutils import kdtree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_common as rc  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
arm, mesh = rc.load_meshy(Path(args[0]).resolve())
mw = mesh.matrix_world
mp = [mw @ v.co for v in mesh.data.vertices]
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(Path(args[1]).resolve()))
cand = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
cw = cand.matrix_world
cp = [cw @ v.co for v in cand.data.vertices]
print("MESHY verts", len(mp), "tris", rc.tri_count(mesh), "uv", [l.name for l in mesh.data.uv_layers])
print("CAND verts", len(cp), "tris", rc.tri_count(cand), "uv", [l.name for l in cand.data.uv_layers])
kd = kdtree.KDTree(len(cp))
for i, p in enumerate(cp):
    kd.insert(p, i)
kd.balance()
d = sorted(kd.find(p)[2] for p in mp)
n = len(d)
print("MESHY->CAND dist p50 %.5f p95 %.5f p99 %.5f max %.5f" % (d[n // 2], d[int(n * .95)], d[int(n * .99)], d[-1]))
for im in bpy.data.images:
    print("IMAGE", im.name, im.size[:], im.packed_file is not None, im.filepath)
for m in bpy.data.materials:
    print("MAT", m.name, m.users)
