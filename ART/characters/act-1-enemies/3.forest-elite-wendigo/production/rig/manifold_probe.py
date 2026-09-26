"""Attempt a local manifold cleanup before a QuadriFlow retopo trial."""

import json
import sys
import time
from pathlib import Path

import bmesh
import bpy

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
target = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
target.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
obj = next(o for o in bpy.data.objects if o.type == "MESH")
mesh = obj.data
bm = bmesh.new()
bm.from_mesh(mesh)
bm.faces.ensure_lookup_table()
bm.edges.ensure_lookup_table()
initial = {"vertices": len(bm.verts), "faces": len(bm.faces),
           "boundary": sum(len(e.link_faces) == 1 for e in bm.edges),
           "overfull": sum(len(e.link_faces) > 2 for e in bm.edges)}
removed = 0
for _ in range(4):
    offenders = [edge for edge in bm.edges if len(edge.link_faces) > 2]
    if not offenders:
        break
    to_remove = set()
    for edge in offenders:
        faces = list(edge.link_faces)
        faces.sort(key=lambda f: f.calc_area())
        to_remove.update(faces[:len(faces) - 2])
    removed += len(to_remove)
    bmesh.ops.delete(bm, geom=list(to_remove), context="FACES_ONLY")
before_fill = sum(len(e.link_faces) == 1 for e in bm.edges)
try:
    filled = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if len(e.link_faces) == 1], sides=0)
    fill_count = len(filled.get("faces", []))
except Exception as exc:
    fill_count = -1
    print("HOLES_FILL_ERROR", repr(exc), flush=True)
remaining = {edge for edge in bm.edges if len(edge.link_faces) == 1}
while remaining:
    first = remaining.pop()
    ordered = [first.verts[0], first.verts[1]]
    previous = first
    while True:
        current = ordered[-1]
        next_edges = [edge for edge in remaining if current in edge.verts]
        if not next_edges:
            break
        next_edge = next_edges[0]
        remaining.remove(next_edge)
        other = next_edge.other_vert(current)
        if other == ordered[0]:
            break
        ordered.append(other)
    if len(ordered) >= 3:
        try:
            bm.faces.new(ordered)
            fill_count += 1
        except ValueError:
            print("FILL_FAILED", [tuple(v.co) for v in ordered], flush=True)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
after = {"vertices": len(bm.verts), "faces": len(bm.faces),
         "boundary": sum(len(e.link_faces) == 1 for e in bm.edges),
         "overfull": sum(len(e.link_faces) > 2 for e in bm.edges)}
if after["boundary"]:
    print("REMAINING_BOUNDARY", [
        [tuple(round(c, 4) for c in v.co) for v in edge.verts]
        for edge in bm.edges if len(edge.link_faces) == 1
    ], flush=True)
bm.to_mesh(mesh)
bm.free()
mesh.update()
report = {"initial": initial, "removed_faces": removed,
          "boundary_before_fill": before_fill, "filled_faces": fill_count,
          "after": after}

if after["boundary"] == 0 and after["overfull"] == 0:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    repaired = target / "manifold_repaired.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(repaired))
    report["repaired"] = str(repaired)
    copy = obj.copy()
    copy.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(copy)
    obj.hide_set(True)
    obj.hide_render = True
    bpy.ops.object.select_all(action="DESELECT")
    copy.select_set(True)
    bpy.context.view_layer.objects.active = copy
    start = time.monotonic()
    print("QUADRIFLOW_START", flush=True)
    try:
        bpy.ops.object.quadriflow_remesh(mode="FACES", target_faces=10500,
                                        use_preserve_sharp=True,
                                        use_preserve_boundary=False,
                                        preserve_attributes=True,
                                        use_mesh_symmetry=False, seed=1)
        copy.data.calc_loop_triangles()
        report["quadriflow"] = {
            "faces": len(copy.data.polygons),
            "triangles": len(copy.data.loop_triangles),
            "uv_layers": len(copy.data.uv_layers),
            "seconds": round(time.monotonic() - start, 2),
        }
        bpy.ops.wm.save_as_mainfile(filepath=str(target / "quadriflow_repaired_probe.blend"))
    except Exception as exc:
        report["quadriflow_error"] = repr(exc)

(target / "manifold_probe.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
