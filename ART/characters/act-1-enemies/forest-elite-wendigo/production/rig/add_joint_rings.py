"""Add spatially limited support rings around Wendigo's deforming joints."""

import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out_dir = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
out_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
obj = next(obj for obj in bpy.data.objects if obj.type == "MESH")
mesh = obj.data
mesh.calc_loop_triangles()
before = len(mesh.loop_triangles)
# Reserve triangles for deliberate support rings rather than exhausting the
# budget with random edge density on broad, rigid surfaces.
modifier = obj.modifiers.new("ReserveJointBudget", "DECIMATE")
modifier.decimate_type = "COLLAPSE"
modifier.ratio = 22900 / before
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh.calc_loop_triangles()
reserved_triangles = len(mesh.loop_triangles)
bm = bmesh.new()
bm.from_mesh(mesh)

def close_local_artifacts():
    for _ in range(4):
        overloaded = [edge for edge in bm.edges if len(edge.link_faces) > 2]
        if not overloaded:
            break
        faces_to_remove = {
            sorted(edge.link_faces, key=lambda face: face.calc_area())[0]
            for edge in overloaded
        }
        bmesh.ops.delete(bm, geom=list(faces_to_remove), context="FACES_ONLY")
    boundary = [edge for edge in bm.edges if len(edge.link_faces) == 1]
    if boundary:
        bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    remaining = {edge for edge in bm.edges if len(edge.link_faces) == 1}
    while remaining:
        first = remaining.pop()
        ordered = [first.verts[0], first.verts[1]]
        while True:
            current = ordered[-1]
            choices = [edge for edge in remaining if current in edge.verts]
            if not choices:
                break
            next_edge = choices[0]
            remaining.remove(next_edge)
            other = next_edge.other_vert(current)
            if other == ordered[0]:
                break
            ordered.append(other)
        if len(ordered) >= 3:
            try:
                bm.faces.new(ordered)
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))

close_local_artifacts()
steps = []

joint_specs = {
    "L": (
        ("shoulder", (-.37, .29, 1.91), (-.12, .22, -.43), .22, .045),
        ("elbow", (-.49, .51, 1.48), (-.04, .25, -.35), .18, .035),
        ("wrist", (-.53, .76, 1.13), (-.01, .11, -.28), .15, .028),
        ("hip", (-.20, .13, 1.27), (-.23, .14, -.55), .20, .045),
        ("knee", (-.43, .27, .72), (-.17, .12, -.48), .18, .035),
        ("ankle", (-.60, .39, .24), (0, .16, -.15), .14, .025),
    ),
    "R": (
        ("shoulder", (.37, -.42, 1.91), (.05, -.20, -.43), .22, .045),
        ("elbow", (.42, -.62, 1.48), (.09, -.19, -.35), .18, .035),
        ("wrist", (.51, -.81, 1.13), (.01, -.09, -.28), .15, .028),
        ("hip", (.19, -.21, 1.27), (.05, -.22, -.55), .20, .045),
        ("knee", (.24, -.43, .72), (.24, -.08, -.48), .18, .035),
        ("ankle", (.48, -.51, .24), (.07, -.01, -.15), .14, .025),
    ),
}
for side, joints in joint_specs.items():
    for joint_name, center, normal, radius, spacing in joints:
        center = Vector(center)
        normal = Vector(normal).normalized()
        for offset in (-spacing, spacing):
            plane = center + normal * offset
            selected_faces = [
                face for face in bm.faces
                if (face.calc_center_median() - center).length <= radius
                and min((vertex.co - center).length for vertex in face.verts) <= radius
            ]
            selected_edges = {edge for face in selected_faces for edge in face.edges}
            selected_verts = {vert for face in selected_faces for vert in face.verts}
            changed = bmesh.ops.bisect_plane(
                bm,
                geom=selected_faces + list(selected_edges) + list(selected_verts),
                dist=0.000001,
                plane_co=plane,
                plane_no=normal,
                clear_inner=False,
                clear_outer=False,
            )
            steps.append({"joint": f"{side}_{joint_name}", "offset_m": offset,
                          "selected_faces": len(selected_faces),
                          "cut_elements": len(changed.get("geom_cut", []))})

bm.to_mesh(mesh)
bm.free()
mesh.update()
mesh.calc_loop_triangles()
check = bmesh.new()
check.from_mesh(mesh)
after = len(mesh.loop_triangles)
report = {
    "source": str(source),
    "before_triangles": before,
    "after_budget_reserve_triangles": reserved_triangles,
    "after_triangles": after,
    "triangle_budget": 25000,
    "boundary_edges": sum(edge.is_boundary for edge in check.edges),
    "overfull_edges": sum(len(edge.link_faces) > 2 for edge in check.edges),
    "steps": steps,
}
check.free()
print("JOINT_RING_COUNTS", json.dumps({
    "after": after,
    "cuts": {name: sum(s["cut_elements"] for s in steps if s["joint"] == name)
             for name in sorted({s["joint"] for s in steps})},
}), flush=True)
if after > 25000:
    raise RuntimeError(f"Joint rings exceeded budget: {after}")
if report["boundary_edges"] or report["overfull_edges"]:
    raise RuntimeError("Joint rings opened the mesh: " + json.dumps(report))
target = out_dir / "joint_ring_mesh.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(target))
report["blend"] = str(target)
(out_dir / "joint_rings_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({key: value for key, value in report.items() if key != "steps"}), flush=True)
