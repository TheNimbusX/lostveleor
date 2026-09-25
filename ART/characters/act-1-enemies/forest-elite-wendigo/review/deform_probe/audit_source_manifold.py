"""Compare source topology to the decimated candidate without changing either."""

import json
import sys
from pathlib import Path

import bmesh
import bpy


source, output = (Path(value).resolve() for value in sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source))
obj, = [o for o in bpy.context.scene.objects if o.type == "MESH"]
mesh = obj.data
bm = bmesh.new()
bm.from_mesh(mesh)
before = {"vertices": len(bm.verts), "faces": len(bm.faces),
          "boundary_edges": sum(len(e.link_faces) == 1 for e in bm.edges),
          "overfull_edges": sum(len(e.link_faces) > 2 for e in bm.edges)}
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.000001)
after = {"vertices": len(bm.verts), "faces": len(bm.faces),
         "boundary_edges": sum(len(e.link_faces) == 1 for e in bm.edges),
         "overfull_edges": sum(len(e.link_faces) > 2 for e in bm.edges)}
report = {"source": str(source), "raw": before, "welded": after}
(output / "source_manifold_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
bm.free()
