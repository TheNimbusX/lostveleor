"""Inspect the 24k candidate: objects, bounds, islands, slice profiles (read-only)."""
import sys, json
from pathlib import Path
import bpy, bmesh
from mathutils import Vector

src = Path(sys.argv[sys.argv.index("--") + 1])
if src.suffix == ".glb":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(src))
else:
    bpy.ops.wm.open_mainfile(filepath=str(src))

out = {"objects": []}
for o in bpy.data.objects:
    d = {"name": o.name, "type": o.type, "loc": list(o.location), "rot": list(o.rotation_euler),
         "scale": list(o.scale), "parent": o.parent.name if o.parent else None}
    if o.type == "MESH":
        me = o.data
        me.calc_loop_triangles()
        ws = [o.matrix_world @ v.co for v in me.vertices]
        mn = Vector((min(v.x for v in ws), min(v.y for v in ws), min(v.z for v in ws)))
        mx = Vector((max(v.x for v in ws), max(v.y for v in ws), max(v.z for v in ws)))
        bm = bmesh.new(); bm.from_mesh(me)
        # islands
        seen = set(); islands = []
        bm.verts.ensure_lookup_table()
        for v in bm.verts:
            if v.index in seen: continue
            stack = [v]; comp = []
            seen.add(v.index)
            while stack:
                a = stack.pop(); comp.append(a.index)
                for e in a.link_edges:
                    b = e.other_vert(a)
                    if b.index not in seen:
                        seen.add(b.index); stack.append(b)
            islands.append(comp)
        islands.sort(key=len, reverse=True)
        isl = []
        for comp in islands[:30]:
            pts = [ws[i] for i in comp]
            isl.append({"n": len(comp),
                        "min": [round(min(p[k] for p in pts), 3) for k in range(3)],
                        "max": [round(max(p[k] for p in pts), 3) for k in range(3)]})
        d.update({"verts": len(me.vertices), "tris": len(me.loop_triangles), "min": list(mn), "max": list(mx),
                  "materials": [m.name if m else None for m in me.materials],
                  "uv_layers": [u.name for u in me.uv_layers],
                  "color_attrs": [c.name for c in me.color_attributes],
                  "vgroups": [g.name for g in o.vertex_groups],
                  "modifiers": [m.type for m in o.modifiers],
                  "island_count": len(islands), "islands": isl,
                  "boundary_edges": sum(e.is_boundary for e in bm.edges),
                  "nonmanifold_edges": sum(len(e.link_faces) > 2 for e in bm.edges)})
        bm.free()
        # slice profiles: for z bands, x/y extents
        prof = []
        for zi in range(0, 28):
            z0 = zi * 0.05; z1 = z0 + 0.05
            pts = [p for p in ws if z0 <= p.z < z1]
            if not pts: continue
            prof.append({"z": round(z0, 2), "n": len(pts),
                         "x": [round(min(p.x for p in pts), 2), round(max(p.x for p in pts), 2)],
                         "y": [round(min(p.y for p in pts), 2), round(max(p.y for p in pts), 2)]})
        d["z_profile"] = prof
        profy = []
        for yi in range(-20, 20):
            y0 = yi * 0.05; y1 = y0 + 0.05
            pts = [p for p in ws if y0 <= p.y < y1]
            if not pts: continue
            profy.append({"y": round(y0, 2), "n": len(pts),
                          "x": [round(min(p.x for p in pts), 2), round(max(p.x for p in pts), 2)],
                          "z": [round(min(p.z for p in pts), 2), round(max(p.z for p in pts), 2)]})
        d["y_profile"] = profy
    out["objects"].append(d)
out["images"] = [(i.name, list(i.size)) for i in bpy.data.images]
out_path = Path(sys.argv[sys.argv.index("--") + 2])
out_path.write_text(json.dumps(out, indent=1), encoding="utf-8")
print("DONE")
