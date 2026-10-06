"""Контрольный рендер маски на исходном FBX: оранжевое выделение, остальной атлас без правок."""
import bpy
from mathutils import Vector
from pathlib import Path
import importlib.util
import sys

OUT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("wood_palette_masks", OUT / "wood_palette_masks.py")
production = importlib.util.module_from_spec(spec)
spec.loader.exec_module(production)
bpy.ops.wm.open_mainfile(filepath=str(OUT / "wood-palette-source.blend"))
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.render.resolution_x = 1100
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (.08, .08, .08)
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
camera_data = bpy.data.cameras.new("WoodPaletteReviewCamera")
camera = bpy.data.objects.new("WoodPaletteReviewCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
sun_data = bpy.data.lights.new("WoodPaletteReviewSun", "SUN")
sun = bpy.data.objects.new("WoodPaletteReviewSun", sun_data)
scene.collection.objects.link(sun)
sun_data.energy = 2.2

for kind, base in production.SOURCES.items():
    if "--barrel-only" in sys.argv and kind != "barrel":
        continue
    objects = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name.startswith("WoodPaletteSource_" + kind)]
    if kind == "merchant":
        objects = [obj for obj in objects if not obj.name.startswith("WoodPaletteSource_merchant_wooden")]
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            obj.hide_render = obj not in objects
    source = bpy.data.images.load(str(production.ASSETS / "Resources/Environment/Camp" / (base + ".fbm") / (base + "_basecolor.jpg")), check_existing=True)
    material = bpy.data.materials.new("REVIEW_" + kind)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    surface = nodes.get("Principled BSDF")
    surface.inputs["Roughness"].default_value = .95
    albedo = nodes.new("ShaderNodeTexImage")
    albedo.image = source
    mask = nodes.new("ShaderNodeTexImage")
    mask.image = bpy.data.images.get(kind + "_wood_mask")
    separate = nodes.new("ShaderNodeSeparateColor")
    material.node_tree.links.new(mask.outputs["Color"], separate.inputs[0])
    overlay = nodes.new("ShaderNodeMixRGB")
    overlay.inputs[2].default_value = (.68, .145, .027, 1)
    material.node_tree.links.new(albedo.outputs["Color"], overlay.inputs[1])
    material.node_tree.links.new(separate.outputs["Red"], overlay.inputs[0])
    material.node_tree.links.new(overlay.outputs[0], surface.inputs["Base Color"])
    for obj in objects:
        obj.data.materials.clear()
        obj.data.materials.append(material)
        for polygon in obj.data.polygons:
            polygon.material_index = 0
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = Vector([min(point[i] for point in points) for i in range(3)])
    high = Vector([max(point[i] for point in points) for i in range(3)])
    centre = (low + high) * .5
    extent = max(high - low)
    for side, offset in [("front", Vector((.9, -1.35, .85))), ("back", Vector((-.9, 1.35, .85)))]:
        camera.location = centre + offset * extent
        camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera_data.ortho_scale = extent * 1.48
        sun.rotation_euler = camera.rotation_euler
        scene.render.filepath = str(OUT / (kind + "-wood-mask-" + side + ".png"))
        bpy.ops.render.render(write_still=True)
        print("WOOD_PALETTE_REVIEW " + scene.render.filepath)
