import bpy
import json
from pathlib import Path
from collections import defaultdict
OUT = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT / "wood-mask-source.blend"))
obj = bpy.data.objects["WoodMaskSource_fence"]
mesh = obj.data
uv = mesh.uv_layers.active.data
polygons = []
for poly in mesh.polygons:
    verts = [tuple(mesh.vertices[v].co) for v in poly.vertices]
    polygons.append({"index": poly.index, "centre": list(poly.center), "normal": list(poly.normal), "area": poly.area, "verts": verts, "uv": [list(uv[i].uv) for i in poly.loop_indices]})
(OUT / "fence-geometry-inspection.json").write_text(json.dumps(polygons), encoding="utf-8")
parents = list(range(len(mesh.polygons)))
def find(i):
    while parents[i] != i:
        parents[i] = parents[parents[i]]
        i = parents[i]
    return i
edges = {}
for poly in mesh.polygons:
    coordinates = [tuple(round(float(c), 6) for c in uv[i].uv) for i in poly.loop_indices]
    for a, b in zip(coordinates, coordinates[1:] + coordinates[:1]):
        key = tuple(sorted((a, b)))
        if key in edges:
            parents[find(poly.index)] = find(edges[key])
        else:
            edges[key] = poly.index
islands = defaultdict(list)
for poly in mesh.polygons:
    islands[find(poly.index)].append(poly.index)
island_report = []
for indices in islands.values():
    positions = [mesh.polygons[i].center for i in indices]
    island_report.append({"first": indices[0], "indices": indices, "faces": len(indices), "area": sum(mesh.polygons[i].area for i in indices), "min": [min(p[i] for p in positions) for i in range(3)], "max": [max(p[i] for p in positions) for i in range(3)]})
(OUT / "fence-uv-islands.json").write_text(json.dumps(sorted(island_report, key=lambda i: i["area"], reverse=True), indent=2), encoding="utf-8")
print("FENCE_GEOMETRY " + str(len(polygons)))
