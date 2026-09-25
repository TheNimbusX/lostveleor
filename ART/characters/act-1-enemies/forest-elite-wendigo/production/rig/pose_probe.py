"""Render severe bind-pose checks without changing the production rig file."""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out_dir = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
out_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
arm = next(obj for obj in scene.objects if obj.type == "ARMATURE")
mesh = next(obj for obj in scene.objects if obj.type == "MESH")

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
    ("Key", (3.2, 3.5, 5.5), 700, (1, .92, .80)),
    ("Fill", (-4, 2.0, 3.0), 450, (.72, .85, 1)),
    ("Rim", (0, -4.0, 4.3), 800, (1, .97, .85)),
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

camera_data = bpy.data.cameras.new("ProbeCamera")
camera = bpy.data.objects.new("ProbeCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = 3.7
target = Vector((0, 0, 1.5))


def clear_pose():
    for bone in arm.pose.bones:
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = Quaternion((1, 0, 0, 0))
        bone.location = Vector((0, 0, 0))
        bone.scale = Vector((1, 1, 1))
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.influence = 0


def rotate(name, axis, degrees):
    bone = arm.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    global_rotation = Quaternion(Vector(axis), math.radians(degrees))
    local_rotation = (rest.inverted() @ global_rotation.to_matrix() @ rest).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local_rotation


def move_control(name, world_position):
    bone = arm.pose.bones[name]
    transform = bone.bone.matrix_local.copy()
    transform.translation = Vector(world_position)
    bone.matrix = transform
    bpy.context.view_layer.update()


def render(label, view):
    camera.location = target + Vector(view).normalized() * 8
    look_at(camera, target)
    scene.render.filepath = str(out_dir / (label + ".png"))
    bpy.ops.render.render(write_still=True)


clear_pose()
render("neutral_front", (0, 1, .15))
render("neutral_game", (1, 1, .75))

clear_pose()
arm.pose.bones["pelvis"].location.z = -.35
rotate("pelvis", (1, 0, 0), 12)
rotate("spine_01", (1, 0, 0), -10)
for side in ("L", "R"):
    arm.pose.bones[f"{side}_leg_lower"].constraints[f"IK_{side}_foot_Plant"].influence = 1
    rotate(f"{side}_arm_upper", (1, 0, 0), 24)
render("deep_crouch_front", (0, 1, .15))
render("deep_crouch_game", (1, 1, .75))

clear_pose()
rotate("spine_02", (0, 0, 1), -26)
rotate("R_clavicle", (0, 0, 1), 22)
rotate("R_arm_upper", (1, 0, 0), 65)
rotate("R_arm_upper", (0, 0, 1), -72)
rotate("R_arm_lower", (1, 0, 0), -32)
rotate("L_arm_upper", (1, 0, 0), 35)
render("claw_windup_front", (0, 1, .15))
render("claw_windup_game", (1, 1, .75))

clear_pose()
rotate("spine_02", (0, 0, 1), 34)
rotate("R_clavicle", (0, 0, 1), -18)
rotate("R_arm_upper", (1, 0, 0), 72)
rotate("R_arm_upper", (0, 0, 1), 76)
rotate("R_arm_lower", (1, 0, 0), 20)
render("claw_followthrough_front", (0, 1, .15))
render("claw_followthrough_game", (1, 1, .75))

clear_pose()
arm.pose.bones["L_arm_lower"].constraints["IK_L_hand_Plant"].influence = 1
rotate("spine_02", (0, 0, 1), -18)
move_control("CTRL_L_hand", (-.71, .62, 1.42))
render("claw_windup_ik_game", (1, 1, .75))

clear_pose()
arm.pose.bones["L_arm_lower"].constraints["IK_L_hand_Plant"].influence = 1
rotate("spine_02", (0, 0, 1), 30)
move_control("CTRL_L_hand", (-.10, .70, 1.23))
render("claw_sweep_ik_game", (1, 1, .75))

clear_pose()
for side in ("L", "R"):
    rotate(f"{side}_arm_upper", (1, 0, 0), 145)
    rotate(f"{side}_arm_lower", (1, 0, 0), -35)
render("raised_arms_front", (0, 1, .15))

report = {"source": str(source), "views": [file.name for file in out_dir.glob("*.png")],
          "pose_labels": ["neutral", "deep_crouch", "claw_windup", "claw_followthrough", "raised_arms"]}
(out_dir / "pose_probe.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
