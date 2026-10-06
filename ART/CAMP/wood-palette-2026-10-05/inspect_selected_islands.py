import bpy
import json
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

OUT = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT / "wood-palette-source.blend"))
report = json.loads((OUT / "wood-palette-production.json").read_text())
scene = bpy.context.scene
scene.render.resolution_x, scene.render.resolution_y = 1100, 900
camera_data = bpy.data.cameras.new("IslandInspectionCamera")
camera = bpy.data.objects.new("IslandInspectionCamera", camera_data)
scene.collection.objects.link(camera)
camera_data.type = "ORTHO"
screen_report = {}
for kind, entry in report.items():
    objects = [bpy.data.objects[name] for name in set(island["object"] for island in entry["islands"])]
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = Vector([min(point[i] for point in points) for i in range(3)])
    high = Vector([max(point[i] for point in points) for i in range(3)])
    centre = (low + high) * .5
    extent = max(high - low)
    camera.location = centre + Vector((.9, -1.35, .85)) * extent
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = extent * 1.48
    bpy.context.view_layer.update()
    screen_report[kind] = []
    for island in entry["islands"]:
        if not island["selectedFaces"]:
            continue
        obj = bpy.data.objects[island["object"]]
        mesh = obj.data
        selected = [mesh.polygons[i] for i in island["indices"] if mesh.polygons[i].material_index == 1]
        surface_area = sum(poly.area for poly in selected)
        point = sum((poly.center * poly.area for poly in selected), start=Vector()) / surface_area
        screen = world_to_camera_view(scene, camera, obj.matrix_world @ point)
        screen_report[kind].append({"first": island["first"], "screen": [screen.x * 1100, (1 - screen.y) * 900], "local": list(point), "faces": island["selectedFaces"], "planarity": island["planarity"], "rgb": island["rgb"]})
(OUT / "selected-island-projection.json").write_text(json.dumps(screen_report, indent=2))
target = Vector((482, 465))
print("MERCHANT_BOTTLE_CANDIDATES " + json.dumps(sorted(screen_report["merchant"], key=lambda i: (Vector(i["screen"]) - target).length)[:12]))
