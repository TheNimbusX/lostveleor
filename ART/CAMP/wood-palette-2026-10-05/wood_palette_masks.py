"""Дерево лагеря: отдельные FBX-копии, геометрия/UV неизменны; маски запекаются Blender EMIT."""
import bpy
import json
import sys
import math
from array import array
from collections import defaultdict
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
ASSETS = ROOT / "razlom/Assets"
MASK_OUT = ASSETS / "Resources/Environment/Camp/WoodPalette/Textures"
SOURCES = {
    "barrel": "wooden+barrel+3d+model",
    "merchant": "medieval+market+stall+3d+model",
    "merchant_wooden": "wooden+market+stall+3d+model",
    "arch": "wooden+archway+3d+model",
}
EXCLUDED_ISLANDS = {kind: set() for kind in SOURCES}


def islands(mesh):
    parents = list(range(len(mesh.polygons)))
    def find(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index
    uv = mesh.uv_layers.active.data
    edges = {}
    for polygon in mesh.polygons:
        points = [tuple(round(float(c), 6) for c in uv[i].uv) for i in polygon.loop_indices]
        for a, b in zip(points, points[1:] + points[:1]):
            key = tuple(sorted((a, b)))
            if key in edges:
                parents[find(polygon.index)] = find(edges[key])
            else:
                edges[key] = polygon.index
    groups = defaultdict(list)
    for polygon in mesh.polygons:
        groups[find(polygon.index)].append(polygon.index)
    return list(groups.values())


def is_wood_color(rgb, kind):
    r, g, b = rgb
    # Цвет — дополнительный фильтр, а не замена геометрическому выделению дерева.
    ratio = r / max(g, .001)
    if kind == "barrel":
        return r - g > .045 and g - b > .025 and 1.75 < ratio < 3.3 and 1.2 < g / max(b, .001) < 4.3
    if kind == "arch":
        return r - g > .035 and g - b > .02 and 1.32 < ratio < 4.8 and 1.16 < g / max(b, .001) < 6.5
    return r - g > .055 and g - b > .045 and 1.32 < ratio < 1.95 and 1.13 < g / max(b, .001) < 2.8


def protected_geometry(kind, polygon, mesh):
    centre = polygon.center
    if kind == "barrel":
        # Две металлические ленты с заклёпками; древесина за ними не видна и не нуждается в коррекции.
        return .155 < centre.y < .275 or .745 < centre.y < .87
    if kind == "arch":
        if centre.y < .205 or centre.y > .633:
            return True
        # Единственный подвесной фонарь с цепью внутри правой стороны проёма.
        return .08 < centre.x < .21 and centre.y < .505
    return False


def emission_material(name, selected, destination):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (float(selected), 0, 0, 1)
    material.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    target = nodes.new("ShaderNodeTexImage")
    target.image = destination
    nodes.active = target
    return material


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    report = {}
    MASK_OUT.mkdir(parents=True, exist_ok=True)
    for kind, base in SOURCES.items():
        model = ASSETS / "Resources/Environment/Camp" / (base + ".fbx")
        texture = ASSETS / "Resources/Environment/Camp" / (base + ".fbm") / (base + "_basecolor.jpg")
        old_objects = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(model))
        objects = [obj for obj in bpy.data.objects if obj not in old_objects and obj.type == "MESH"]
        source = bpy.data.images.load(str(texture), check_existing=True)
        width, height = source.size
        pixels = array("f", [0.0]) * (width * height * 4)
        source.pixels.foreach_get(pixels)
        def sample(u, v):
            x = max(0, min(width - 1, int(u * width)))
            y = max(0, min(height - 1, int(v * height)))
            start = (y * width + x) * 4
            return pixels[start:start + 3]
        resolution = 1024 if kind == "barrel" else 2048
        destination = bpy.data.images.new(kind + "_wood_mask", width=resolution, height=resolution, alpha=False)
        destination.colorspace_settings.name = "Non-Color"
        selected_material = emission_material("MASK_" + kind + "_wood", True, destination)
        protected_material = emission_material("MASK_" + kind + "_protected", False, destination)
        all_islands = []
        total, selected_count, total_area, selected_area = 0, 0, 0.0, 0.0
        for object_index, obj in enumerate(objects):
            obj.name = "WoodPaletteSource_" + kind + ("_" + str(object_index) if object_index else "")
            mesh = obj.data
            uv = mesh.uv_layers.active.data
            mesh.materials.clear()
            mesh.materials.append(protected_material)
            mesh.materials.append(selected_material)
            for indices in islands(mesh):
                coordinates, uv_coordinates, samples = [], [], []
                uv_area = 0.0
                for index in indices:
                    polygon = mesh.polygons[index]
                    coordinates.append(polygon.center.copy())
                    points = [uv[i].uv.copy() for i in polygon.loop_indices]
                    uv_coordinates.extend(points)
                    centre = sum(points, start=Vector((0, 0))) / len(points)
                    samples.append(sample(*centre))
                    for i in range(1, len(points) - 1):
                        uv_area += abs((points[i] - points[0]).cross(points[i + 1] - points[0])) * .5
                mean_rgb = [sum(color[c] for color in samples) / len(samples) for c in range(3)]
                island_area = sum(mesh.polygons[i].area for i in indices)
                low = [min(p[i] for p in coordinates) for i in range(3)]
                high = [max(p[i] for p in coordinates) for i in range(3)]
                ratio = sum(is_wood_color(color, kind) for color in samples) / len(samples)
                mean_normal = sum((mesh.polygons[i].normal * mesh.polygons[i].area for i in indices), start=Vector()) / max(island_area, .000001)
                planarity = mean_normal.length
                # Мелкие детали торговли и крепежа оставлены нетронутыми; ткань/камень отсеиваются отдельно.
                island_selected = uv_area >= (.0004 if kind in {"merchant", "merchant_wooden"} else .00018) and ratio >= .72
                if kind == "barrel":
                    # Цветная живопись внутри одной клёпки не должна разрывать маску на треугольники.
                    island_selected = uv_area >= .000015 and ratio >= .35 and mean_rgb[0] / max(mean_rgb[1], .001) > 1.75
                if kind in {"merchant", "merchant_wooden"} and planarity < .93:
                    island_selected = False
                if indices[0] in EXCLUDED_ISLANDS[kind]:
                    island_selected = False
                island_selected_count = 0
                for index in indices:
                    polygon = mesh.polygons[index]
                    total += 1
                    total_area += polygon.area
                    points = [uv[i].uv.copy() for i in polygon.loop_indices]
                    centre = sum(points, start=Vector((0, 0))) / len(points)
                    colors = [sample(*centre)] + [sample(*(centre.lerp(point, .6))) for point in points]
                    selected = island_selected and all(is_wood_color(color, kind) for color in colors) and not protected_geometry(kind, polygon, mesh)
                    if kind == "barrel":
                        selected = island_selected and not protected_geometry(kind, polygon, mesh)
                    polygon.material_index = 1 if selected else 0
                    polygon.select = selected
                    if selected:
                        selected_count += 1
                        selected_area += polygon.area
                        island_selected_count += 1
                all_islands.append({"object": obj.name, "first": indices[0], "faces": len(indices), "selectedFaces": island_selected_count,
                                    "uvArea": uv_area, "surfaceArea": island_area, "rgb": mean_rgb, "woodColorRatio": ratio,
                                    "planarity": planarity,
                                    "min": low, "max": high, "uvMin": [min(p[i] for p in uv_coordinates) for i in range(2)],
                                    "uvMax": [max(p[i] for p in uv_coordinates) for i in range(2)], "indices": indices})
        if "--bake" in sys.argv:
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
            destination.filepath_raw = str(MASK_OUT / (kind + "_wood_mask.png"))
            destination.file_format = "PNG"
            destination.save()
            destination.pack()
        report[kind] = {"model": str(model), "texture": str(texture), "mask": str(MASK_OUT / (kind + "_wood_mask.png")),
                        "resolution": resolution, "totalFaces": total, "selectedFaces": selected_count,
                        "surfaceArea": total_area, "selectedSurfaceArea": selected_area,
                        "channels": {"R": "selected structural timber", "G": "zero", "B": "zero"},
                        "method": "UV island and geometry selection; native Blender EMIT bake; original FBX/JPG unchanged",
                        "islands": sorted(all_islands, key=lambda entry: entry["uvArea"], reverse=True)}
        print("WOOD_PALETTE " + kind + " faces=" + str(total) + " wood=" + str(selected_count) + " islands=" + str(len(all_islands)))
        del pixels
    (OUT / "wood-palette-production.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "wood-palette-source.blend"))


if __name__ == "__main__":
    main()
