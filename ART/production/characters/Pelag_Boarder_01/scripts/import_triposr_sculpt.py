import math
import os
import sys

import bpy
from mathutils import Vector


CHARACTER_HEIGHT_M = 1.91


def arg_after_double_dash():
    if "--" not in sys.argv:
        raise RuntimeError("Expected input OBJ and output directory after --")
    args = sys.argv[sys.argv.index("--") + 1 :]
    if len(args) not in (2, 3):
        raise RuntimeError("Usage: blender ... -- <input.obj> <output_dir> [texture.png]")
    texture_path = os.path.abspath(args[2]) if len(args) == 3 else None
    return os.path.abspath(args[0]), os.path.abspath(args[1]), texture_path


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def mesh_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    mins = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    maxs = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return mins, maxs


def normalize_character(obj):
    # TripoSR's exported reconstruction is X-up. Rotate its axes into Blender's
    # X-width / Y-depth / Z-up convention while preserving handedness.
    obj.rotation_euler = (0.0, math.radians(-90.0), math.radians(90.0))
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    mins, maxs = mesh_bounds(obj)
    height = maxs.z - mins.z
    if height <= 0.0001:
        raise RuntimeError("Imported sculpt has zero height")
    scale = CHARACTER_HEIGHT_M / height
    obj.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    mins, maxs = mesh_bounds(obj)
    center_x = (mins.x + maxs.x) * 0.5
    center_y = (mins.y + maxs.y) * 0.5
    obj.location += Vector((-center_x, -center_y, -mins.z))
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


def make_qa_material(texture_path=None):
    mat = bpy.data.materials.new("MAT_QA_CoralClay")
    mat.diffuse_color = (0.74, 0.18, 0.12, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.74, 0.18, 0.12, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.78
    bsdf.inputs["Metallic"].default_value = 0.0
    if texture_path:
        image = bpy.data.images.load(texture_path, check_existing=True)
        texture = mat.node_tree.nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.interpolation = "Linear"
        mat.node_tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def add_area_light(name, location, energy, size, color):
    light_data = bpy.data.lights.new(name=name, type="AREA")
    light_data.energy = energy
    light_data.shape = "DISK"
    light_data.size = size
    light_data.color = color
    light = bpy.data.objects.new(name, light_data)
    bpy.context.scene.collection.objects.link(light)
    light.location = location
    direction = Vector((0.0, 0.0, 0.95)) - light.location
    light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return light


def render_views(obj, output_dir):
    qa_dir = os.path.join(output_dir, "qa")
    os.makedirs(qa_dir, exist_ok=True)

    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.color = (0.035, 0.035, 0.045)
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.028, 0.032, 0.045, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.45

    camera_data = bpy.data.cameras.new("QA_Camera")
    camera = bpy.data.objects.new("QA_Camera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.35
    bpy.context.scene.camera = camera

    lights = [
        add_area_light("QA_Key", (3.3, -4.2, 4.6), 1100.0, 4.0, (1.0, 0.82, 0.72)),
        add_area_light("QA_Fill", (-3.2, -2.4, 2.7), 750.0, 3.0, (0.56, 0.72, 1.0)),
        add_area_light("QA_Rim", (2.2, 3.0, 3.7), 900.0, 3.0, (1.0, 0.35, 0.24)),
    ]

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 768
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.look = "AgX - Medium High Contrast"

    target = Vector((0.0, 0.0, 0.95))
    views = {
        "front": (0.0, -4.4, 1.05),
        "back": (0.0, 4.4, 1.05),
        "left": (-4.4, 0.0, 1.05),
        "right": (4.4, 0.0, 1.05),
        "iso_front": (3.4, -3.4, 2.75),
        "iso_back": (-3.4, 3.4, 2.75),
    }
    for name, location in views.items():
        camera.location = location
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(qa_dir, f"{name}.png")
        bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(camera, do_unlink=True)
    for light in lights:
        bpy.data.objects.remove(light, do_unlink=True)


def main():
    input_obj, output_dir, texture_path = arg_after_double_dash()
    os.makedirs(output_dir, exist_ok=True)
    clear_scene()

    bpy.ops.wm.obj_import(filepath=input_obj, forward_axis="NEGATIVE_Z", up_axis="Y")
    meshes = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("OBJ import produced no mesh")
    if len(meshes) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for mesh in meshes:
            mesh.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
    sculpt = bpy.context.view_layer.objects.active
    sculpt.name = "Pelag_Boarder_01_GeneratedSculpt_v1"
    sculpt.data.name = "Pelag_Boarder_01_GeneratedSculpt_v1_Mesh"
    normalize_character(sculpt)

    sculpt.data.materials.clear()
    sculpt.data.materials.append(make_qa_material(texture_path))
    for poly in sculpt.data.polygons:
        poly.use_smooth = True

    bpy.context.scene.render.fps = 30
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    render_views(sculpt, output_dir)

    sculpt["source"] = input_obj
    sculpt["qa_status"] = "UNREVIEWED"
    sculpt["target_height_m"] = CHARACTER_HEIGHT_M
    sculpt["vertex_count"] = len(sculpt.data.vertices)
    sculpt["triangle_count"] = sum(len(poly.vertices) - 2 for poly in sculpt.data.polygons)

    blend_path = os.path.join(output_dir, "Pelag_Boarder_01_GeneratedSculpt_v1.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print(f"SCULPT_BLEND={blend_path}")
    print(f"VERTICES={len(sculpt.data.vertices)}")
    print(f"TRIANGLES={sum(len(poly.vertices) - 2 for poly in sculpt.data.polygons)}")


if __name__ == "__main__":
    main()
