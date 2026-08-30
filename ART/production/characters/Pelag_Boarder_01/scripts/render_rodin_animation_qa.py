"""Render deterministic contact sheets for the temporary Rodin animation clips."""

import os
import sys

import bpy
from mathutils import Vector


def arguments():
    if "--" not in sys.argv:
        raise RuntimeError("Expected FBX path and output directory after --")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise RuntimeError("Expected FBX path and output directory after --")
    return os.path.abspath(values[0]), os.path.abspath(values[1])


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def make_material(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Roughness"].default_value = 0.9
    return material


def main():
    fbx_path, output_dir = arguments()
    os.makedirs(output_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx_path)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.image_settings.file_format = "PNG"
    scene.render.resolution_x = 320
    scene.render.resolution_y = 320
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("QA_World")
    scene.world.color = (0.05, 0.06, 0.08)

    camera_data = bpy.data.cameras.new("QA_AnimationCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.5
    camera = bpy.data.objects.new("QA_AnimationCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (3.0, -4.0, 2.5)
    look_at(camera, Vector((0.0, 0.0, 0.95)))
    scene.camera = camera

    light_data = bpy.data.lights.new("QA_Key", "AREA")
    light_data.energy = 1200
    light_data.shape = "DISK"
    light_data.size = 4.0
    light = bpy.data.objects.new("QA_Key", light_data)
    scene.collection.objects.link(light)
    light.location = (3.0, -4.0, 5.0)
    look_at(light, Vector((0.0, 0.0, 1.0)))

    marker_material = make_material("QA_ContactMarker", (1.0, 0.08, 0.04))
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.07)
    marker = bpy.context.object
    marker.name = "QA_RightHandMarker"
    marker.data.materials.append(marker_material)

    armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    first = int(action.frame_range[0])
    last = int(action.frame_range[1])
    samples = sorted({first, last, *range(first, last + 1, 10)})

    hand = armature.pose.bones.get("mixamorig:RightHand")
    if hand is None:
        raise RuntimeError("Right hand bone is missing")

    for frame in samples:
        scene.frame_set(frame)
        marker.location = armature.matrix_world @ hand.head
        scene.render.filepath = os.path.join(output_dir, f"frame_{frame:03d}.png")
        bpy.ops.render.render(write_still=True)

    print(f"ANIMATION_QA_FRAMES={len(samples)}")
    print(f"ANIMATION_QA_RANGE={first}:{last}")


if __name__ == "__main__":
    main()
