import bpy
import math
import os
import sys
from mathutils import Matrix, Vector


def arg(index):
    marker = sys.argv.index("--")
    return sys.argv[marker + 1 + index]


source = os.path.abspath(arg(0))
output = os.path.abspath(arg(1))
qa_dir = os.path.abspath(arg(2))
os.makedirs(os.path.dirname(output), exist_ok=True)
os.makedirs(qa_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source, use_anim=False)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 720
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.render.fps = 30
scene.frame_start = 0
scene.frame_end = 30
bpy.context.preferences.edit.keyframe_new_interpolation_type = "LINEAR"

armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
bpy.context.view_layer.objects.active = armature
armature.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
for bone in armature.pose.bones:
    bone.rotation_mode = "XYZ"
    bone.location = Vector((0.0, 0.0, 0.0))
    bone.rotation_euler = (0.0, 0.0, 0.0)
    bone.scale = Vector((1.0, 1.0, 1.0))
bpy.ops.object.mode_set(mode="OBJECT")


def control(name):
    obj = bpy.data.objects.new(name, None)
    scene.collection.objects.link(obj)
    obj.empty_display_type = "SPHERE"
    obj.empty_display_size = 0.025
    return obj


right = control("CTRL_R_Hand")
left = control("CTRL_L_Hand")
right.rotation_mode = "XYZ"
left.rotation_mode = "XYZ"

for bone_name, target in (("R_Hand", right), ("L_Hand", left)):
    constraint = armature.pose.bones[bone_name].constraints.new("IK")
    constraint.name = "Whirlwind IK"
    constraint.target = target
    constraint.chain_count = 3
    constraint.use_rotation = True

# Frame, hips yaw, hips lower, torso lean, right hand, left hand,
# right-hand orientation. Forward for this source rig is -Y.
poses = [
    (0,   0,   0.00,   2, (-0.29, -0.12, 0.57), (0.22, -0.12, 0.60), (10,  0, -35)),
    (6, -38,  -0.045, -12, (-0.08,  0.31, 0.54), (0.27, -0.08, 0.55), (35,  0,  35)),
    (8, -12,  -0.025,  -7, (-0.33,  0.14, 0.58), (0.25, -0.18, 0.58), ( 8,  0, -20)),
    (10, 70,  -0.010,   5, (-0.34, -0.17, 0.61), (0.16,  0.22, 0.58), ( 0,  0, -82)),
    (12, 155,  0.000,   8, ( 0.00, -0.39, 0.62), (-0.22, 0.17, 0.58), ( 0,  0, -168)),
    (14, 245,  0.000,   7, ( 0.35, -0.05, 0.61), (-0.18,-0.20, 0.58), ( 0,  0, -258)),
    (17, 352, -0.005,   3, ( 0.03,  0.36, 0.59), (0.22, -0.14, 0.57), ( 5,  0, -365)),
    (21, 360, -0.015,  -2, (-0.31,  0.04, 0.57), (0.24, -0.12, 0.59), (10,  0, -395)),
    (26, 360, -0.005,   1, (-0.30, -0.09, 0.56), (0.22, -0.12, 0.60), (10,  0, -395)),
    (30, 360,  0.000,   2, (-0.29, -0.12, 0.57), (0.22, -0.12, 0.60), (10,  0, -395)),
]

hip = armature.pose.bones["Hip"]
hip.rotation_mode = "QUATERNION"
waist = armature.pose.bones["Waist"]
spine1 = armature.pose.bones["Spine01"]
spine2 = armature.pose.bones["Spine02"]
l_thigh = armature.pose.bones["L_Thigh"]
r_thigh = armature.pose.bones["R_Thigh"]
l_calf = armature.pose.bones["L_Calf"]
r_calf = armature.pose.bones["R_Calf"]
l_foot = armature.pose.bones["L_Foot"]
r_foot = armature.pose.bones["R_Foot"]

action = bpy.data.actions.new("Pelag_Whirlwind")
armature.animation_data_create()
armature.animation_data.action = action


def set_world_hip_delta(yaw_degrees, lean_degrees, world_location):
    """Convert an armature-space yaw/lean into this bone's rolled local basis."""
    rest = hip.bone.matrix_local.to_3x3()
    world_delta = (
        Matrix.Rotation(math.radians(yaw_degrees), 3, "Z")
        @ Matrix.Rotation(math.radians(lean_degrees), 3, "X")
    )
    hip.rotation_quaternion = (rest.inverted() @ world_delta @ rest).to_quaternion()
    hip.location = rest.inverted() @ Vector(world_location)

for frame, yaw, lower, lean, rh, lh, rr in poses:
    scene.frame_set(frame)
    set_world_hip_delta(
        yaw,
        lean * 0.18,
        (0.0, -0.018 if frame in (8, 10) else 0.0, lower),
    )
    waist.rotation_euler = (math.radians(lean * 0.45), 0.0, math.radians(-yaw * 0.08))
    spine1.rotation_euler = (math.radians(lean * 0.35), math.radians(4 if frame in (10, 12) else -2), math.radians(yaw * 0.05))
    spine2.rotation_euler = (math.radians(-lean * 0.15), 0.0, math.radians(yaw * 0.03))

    # Ноги не стоят столбами: низкая подготовка, шаг ведущей ногой и pivot.
    crouch = 20 if frame == 6 else 12 if frame == 8 else 5 if frame in (10, 12, 14, 17) else 2
    l_thigh.rotation_euler = (math.radians(crouch), math.radians(-6), math.radians(-yaw * 0.05))
    r_thigh.rotation_euler = (math.radians(crouch * 0.70), math.radians(7), math.radians(yaw * 0.04))
    l_calf.rotation_euler = (math.radians(-crouch * 1.25), 0.0, 0.0)
    r_calf.rotation_euler = (math.radians(-crouch), 0.0, 0.0)
    l_foot.rotation_euler = (math.radians(crouch * 0.32), 0.0, math.radians(-yaw * 0.12))
    r_foot.rotation_euler = (math.radians(crouch * 0.25), 0.0, math.radians(-yaw * 0.10))

    hip.keyframe_insert("location", frame=frame)
    hip.keyframe_insert("rotation_quaternion", frame=frame)
    for bone in (waist, spine1, spine2, l_thigh, r_thigh, l_calf, r_calf, l_foot, r_foot):
        bone.keyframe_insert("location", frame=frame)
        bone.keyframe_insert("rotation_euler", frame=frame)

    right.location = Vector(rh)
    left.location = Vector(lh)
    right.rotation_euler = tuple(math.radians(v) for v in rr)
    left.rotation_euler = (math.radians(82), math.radians(-6), math.radians(18 + yaw * 0.05))
    for target in (right, left):
        target.keyframe_insert("location", frame=frame)
        target.keyframe_insert("rotation_euler", frame=frame)

# Bake visual IK result into the skeleton so Unity receives only bone curves.
bpy.context.view_layer.objects.active = armature
armature.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.nla.bake(
    frame_start=0,
    frame_end=30,
    step=1,
    only_selected=True,
    visual_keying=True,
    clear_constraints=True,
    clear_parents=False,
    use_current_action=True,
    bake_types={"POSE"},
)
bpy.ops.object.mode_set(mode="OBJECT")

# Controls are now baked and must not leak into the FBX.
bpy.data.objects.remove(right, do_unlink=True)
bpy.data.objects.remove(left, do_unlink=True)

# A temporary saber proxy is render-only and excluded from export.
bpy.ops.mesh.primitive_cube_add(location=(0, 0, 0))
saber = bpy.context.object
saber.name = "QA_Saber_Proxy"
saber.dimensions = (0.025, 0.42, 0.018)
saber.parent = armature
saber.parent_type = "BONE"
saber.parent_bone = "R_Hand"
saber.location = (0.0, -0.19, 0.0)

# Simple neutral QA lighting and isometric camera.
bpy.ops.object.light_add(type="AREA", location=(2.4, -3.0, 4.0))
bpy.context.object.data.energy = 700
bpy.context.object.data.shape = "DISK"
bpy.context.object.data.size = 3.0
bpy.ops.object.light_add(type="AREA", location=(-2.0, 1.5, 2.2))
bpy.context.object.data.energy = 350
bpy.context.object.data.color = (1.0, 0.35, 0.16)
bpy.context.object.data.size = 2.0

bpy.ops.object.camera_add(location=(2.15, -3.15, 1.85))
camera = bpy.context.object
scene.camera = camera


def point_camera(target):
    direction = Vector(target) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


point_camera((0.0, 0.0, 0.48))
camera.data.lens = 58
if scene.world is None:
    scene.world = bpy.data.worlds.new("Whirlwind QA World")
scene.world.color = (0.035, 0.045, 0.055)

for frame, label in ((0, "ready"), (6, "anticipation"), (8, "drive"),
                     (10, "slash_a"), (12, "contact"), (14, "slash_b"),
                     (21, "follow_through"), (26, "recovery")):
    scene.frame_set(frame)
    scene.render.filepath = os.path.join(qa_dir, f"{frame:02d}_{label}.png")
    bpy.ops.render.render(write_still=True)

# Export one animation-only skeleton. Mesh and QA proxy remain untouched in source.
bpy.ops.object.select_all(action="DESELECT")
armature.select_set(True)
bpy.context.view_layer.objects.active = armature
scene.frame_set(0)
bpy.ops.export_scene.fbx(
    filepath=output,
    use_selection=True,
    object_types={"ARMATURE"},
    add_leaf_bones=False,
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
)

production_root = os.path.dirname(os.path.dirname(qa_dir))
source_dir = os.path.join(production_root, "source")
os.makedirs(source_dir, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(source_dir, "Pelag_Whirlwind_Source.blend"))
print(f"WHIRLWIND_EXPORTED={output}")
