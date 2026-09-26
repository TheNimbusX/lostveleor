"""Render rest skeleton over textured mesh to align joints to anatomy."""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out_dir = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
out_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
arm = next(obj for obj in scene.objects if obj.type == "ARMATURE")

def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs[0].default_value = (*color, 1)
    mat.node_tree.links.new(emission.outputs[0], output.inputs[0])
    return mat

mats = {
    "L_arm": material("LArmRed", (1, .12, .12)),
    "R_arm": material("RArmCyan", (.1, 1, 1)),
    "L_leg": material("LLegYellow", (1, .84, .05)),
    "R_leg": material("RLegGreen", (.2, 1, .15)),
    "body": material("BodyMagenta", (1, .2, .9)),
}

for bone in arm.data.bones:
    if not bone.use_deform:
        continue
    name = bone.name
    if name.startswith("L_arm") or name.startswith("L_clavicle") or name.startswith("L_hand"):
        mat = mats["L_arm"]
    elif name.startswith("R_arm") or name.startswith("R_clavicle") or name.startswith("R_hand"):
        mat = mats["R_arm"]
    elif name.startswith("L_leg") or name.startswith("L_foot") or name.startswith("L_toe"):
        mat = mats["L_leg"]
    elif name.startswith("R_leg") or name.startswith("R_foot") or name.startswith("R_toe"):
        mat = mats["R_leg"]
    else:
        mat = mats["body"]
    start = arm.matrix_world @ bone.head_local
    end = arm.matrix_world @ bone.tail_local
    midpoint = (start + end) / 2
    vec = end - start
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=.013, depth=vec.length,
                                        location=midpoint)
    cylinder = bpy.context.object
    cylinder.name = "GUIDE_" + name
    cylinder.rotation_euler = vec.to_track_quat("Z", "Y").to_euler()
    cylinder.data.materials.append(mat)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, radius=.023,
                                         location=start)
    bpy.context.object.data.materials.append(mat)

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.use_nodes = True
scene.world.node_tree.nodes.get("Background").inputs[0].default_value = (.1, .12, .13, 1)
scene.world.node_tree.nodes.get("Background").inputs[1].default_value = .7

def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()

for name, position, energy in (("Key", (3, 4, 5), 700),
                               ("Fill", (-3, 2, 3), 450),
                               ("Rim", (0, -4, 4), 800)):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = 3
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    look_at(obj, Vector((0, 0, 1.5)))

cam_data = bpy.data.cameras.new("Camera")
cam = bpy.data.objects.new("Camera", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.type = "ORTHO"
cam_data.ortho_scale = 3.7
target = Vector((0, 0, 1.5))
for name, direction in (("front", (0, 1, .1)), ("left", (-1, 0, .1)),
                        ("right", (1, 0, .1)), ("game", (1, 1, .7))):
    cam.location = target + Vector(direction).normalized() * 8
    look_at(cam, target)
    scene.render.filepath = str(out_dir / ("bones_" + name + ".png"))
    bpy.ops.render.render(write_still=True)
