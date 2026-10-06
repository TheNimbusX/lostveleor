"""Уточнение существующей бочки без переимпорта или пересборки других готовых масок."""
import bpy
import json
from pathlib import Path
import importlib.util

OUT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("wood_palette_masks", OUT / "wood_palette_masks.py")
production = importlib.util.module_from_spec(spec)
spec.loader.exec_module(production)
bpy.ops.wm.open_mainfile(filepath=str(OUT / "wood-palette-source.blend"))
report = json.loads((OUT / "wood-palette-production.json").read_text(encoding="utf-8"))
entry = report["barrel"]
selected_count, selected_area = 0, 0.0
objects = [bpy.data.objects[name] for name in set(island["object"] for island in entry["islands"])]
for island in entry["islands"]:
    obj = bpy.data.objects[island["object"]]
    mesh = obj.data
    rgb = island["rgb"]
    selected_island = island["uvArea"] >= .000015 and island["woodColorRatio"] >= .35 and rgb[0] / max(rgb[1], .001) > 1.75
    island["selectedFaces"] = 0
    for index in island["indices"]:
        polygon = mesh.polygons[index]
        selected = selected_island and not production.protected_geometry("barrel", polygon, mesh)
        polygon.material_index = 1 if selected else 0
        polygon.select = selected
        if selected:
            island["selectedFaces"] += 1
            selected_count += 1
            selected_area += polygon.area
bpy.ops.object.select_all(action="DESELECT")
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.render.bake.margin = 0
scene.render.bake.use_clear = True
bpy.ops.object.bake(type="EMIT")
destination = bpy.data.images["barrel_wood_mask"]
destination.filepath_raw = entry["mask"]
destination.file_format = "PNG"
destination.save()
destination.pack()
entry["selectedFaces"] = selected_count
entry["selectedSurfaceArea"] = selected_area
entry["refinement"] = "Existing UV islands kept whole across painted highlights; both metal hoop regions protected geometrically"
(OUT / "wood-palette-production.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "wood-palette-source.blend"))
print("BARREL_REFINEMENT wood=" + str(selected_count) + " area=" + str(selected_area / entry["surfaceArea"]))
