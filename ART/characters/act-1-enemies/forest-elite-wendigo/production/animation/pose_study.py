"""Non-destructive blocking study for the approved Wendigo rig."""

import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


out = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
for pb in rig.pose.bones:
    pb.rotation_mode = "QUATERNION"


def reset():
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
        for constraint in pb.constraints:
            if constraint.type == "IK":
                constraint.influence = 0


def rotate(name, axis, degrees):
    pb = rig.pose.bones[name]
    rest = pb.bone.matrix_local.to_3x3()
    world = Quaternion(Vector(axis), math.radians(degrees))
    pb.rotation_quaternion = pb.rotation_quaternion @ (rest.inverted() @ world.to_matrix() @ rest).to_quaternion()


def move(name, x=0, y=0, z=0):
    pb = rig.pose.bones[name]
    delta = Vector((x, y, z))
    pb.location = pb.bone.matrix_local.to_3x3().inverted() @ delta


def ik(side, kind, amount=1):
    end = "leg_lower" if kind == "foot" else "arm_lower"
    rig.pose.bones[f"{side}_{end}"].constraints[f"IK_{side}_{kind}_Plant"].influence = amount


scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 850
scene.render.resolution_y = 850
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.use_nodes = True
scene.world.node_tree.nodes.get("Background").inputs[0].default_value = (0.11, 0.13, 0.14, 1)
scene.world.node_tree.nodes.get("Background").inputs[1].default_value = 0.65
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


for name, position, energy, color in (
    ("StudyKey", (3.2, 3.5, 5.5), 700, (1, .92, .80)),
    ("StudyFill", (-4, 2, 3), 450, (.72, .85, 1)),
    ("StudyRim", (0, -4, 4.3), 800, (1, .97, .85)),
):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = 3
    data.color = color
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    look_at(obj, Vector((0, 0, 1.5)))
camera_data = bpy.data.cameras.new("StudyCamera")
camera = bpy.data.objects.new("StudyCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = 3.8


def render(label, direction):
    target = Vector((0, 0, 1.45))
    camera.location = target + Vector(direction).normalized() * 8
    look_at(camera, target)
    scene.render.filepath = str(out / f"{label}.png")
    bpy.context.view_layer.update()
    bpy.ops.render.render(write_still=True)


reset()
for side, sign in (("L", -1), ("R", 1)):
    ik(side, "foot")
    ik(side, "hand")
    move(f"CTRL_{side}_foot", x=sign * .10, y=.04)
    move(f"CTRL_{side}_hand", x=sign * .12, y=.28, z=-.44)
move("pelvis", z=-.34)
rotate("spine_01", (1, 0, 0), -10)
rotate("spine_02", (1, 0, 0), -11)
rotate("neck", (1, 0, 0), -7)
render("crouch_game", (1, 1, .75))
render("crouch_side", (1, 0, .2))

reset()
for side in ("L", "R"):
    ik(side, "foot")
move("pelvis", z=-.10)
rotate("pelvis", (0, 0, 1), -10)
rotate("spine_02", (0, 0, 1), -35)
rotate("R_clavicle", (0, 0, 1), 25)
rotate("R_arm_upper", (1, 0, 0), 105)
rotate("R_arm_upper", (0, 0, 1), -75)
rotate("R_arm_lower", (1, 0, 0), -35)
rotate("L_arm_upper", (1, 0, 0), 25)
render("claw_windup_game", (1, 1, .75))

reset()
for side in ("L", "R"):
    ik(side, "foot")
move("pelvis", z=-.08)
rotate("pelvis", (0, 0, 1), 18)
rotate("spine_02", (0, 0, 1), 48)
rotate("spine_02", (1, 0, 0), -15)
rotate("R_clavicle", (0, 0, 1), -20)
rotate("R_arm_upper", (1, 0, 0), 85)
rotate("R_arm_upper", (0, 0, 1), 92)
rotate("R_arm_lower", (1, 0, 0), 20)
rotate("L_arm_upper", (1, 0, 0), 30)
render("claw_contact_game", (1, 1, .75))
