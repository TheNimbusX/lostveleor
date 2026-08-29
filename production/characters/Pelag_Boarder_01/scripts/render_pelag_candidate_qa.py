import os
import sys

import bpy
from mathutils import Matrix
from mathutils import Vector


def output_dir():
    if "--" not in sys.argv:
        raise RuntimeError("Expected output directory after --")
    path = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
    os.makedirs(path, exist_ok=True)
    return path


def material(name, color):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.84
    return mat


def area(name, location, energy, size, color):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector((0.0, 0.0, 1.0)) - obj.location).to_track_quat("-Z", "Y").to_euler()
    return obj


def configure_freestyle(scene):
    scene.render.use_freestyle = True
    scene.render.line_thickness = 1.15
    lineset = scene.view_layers[0].freestyle_settings.linesets[0]
    lineset.select_silhouette = True
    lineset.select_contour = True
    lineset.select_external_contour = True
    lineset.select_crease = False
    lineset.select_border = False
    lineset.select_material_boundary = False
    lineset.select_edge_mark = False


def reset_pose(rig):
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
        for constraint in bone.constraints:
            constraint.influence = 0.0
    bpy.context.view_layer.update()


def move_control(rig, name, delta):
    bone = rig.pose.bones[name]
    bone.location = Vector(delta)


def enable_constraint(rig, bone_name, constraint_name):
    rig.pose.bones[bone_name].constraints[constraint_name].influence = 1.0


def render(scene, camera, path, location, target, size, background, floor_mat):
    camera.location = location
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.line_thickness = 1.15 if size >= 512 else 0.65
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (*background, 1.0)
    bpy.data.objects["QA_Floor"].data.materials[0] = floor_mat
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    out = output_dir()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.image_settings.file_format = "PNG"
    scene.render.fps = 30
    scene.view_settings.look = "AgX - Medium High Contrast"
    configure_freestyle(scene)
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.72

    dark_floor = material("MAT_QA_DarkFloor", (0.035, 0.045, 0.060))
    light_floor = material("MAT_QA_LightFloor", (0.64, 0.60, 0.55))
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=1.25, depth=0.05, location=(0.0, 0.0, -0.027))
    floor = bpy.context.object
    floor.name = "QA_Floor"
    floor.data.materials.append(light_floor)

    camera_data = bpy.data.cameras.new("QA_Camera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.25
    camera = bpy.data.objects.new("QA_Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    lights = [
        area("QA_Key", (3.5, -4.0, 4.8), 1100, 4.0, (1.0, 0.82, 0.68)),
        area("QA_Fill", (-3.0, -2.4, 3.0), 680, 3.5, (0.55, 0.76, 1.0)),
        area("QA_Rim", (2.0, 3.2, 4.0), 850, 3.0, (1.0, 0.38, 0.28)),
    ]
    target = Vector((0.0, 0.0, 0.94))
    render(scene, camera, os.path.join(out, "front_768.png"), Vector((0.0, -4.2, 1.05)), target, 768, (0.64, 0.60, 0.55), light_floor)
    render(scene, camera, os.path.join(out, "iso_768.png"), Vector((3.2, -3.2, 2.65)), target, 768, (0.64, 0.60, 0.55), light_floor)
    render(scene, camera, os.path.join(out, "gameplay_dark_120.png"), Vector((3.2, -3.2, 2.65)), target, 120, (0.022, 0.027, 0.038), dark_floor)
    render(scene, camera, os.path.join(out, "gameplay_dark_80.png"), Vector((3.2, -3.2, 2.65)), target, 80, (0.022, 0.027, 0.038), dark_floor)
    render(scene, camera, os.path.join(out, "gameplay_light_120.png"), Vector((3.2, -3.2, 2.65)), target, 120, (0.64, 0.60, 0.55), light_floor)
    render(scene, camera, os.path.join(out, "gameplay_light_80.png"), Vector((3.2, -3.2, 2.65)), target, 80, (0.64, 0.60, 0.55), light_floor)

    # Static deformation QA only. No Actions, keyframes or animation clips are created.
    rig = bpy.data.objects["Pelag_Boarder_01_Rig"]
    reset_pose(rig)
    for name in ("LeftUpperArm", "RightUpperArm"):
        rig.pose.bones[name].rotation_mode = "XYZ"
        rig.pose.bones[name].rotation_euler.x = 1.22173
    bpy.context.view_layer.update()
    render(scene, camera, os.path.join(out, "deform_arms_up.png"), Vector((3.2, -3.2, 2.65)), target, 768, (0.64, 0.60, 0.55), light_floor)

    reset_pose(rig)
    rig.pose.bones["Hips"].location.z = -0.14
    for name in ("LeftUpperLeg", "RightUpperLeg"):
        rig.pose.bones[name].rotation_mode = "XYZ"
        rig.pose.bones[name].rotation_euler.x = -0.523599
    for name in ("LeftLowerLeg", "RightLowerLeg"):
        rig.pose.bones[name].rotation_mode = "XYZ"
        rig.pose.bones[name].rotation_euler.x = 1.047198
    bpy.context.view_layer.update()
    render(scene, camera, os.path.join(out, "deform_squat.png"), Vector((3.2, -3.2, 2.65)), target, 768, (0.64, 0.60, 0.55), light_floor)

    reset_pose(rig)
    rig.pose.bones["Hips"].location.z = -0.06
    rig.pose.bones["LeftUpperLeg"].rotation_mode = "XYZ"
    rig.pose.bones["RightUpperLeg"].rotation_mode = "XYZ"
    rig.pose.bones["LeftUpperLeg"].rotation_euler.z = -0.436332
    rig.pose.bones["RightUpperLeg"].rotation_euler.z = 0.436332
    bpy.context.view_layer.update()
    render(scene, camera, os.path.join(out, "deform_wide_step.png"), Vector((3.2, -3.2, 2.65)), target, 768, (0.64, 0.60, 0.55), light_floor)
    reset_pose(rig)

    body = bpy.data.objects.get("SKM_Pelag_ProductionBody")
    uv_ok = all(obj.data.uv_layers for obj in bpy.data.objects if obj.type == "MESH" and not obj.name.startswith("QA_"))
    shapes = {}
    if body and body.data.shape_keys:
        basis = body.data.shape_keys.key_blocks["Basis"]
        for name in ("Open", "Grip", "Fist"):
            key = body.data.shape_keys.key_blocks[name]
            shapes[name] = max((key.data[i].co - basis.data[i].co).length for i in range(len(body.data.vertices)))
    print(f"UV_ALL_MESHES={uv_ok}")
    print("SHAPE_MAX_DISPLACEMENTS=" + ",".join(f"{name}:{value:.6f}" for name, value in shapes.items()))

    for obj in [floor, camera, *lights]:
        bpy.data.objects.remove(obj, do_unlink=True)


if __name__ == "__main__":
    main()
