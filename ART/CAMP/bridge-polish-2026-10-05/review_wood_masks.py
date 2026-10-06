"""Проверочный рендер: оранжевое дерево, исходная фактура на защищённых поверхностях."""
import bpy
from mathutils import Vector
from pathlib import Path
import importlib.util

OUT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("wood_masks", OUT / "wood_masks.py")
production = importlib.util.module_from_spec(spec)
spec.loader.exec_module(production)
bpy.ops.wm.open_mainfile(filepath=str(OUT / "wood-mask-source.blend"))
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.render.resolution_x = 1000
scene.render.resolution_y = 820
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (.08, .08, .08)
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
camera_data = bpy.data.cameras.new("WoodMaskReviewCamera")
camera = bpy.data.objects.new("WoodMaskReviewCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
sun_data = bpy.data.lights.new("WoodMaskReviewSun", "SUN")
sun = bpy.data.objects.new("WoodMaskReviewSun", sun_data)
scene.collection.objects.link(sun)
sun_data.energy = 2.2

for kind, (_, texture) in production.SOURCES.items():
    objects = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name.startswith("WoodMaskSource_" + kind)]
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            obj.hide_render = obj not in objects
    source = bpy.data.images.load(str(production.ASSETS / texture), check_existing=True)
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
    overlay.blend_type = "MIX"
    overlay.inputs[2].default_value = (.68, .145, .027, 1)
    material.node_tree.links.new(albedo.outputs["Color"], overlay.inputs[1])
    material.node_tree.links.new(separate.outputs["Red"], overlay.inputs[0])
    damp_overlay = nodes.new("ShaderNodeMixRGB")
    damp_overlay.inputs[2].default_value = (.025, .09, .30, 1)
    material.node_tree.links.new(overlay.outputs[0], damp_overlay.inputs[1])
    material.node_tree.links.new(separate.outputs["Green"], damp_overlay.inputs[0])
    material.node_tree.links.new(damp_overlay.outputs[0], surface.inputs["Base Color"])
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
    camera.location = centre + Vector((.9, -1.35, 1.1)) * extent
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = extent * 1.45
    sun.rotation_euler = camera.rotation_euler
    scene.render.filepath = str(OUT / (kind + "-wood-mask-review.png"))
    bpy.ops.render.render(write_still=True)
    print("WOOD_MASK_REVIEW " + scene.render.filepath)
