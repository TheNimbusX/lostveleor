"""Изолированный Blender: исходная геометрия и UV остаются неизменными."""
import bpy
import json
import sys
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
ASSETS = ROOT / "razlom/Assets"
SOURCES = {
    "bridge": ("Art/Meadow/Creating/wooden bridge/tripo_convert_85437eb2-e656-47fa-9e54-6129ed6e991c.fbx", "Art/Meadow/Creating/wooden bridge/tripo_convert_85437eb2-e656-47fa-9e54-6129ed6e991c.fbm/tripo_image_85437eb2-e656-47fa-9e54-6129ed6e991c_0_0.jpg"),
    "fence": ("Resources/Environment/Camp/wooden+fence+3d+model.fbx", "Resources/Environment/Camp/wooden+fence+3d+model.fbm/wooden+fence+3d+model_basecolor.jpg"),
    "bench": ("Resources/Environment/Camp/wooden+bench+3d+model.fbx", "Resources/Environment/Camp/wooden+bench+3d+model.fbm/wooden+bench+3d+model_basecolor.jpg"),
    "crate": ("Resources/Environment/Camp/wooden+crate+3d+model.fbx", "Resources/Environment/Camp/wooden+crate+3d+model.fbm/wooden+crate+3d+model_basecolor.jpg"),
}
MASK_OUT = ASSETS / "Resources/Environment/Camp/BridgePolish/Textures"


def emission_material(name, value, destination, wet=False):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (value, 0, 0, 1)
    if wet:
        coordinates = nodes.new("ShaderNodeTexCoord")
        separate = nodes.new("ShaderNodeSeparateXYZ")
        ramp = nodes.new("ShaderNodeMath")
        ramp.operation = "SUBTRACT"
        ramp.inputs[0].default_value = .28
        scale = nodes.new("ShaderNodeMath")
        scale.operation = "DIVIDE"
        scale.inputs[1].default_value = .16
        scale.use_clamp = True
        combine = nodes.new("ShaderNodeCombineXYZ")
        combine.inputs[0].default_value = 1
        material.node_tree.links.new(coordinates.outputs["Generated"], separate.inputs[0])
        material.node_tree.links.new(separate.outputs["Z"], ramp.inputs[1])
        material.node_tree.links.new(ramp.outputs[0], scale.inputs[0])
        material.node_tree.links.new(scale.outputs[0], combine.inputs[1])
        material.node_tree.links.new(combine.outputs[0], emission.inputs["Color"])
    material.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    target = nodes.new("ShaderNodeTexImage")
    target.image = destination
    nodes.active = target
    return material


def uv_island_areas(mesh):
    parents = list(range(len(mesh.polygons)))
    def find(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i
    edges = {}
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        coordinates = [tuple(round(float(c), 6) for c in uv[i].uv) for i in poly.loop_indices]
        for a, b in zip(coordinates, coordinates[1:] + coordinates[:1]):
            key = tuple(sorted((a, b)))
            if key in edges:
                parents[find(poly.index)] = find(edges[key])
            else:
                edges[key] = poly.index
    areas = defaultdict(float)
    for poly in mesh.polygons:
        areas[find(poly.index)] += poly.area
    return {poly.index: areas[find(poly.index)] for poly in mesh.polygons}


def bake_mask(kind, objects, sample):
    MASK_OUT.mkdir(parents=True, exist_ok=True)
    destination = bpy.data.images.new(kind + "_wood_mask", width=1024, height=1024, alpha=False)
    destination.colorspace_settings.name = "Non-Color"
    wood = emission_material("MASK_" + kind + "_wood", 1, destination)
    protected = emission_material("MASK_" + kind + "_protected", 0, destination)
    wet_wood = emission_material("MASK_" + kind + "_wet_support_wood", 1, destination, wet=kind == "bridge")
    count, total, area = 0, 0, 0.0
    # Эти отдельные оболочки — обмотки стоек, верхние верёвочные перила и фрагменты крепежа.
    bridge_rope_shells = {175, 307, 573, 864, 859, 1065, 571, 1100, 842, 3655, 1759, 2668, 3792, 4473, 1309, 2229, 2464}
    bridge_support_shells = {343, 837, 1117, 236, 619, 1010}
    for obj in objects:
        mesh = obj.data
        uv = mesh.uv_layers.active.data
        mesh.materials.clear()
        mesh.materials.append(protected)
        mesh.materials.append(wood)
        mesh.materials.append(wet_wood)
        island_areas = uv_island_areas(mesh) if kind == "fence" else {}
        shell_for_face = {}
        if kind == "bridge":
            for indices in topology_components(mesh):
                for index in indices:
                    shell_for_face[index] = indices[0]
        for polygon in mesh.polygons:
            total += 1
            positions = [uv[loop].uv.copy() for loop in polygon.loop_indices]
            centre = sum(positions, start=__import__('mathutils').Vector((0, 0))) / len(positions)
            # Выбор учитывает геометрию/UV; нейтральные и светлые волокнистые острова исключаются.
            samples = [sample(*centre)] + [sample(*(centre.lerp(pos, 0.5))) for pos in positions]
            warm = sum(1 for r, g, b in samples if r - g > 0.055 and g - b > 0.045 and r > g * 1.12)
            if kind == "bench":
                selected = True
            elif kind == "bridge":
                selected = shell_for_face[polygon.index] not in bridge_rope_shells and warm >= len(samples) - 1
            elif kind == "fence":
                # Обмотки соединены с деревом, но имеют отдельные мелкие UV-острова; это подтверждено рендером.
                selected = island_areas[polygon.index] > .0022 and warm == len(samples)
            else:
                selected = warm == len(samples)
                # Угловые металлические накладки ящика исключены геометрически, включая ржавые тёплые пиксели.
                corner_hardware = any(abs(mesh.vertices[v].co.x) > .28 and abs(mesh.vertices[v].co.z) > .28
                                      and (mesh.vertices[v].co.y < .22 or mesh.vertices[v].co.y > .64)
                                      for v in polygon.vertices)
                selected = selected and not corner_hardware
            polygon.material_index = 1 if selected else 0
            if selected and kind == "bridge" and shell_for_face[polygon.index] in bridge_support_shells:
                polygon.material_index = 2
            polygon.select = selected
            if selected:
                count += 1
                area += polygon.area
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
    return {"selectedFaces": count, "totalFaces": total, "selectedSurfaceArea": area, "mask": str(destination.filepath_raw), "resolution": 1024, "channels": {"R": "wood selection", "G": "lowest bridge support wood dampness; all other assets zero", "B": "unused"}, "method": "UV polygon/shell selection, EMIT bake; original meshes/textures untouched"}


def topology_components(mesh):
    parents = list(range(len(mesh.vertices)))
    def find(v):
        while parents[v] != v:
            parents[v] = parents[parents[v]]
            v = parents[v]
        return v
    def join(a, b):
        a, b = find(a), find(b)
        if a != b:
            parents[b] = a
    # Швы FBX разрывают индексы вершин; совпадающая позиция нужна только для анализа.
    positions = {}
    for vertex in mesh.vertices:
        key = tuple(round(float(c), 5) for c in vertex.co)
        if key in positions:
            join(vertex.index, positions[key])
        else:
            positions[key] = vertex.index
    for edge in mesh.edges:
        join(edge.vertices[0], edge.vertices[1])
    components = defaultdict(list)
    for polygon in mesh.polygons:
        components[find(polygon.vertices[0])].append(polygon.index)
    return sorted(components.values(), key=len, reverse=True)


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    report, baked = {}, {}
    for kind, (model, texture) in SOURCES.items():
        old = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(ASSETS / model))
        objects = [obj for obj in bpy.data.objects if obj not in old and obj.type == "MESH"]
        image = bpy.data.images.load(str(ASSETS / texture), check_existing=True)
        pixels = list(image.pixels)
        width, height = image.size
        def sample(u, v):
            x = max(0, min(width - 1, int(u * width)))
            y = max(0, min(height - 1, int(v * height)))
            return pixels[(y * width + x) * 4:(y * width + x) * 4 + 3]
        entries = []
        for obj in objects:
            mesh = obj.data
            uv = mesh.uv_layers.active.data
            components = topology_components(mesh)
            summaries = []
            for indices in components:
                colors, positions = [], []
                for index in indices:
                    polygon = mesh.polygons[index]
                    co = sum((mesh.vertices[v].co for v in polygon.vertices), start=__import__('mathutils').Vector()) / len(polygon.vertices)
                    positions.append(tuple(co))
                    uv_centre = sum((uv[loop].uv for loop in polygon.loop_indices), start=__import__('mathutils').Vector((0, 0))) / len(polygon.loop_indices)
                    colors.append(sample(*uv_centre))
                summaries.append({"faces": len(indices), "first": indices[0], "rgb": [sum(c[i] for c in colors) / len(colors) for i in range(3)], "min": [min(p[i] for p in positions) for i in range(3)], "max": [max(p[i] for p in positions) for i in range(3)]})
            obj.name = "WoodMaskSource_" + kind
            entries.append({"object": obj.name, "vertices": len(mesh.vertices), "faces": len(mesh.polygons), "components": summaries})
        report[kind] = entries
        if "--bake" in sys.argv:
            baked[kind] = bake_mask(kind, objects, sample)
    (OUT / "wood-topology-inspection.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    if baked:
        (OUT / "wood-mask-production.json").write_text(json.dumps(baked, indent=2), encoding="utf-8")
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "wood-mask-source.blend"))
    print("WOOD_TOPOLOGY " + json.dumps({kind: [{"faces": entry["faces"], "components": len(entry["components"])} for entry in entries] for kind, entries in report.items()}))


if __name__ == "__main__":
    main()
