"""Inspect the 24k Splitter candidate: objects, bounds, loose islands (for shell segmentation)."""
import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

src = Path(sys.argv[sys.argv.index("--") + 1])
out = Path(sys.argv[sys.argv.index("--") + 2])
bpy.ops.wm.open_mainfile(filepath=str(src))
info = {"objects": []}
for o in bpy.data.objects:
    info["objects"].append({"name": o.name, "type": o.type, "loc": list(o.location),
                            "rot": list(o.rotation_euler), "scale": list(o.scale),
                            "parent": o.parent.name if o.parent else None})
mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
me = mesh_obj.data
mw = mesh_obj.matrix_world
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
# islands
seen = set()
islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack = [v]
    seen.add(v.index)
    ids = []
    while stack:
        cur = stack.pop()
        ids.append(cur.index)
        for e in cur.link_edges:
            w = e.other_vert(cur)
            if w.index not in seen:
                seen.add(w.index)
                stack.append(w)
    cos = [mw @ bm.verts[i].co for i in ids]
    mn = Vector((min(c.x for c in cos), min(c.y for c in cos), min(c.z for c in cos)))
    mx = Vector((max(c.x for c in cos), max(c.y for c in cos), max(c.z for c in cos)))
    islands.append({"verts": len(ids), "min": [round(x, 3) for x in mn], "max": [round(x, 3) for x in mx]})
islands.sort(key=lambda d: -d["verts"])
cos = [mw @ v.co for v in me.vertices]
info["mesh"] = {
    "name": mesh_obj.name, "verts": len(me.vertices), "faces": len(me.polygons),
    "tris": sum(len(p.vertices) - 2 for p in me.polygons),
    "min": [min(c[i] for c in cos) for i in range(3)], "max": [max(c[i] for c in cos) for i in range(3)],
    "materials": [m.name if m else None for m in me.materials],
    "uv_layers": [u.name for u in me.uv_layers],
    "color_attrs": [a.name for a in me.color_attributes],
    "vertex_groups": [g.name for g in mesh_obj.vertex_groups],
    "modifiers": [m.type for m in mesh_obj.modifiers],
    "islands": len(islands), "island_list": islands[:40],
    "boundary_edges": sum(e.is_boundary for e in bm.edges),
}
info["images"] = [{"name": i.name, "size": list(i.size), "path": i.filepath, "packed": bool(i.packed_file)} for i in bpy.data.images]
bm.free()
out.write_text(json.dumps(info, indent=1), encoding="utf-8")
print("DONE")
