"""Render a before/after contact sheet of Pelag's run for the weapon-arm pass.

A stand-in blade is parented to ``mixamorig:RightHand`` so the preview shows
what the sabre does, not just what the fist does.  The blade is a plain box in
bone space: its exact hilt placement is the game's business, its attitude over
the cycle is what this preview is for.

    blender -b -noaudio -P tools/pelag_run_arm_preview.py -- <fbx> <out_dir>
"""

from __future__ import annotations

import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


FRAMES = list(range(1, 34, 3))


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for blocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes,
                   bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(blocks):
            if block.users == 0:
                blocks.remove(block)


def main() -> None:
    fbx, out_dir = sys.argv[sys.argv.index("--") + 1:][:2]
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)

    clear_scene()
    bpy.ops.import_scene.fbx(filepath=fbx, use_anim=True)
    scene = bpy.context.scene
    armature = max((o for o in scene.objects if o.type == "ARMATURE"),
                   key=lambda o: len(o.data.bones))

    # Body extent, so the camera frames the character rather than a guess.
    meshes = [o for o in scene.objects if o.type == "MESH"]
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for mesh in meshes:
        for corner in mesh.bound_box:
            world = mesh.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], world[i]) for i in range(3)))
            hi = Vector((max(hi[i], world[i]) for i in range(3)))
    centre = (lo + hi) * 0.5
    height = max(hi.z - lo.z, 0.001)

    # Stand-in blade: a long thin box along the hand's own long axis.
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    blade = bpy.context.active_object
    blade.name = "BladeProxy"
    hand = armature.pose.bones["mixamorig:RightHand"]
    blade_length = height * 0.55
    blade.scale = (height * 0.015, blade_length * 0.5, height * 0.035)
    blade.parent = armature
    blade.parent_type = "BONE"
    blade.parent_bone = "mixamorig:RightHand"
    # Bone parenting anchors at the bone tail; step back along the bone so the
    # hilt sits in the fist and the blade runs on past the fingers.
    blade.matrix_parent_inverse = (armature.matrix_world @ hand.matrix).inverted()
    blade.location = (0.0, blade_length * 0.5, 0.0)
    blade.rotation_euler = (0.0, 0.0, 0.0)

    material = bpy.data.materials.new("BladeProxyMaterial")
    material.use_nodes = True
    material.node_tree.nodes["Principled BSDF"].inputs[0].default_value = (0.85, 0.1, 0.1, 1.0)
    blade.data.materials.append(material)

    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    distance = height * 2.6
    camera.location = centre + Vector((distance * 0.72, -distance * 0.72, height * 0.28))
    direction = centre - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 55
    scene.camera = camera

    sun_data = bpy.data.lights.new("PreviewSun", type="SUN")
    sun_data.energy = 4.0
    sun = bpy.data.objects.new("PreviewSun", sun_data)
    sun.rotation_euler = (math.radians(55), 0.0, math.radians(35))
    scene.collection.objects.link(sun)
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.62, 0.6, 1.0)

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 320
    scene.render.resolution_y = 400
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"

    for index, frame in enumerate(FRAMES):
        scene.frame_set(frame)
        scene.render.filepath = str(out / f"f_{index:02d}")
        bpy.ops.render.render(write_still=True)
    print(f"RENDERED {len(FRAMES)} -> {out}")


if __name__ == "__main__":
    main()
