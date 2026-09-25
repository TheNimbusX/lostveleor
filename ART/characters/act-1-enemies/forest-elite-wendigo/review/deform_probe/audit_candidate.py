"""Read-only topology audit of the 24k forest wendigo candidate."""

import json
import math
import sys
from collections import Counter
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


candidate, output = (Path(value).resolve() for value in sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(candidate))
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
assert len(meshes) == 1
obj = meshes[0]
mesh = obj.data
mesh.calc_loop_triangles()
bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
bm.edges.ensure_lookup_table()
bm.faces.ensure_lookup_table()

positions = [v.co.copy() for v in bm.verts]
minimum = [min(p[i] for p in positions) for i in range(3)]
maximum = [max(p[i] for p in positions) for i in range(3)]
face_sizes = Counter(len(face.verts) for face in bm.faces)
edge_faces = Counter(len(edge.link_faces) for edge in bm.edges)
vertex_valence = Counter(len(vertex.link_edges) for vertex in bm.verts)
areas = []
qualities = []
thin_faces = []
for face in bm.faces:
    if len(face.verts) != 3:
        continue
    side = [(a.co - b.co).length for a, b in (
        (face.verts[0], face.verts[1]),
        (face.verts[1], face.verts[2]),
        (face.verts[2], face.verts[0]),
    )]
    area = face.calc_area()
    areas.append(area)
    # Circumradius / (2 * inradius); 1 for an equilateral triangle.
    perimeter = sum(side)
    q = (side[0] * side[1] * side[2] * perimeter / (16 * area * area)) if area > 1e-12 else math.inf
    qualities.append(q)
    if q > 10:
        center = face.calc_center_median()
        thin_faces.append((center.x, center.y, center.z))

connected = []
unvisited = set(bm.verts)
while unvisited:
    seed = unvisited.pop()
    stack = [seed]
    count = 1
    while stack:
        vertex = stack.pop()
        for edge in vertex.link_edges:
            other = edge.other_vert(vertex)
            if other in unvisited:
                unvisited.remove(other)
                stack.append(other)
                count += 1
    connected.append(count)

def region_histogram(coordinates, step=0.25):
    return dict(sorted(Counter(f"{math.floor(z / step) * step:.2f}-{(math.floor(z / step) + 1) * step:.2f}" for _, _, z in coordinates).items()))

def quadrant_histogram(coordinates):
    return dict(sorted(Counter(
        f"{'left' if x < -0.25 else 'right' if x > 0.25 else 'center'}_"
        f"{'front' if y > 0.2 else 'back' if y < -0.2 else 'middle'}"
        for x, y, _ in coordinates
    ).items()))

nonmanifold_positions = [(edge.verts[0].co + edge.verts[1].co) * 0.5
                         for edge in bm.edges if len(edge.link_faces) != 2]

report = {
    "source": str(candidate),
    "vertices": len(mesh.vertices),
    "faces": len(mesh.polygons),
    "triangles": len(mesh.loop_triangles),
    "bounds_min": [round(v, 5) for v in minimum],
    "bounds_max": [round(v, 5) for v in maximum],
    "face_sizes": dict(face_sizes),
    "edge_face_count": dict(edge_faces),
    "valence_histogram": dict(vertex_valence),
    "connected_component_vertices": sorted(connected, reverse=True)[:12],
    "component_count": len(connected),
    "area_min": min(areas),
    "area_max": max(areas),
    "triangle_quality_p50": sorted(qualities)[len(qualities) // 2],
    "triangle_quality_p90": sorted(qualities)[int(len(qualities) * 0.90)],
    "triangle_quality_p99": sorted(qualities)[int(len(qualities) * 0.99)],
    "triangle_quality_gt_5": sum(q > 5 for q in qualities),
    "triangle_quality_gt_10": sum(q > 10 for q in qualities),
    "degenerate_area_lt_1e_7": sum(a < 1e-7 for a in areas),
    "thin_faces_gt_10_by_height": region_histogram(thin_faces),
    "thin_faces_gt_10_by_quadrant": quadrant_histogram(thin_faces),
    "nonmanifold_edges_by_height": region_histogram(nonmanifold_positions),
    "nonmanifold_edges_by_quadrant": quadrant_histogram(nonmanifold_positions),
    "nonmanifold_edge_samples": [list(map(lambda x: round(x, 4), p)) for p in nonmanifold_positions[:20]],
}
(output / "topology_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
bm.free()
