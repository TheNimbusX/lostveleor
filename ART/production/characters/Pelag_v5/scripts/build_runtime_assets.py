"""Build Pelag's canonical Mixamo body and a grip-normalized saber.

The body is exported from the same Mixamo delivery as the clips, eliminating
Humanoid retargeting between the 41-bone Tripo rig and the 65-bone Mixamo rig.
The saber is converted from its arbitrary generated orientation into the project
socket convention: grip center at the origin, blade along local +Y.
"""

import bpy
import numpy as np
import os
import sys
from mathutils import Matrix, Vector


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def select_only(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def build_character(source, destination):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=source, use_anim=True)

    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(
            f"Expected one armature and one mesh, got {len(armatures)} / {len(meshes)}"
        )

    armature = armatures[0]
    mesh = meshes[0]
    armature.animation_data_clear()
    for pose_bone in armature.pose.bones:
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    for obj in list(bpy.context.scene.objects):
        if obj not in (armature, mesh):
            bpy.data.objects.remove(obj, do_unlink=True)

    select_only([armature, mesh])
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        path_mode="AUTO",
    )
    print(
        f"CHARACTER={destination}|bones={len(armature.data.bones)}|"
        f"verts={len(mesh.data.vertices)}|polys={len(mesh.data.polygons)}"
    )


def build_saber(source, destination):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=source, use_anim=False)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"Expected one saber mesh, got {len(meshes)}")
    saber = meshes[0]

    world_points = np.array([tuple(saber.matrix_world @ v.co) for v in saber.data.vertices])
    mean = world_points.mean(axis=0)
    values, vectors = np.linalg.eigh(np.cov(world_points - mean, rowvar=False))
    order = np.argsort(values)
    blade_axis = vectors[:, order[-1]]
    width_axis = vectors[:, order[-2]]
    if blade_axis[2] < 0:
        blade_axis *= -1

    # Make a deterministic right-handed basis.  The thinnest PCA axis becomes
    # local Z (blade normal), the broad blade direction becomes local X.
    normal_axis = np.cross(width_axis, blade_axis)
    normal_axis /= np.linalg.norm(normal_axis)
    width_axis = np.cross(blade_axis, normal_axis)
    width_axis /= np.linalg.norm(width_axis)

    projection = (world_points - mean) @ blade_axis
    minimum = projection.min()
    maximum = projection.max()
    length = maximum - minimum

    # Profile audit places the wrapped grip at 17.7%-33.3% of total length.
    # Its midpoint is the physical contact point for the palm.
    grip_t = minimum + length * 0.255
    grip_mask = (projection >= minimum + length * 0.177) & (
        projection <= minimum + length * 0.333
    )
    grip_points = world_points[grip_mask]
    if len(grip_points) < 20:
        raise RuntimeError("Could not identify the saber grip profile")
    grip_mean = grip_points.mean(axis=0)
    grip_center = grip_mean + blade_axis * (grip_t - np.dot(grip_mean - mean, blade_axis))

    for vertex, point in zip(saber.data.vertices, world_points):
        relative = point - grip_center
        vertex.co = Vector(
            (
                float(np.dot(relative, width_axis)),
                float(np.dot(relative, blade_axis)),
                float(np.dot(relative, normal_axis)),
            )
        )
    saber.matrix_world = Matrix.Identity(4)
    saber.name = "Pelag_FantasySaber_GripPivot"
    saber.data.name = "Pelag_FantasySaber_GripPivot"

    select_only([saber])
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        object_types={"MESH"},
        bake_anim=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        path_mode="AUTO",
    )
    print(
        f"SABER={destination}|length={length:.6f}|grip={tuple(round(v, 6) for v in grip_center)}|"
        f"axis={tuple(round(v, 6) for v in blade_axis)}"
    )


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) != 4:
    raise RuntimeError("Expected character_source character_destination saber_source saber_destination")

build_character(os.path.abspath(arguments[0]), os.path.abspath(arguments[1]))
build_saber(os.path.abspath(arguments[2]), os.path.abspath(arguments[3]))
