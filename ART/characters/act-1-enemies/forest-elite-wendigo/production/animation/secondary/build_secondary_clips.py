"""Author the Forest Wendigo's idle, locomotion, hit and death actions.

Run in Blender 5.2 with the approved rig scene open::

    blender -b Wendigo_Rig_v3.blend --python build_secondary_clips.py -- OUTPUT_DIR

The armature root never moves; all locomotion and leap travel belong to Sim.
Only rig actions, an action-source .blend and review renders are emitted here.
"""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


OUT = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
OUT.mkdir(parents=True, exist_ok=True)
SCENE = bpy.context.scene
RIG_SOURCE = bpy.data.filepath
RIG = next(obj for obj in SCENE.objects if obj.type == "ARMATURE")
MESH = next(obj for obj in SCENE.objects if obj.type == "MESH")
SCENE.render.fps = 30
RIG.animation_data_create()

for bone in RIG.pose.bones:
    bone.rotation_mode = "QUATERNION"

REST = {name: RIG.pose.bones[name].bone.matrix_local.copy() for name in RIG.pose.bones.keys()}
FOOT_REST_Y = {
    "L": REST["CTRL_L_foot"].translation.y,
    "R": REST["CTRL_R_foot"].translation.y,
}
WALK_FRAMES = 12
WALK_REACH = 0.42
FOOT_VERTEX_INDICES = {}
for side in ("L", "R"):
    relevant_groups = {MESH.vertex_groups[f"{side}_foot"].index, MESH.vertex_groups[f"{side}_toe"].index}
    FOOT_VERTEX_INDICES[side] = [
        vertex.index for vertex in MESH.data.vertices
        if sum(group.weight for group in vertex.groups if group.group in relevant_groups) >= 0.60
    ]


def smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


def pulse(t, left, peak, right):
    if t < left or t > right:
        return 0.0
    if t <= peak:
        return smooth((t - left) / max(1e-5, peak - left))
    return 1.0 - smooth((t - peak) / max(1e-5, right - peak))


def lerp(a, b, alpha):
    return a + (b - a) * alpha


def move(name, x=0.0, y=0.0, z=0.0):
    bone = RIG.pose.bones[name]
    # Bone pose locations are in the parent's rest axes, not world axes.
    bone.location = REST[name].to_3x3().inverted() @ Vector((x, y, z))


def offset(name, x=0.0, y=0.0, z=0.0):
    bone = RIG.pose.bones[name]
    bone.location += REST[name].to_3x3().inverted() @ Vector((x, y, z))


def turn(name, axis, degrees):
    bone = RIG.pose.bones[name]
    rest_axes = REST[name].to_3x3()
    world_rotation = Quaternion(Vector(axis), math.radians(degrees))
    local_rotation = (rest_axes.inverted() @ world_rotation.to_matrix() @ rest_axes).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local_rotation


def set_ik(side, limb, influence):
    suffix = "leg_lower" if limb == "foot" else "arm_lower"
    RIG.pose.bones[f"{side}_{suffix}"].constraints[f"IK_{side}_{limb}_Plant"].influence = influence


def reset_pose():
    for bone in RIG.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.influence = 0.0


def idle(frame):
    t = frame / 60.0
    breath = math.sin(math.tau * t - 0.45)
    weight = math.sin(math.tau * t + 0.8)
    delayed = math.sin(math.tau * t - 1.15)
    for side in ("L", "R"):
        set_ik(side, "foot", 1.0)
    move("pelvis", x=0.018 * weight, z=0.009 * breath)
    turn("pelvis", (0, 0, 1), 1.6 * weight)
    turn("spine_01", (1, 0, 0), -1.6 - 1.2 * breath)
    turn("spine_02", (1, 0, 0), 0.7 + 1.7 * breath)
    turn("spine_02", (0, 0, 1), -1.2 * delayed)
    turn("neck", (1, 0, 0), -0.8 * breath)
    turn("head", (0, 0, 1), 0.9 * delayed)
    for side, sign in (("L", 1), ("R", -1)):
        turn(f"{side}_clavicle", (0, 1, 0), sign * 1.4 * delayed)
        turn(f"{side}_arm_upper", (1, 0, 0), sign * 1.8 * breath)
        turn(f"{side}_arm_lower", (1, 0, 0), sign * 0.9 * delayed)


def foot_path(phase, hip_center):
    """World-space planted foot during stance, lifted return during swing."""
    stance_fraction = 0.60
    reach = WALK_REACH
    if phase <= stance_fraction:
        u = phase / stance_fraction
        # A tiny landing deceleration removes the seam snap, then the paw
        # catches up over the planted phase. Total stride remains 0.84 m.
        advance = (-2 * u**3 + 3 * u**2) + 0.40 * (u**3 - 2 * u**2 + u) + (u**3 - u**2)
        return hip_center + reach * (1 - 2 * advance), 0.0
    u = (phase - stance_fraction) / (1 - stance_fraction)
    if u <= 0.5:
        return_fraction = 0.5 * smooth(2 * u)
    else:
        return_fraction = 0.5 + 0.5 * (1 - (2 - 2 * u) ** 4)
    height = 0.32 * math.sin(math.pi * u) * (1 - smooth(u))
    return hip_center + reach * (-1 + 2 * return_fraction), height


def walk(frame):
    t = frame / WALK_FRAMES
    # Two alternating footfalls; phase 0 is left landing/front contact.
    cyc = math.tau * t
    move("pelvis", z=-0.065 + 0.018 * math.cos(2 * cyc), x=0.020 * math.sin(cyc))
    turn("pelvis", (0, 0, 1), 3.5 * math.sin(cyc))
    turn("spine_01", (1, 0, 0), -5.5)
    turn("spine_02", (1, 0, 0), -4.0 + 1.8 * math.cos(2 * cyc - 0.7))
    turn("spine_02", (0, 0, 1), -4.5 * math.sin(cyc))
    turn("neck", (1, 0, 0), 3.0)
    turn("head", (1, 0, 0), 2.0 - 1.0 * math.cos(2 * cyc - 0.5))
    for side, phase, center, arm_sign in (("L", t % 1, 0.10, -1), ("R", (t + 0.5) % 1, -0.10, 1)):
        target_y, lift_z = foot_path(phase, center)
        if phase <= 0.60:
            back_roll = max(0.0, min(1.0, (center - target_y) / WALK_REACH))
            mid_roll = math.sin(math.pi * phase / 0.60)
            roll_toe = max(back_roll, mid_roll)
        else:
            roll_toe = 1.0 - smooth((phase - 0.60) / 0.15)
        set_ik(side, "foot", 1.0)
        move(f"CTRL_{side}_foot", y=target_y - FOOT_REST_Y[side], z=lift_z + 0.085 * roll_toe)
        turn(f"{side}_foot", (1, 0, 0), 52.0 * roll_toe)
        turn(f"{side}_toe", (1, 0, 0), 20.0 * roll_toe)
        # Counter-swing the heavy claws, with the forearm lagging the shoulder.
        swing = math.sin(cyc)
        turn(f"{side}_arm_upper", (1, 0, 0), arm_sign * (16 * swing + 3))
        turn(f"{side}_arm_lower", (1, 0, 0), -arm_sign * 4 * math.sin(cyc - 0.55))
        turn(f"{side}_clavicle", (0, 0, 1), -arm_sign * 2.2 * swing)


def solve_walk_floor(frame):
    """Keep planted claw tips touching z=0 even while the ankle rolls.

    This is an authoring-time contact solve. Its result is baked into the action;
    no runtime mesh query or constraint is required in Unity.
    """
    for side, phase in (("L", frame / WALK_FRAMES % 1), ("R", (frame / WALK_FRAMES + 0.5) % 1)):
        stance = phase <= 0.60
        for _ in range(8):
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            evaluated = MESH.evaluated_get(depsgraph)
            deformed = evaluated.to_mesh()
            minimum = min((evaluated.matrix_world @ deformed.vertices[index].co).z
                          for index in FOOT_VERTEX_INDICES[side])
            evaluated.to_mesh_clear()
            if stance:
                error = 0.002 - minimum
                if abs(error) < 0.003:
                    break
            else:
                error = max(0.0, 0.002 - minimum)
                if error < 0.003:
                    break
            # The IK solution is not perfectly linear near extension; small
            # damped steps avoid alternating between hovering and penetration.
            offset(f"CTRL_{side}_foot", z=max(-0.045, min(0.045, 0.45 * error)))


def hit(frame):
    t = frame / 14.0
    recoil = pulse(t, 0.0, 0.30, 0.78)
    settle = pulse(t, 0.44, 0.75, 1.0)
    for side in ("L", "R"):
        set_ik(side, "foot", 1.0)
    move("pelvis", y=-0.11 * recoil + 0.015 * settle, z=-0.045 * recoil)
    turn("pelvis", (0, 0, 1), -6.5 * recoil)
    turn("spine_01", (1, 0, 0), 10 * recoil - 2.0 * settle)
    turn("spine_02", (1, 0, 0), 15 * recoil - 3.2 * settle)
    turn("spine_02", (0, 0, 1), -7 * recoil)
    turn("neck", (1, 0, 0), 7.0 * recoil)
    turn("head", (1, 0, 0), 8.0 * recoil)
    turn("L_arm_upper", (1, 0, 0), -17 * recoil)
    turn("R_arm_upper", (1, 0, 0), -12 * recoil)


def death(frame):
    t = frame / 45.0
    stagger = pulse(t, 0.0, 0.18, 0.40)
    fold = smooth((t - 0.16) / 0.48)
    impact = pulse(t, 0.50, 0.68, 0.84)
    settle = smooth((t - 0.68) / 0.32)
    planted = smooth((t - 0.24) / 0.35)
    for side in ("L", "R"):
        set_ik(side, "foot", 1.0)
        set_ik(side, "hand", planted)
    # The left digitigrade toe otherwise folds into the floor as the pelvis
    # collapses; a small roll keeps its claw tip on the ground.
    turn("L_foot", (1, 0, 0), 12.0 * fold)
    # Hip collapse drives the death; hands catch the weight before the head drops.
    move("pelvis", y=0.09 * fold - 0.10 * stagger, z=-0.50 * fold - 0.045 * impact)
    turn("pelvis", (1, 0, 0), -11.0 * fold + 7.0 * stagger)
    turn("pelvis", (0, 0, 1), 3.0 * stagger - 2.0 * fold)
    turn("spine_01", (1, 0, 0), -20.0 * fold + 10.0 * stagger)
    turn("spine_02", (1, 0, 0), -15.0 * fold + 11.0 * stagger)
    turn("neck", (1, 0, 0), -20.0 * fold + 5.0 * stagger)
    turn("head", (1, 0, 0), -25.0 * fold + 8.0 * stagger + 3.0 * settle)
    for side, x in (("L", -0.05), ("R", 0.15)):
        # Right arm begins far behind the body in the source; it reaches forward
        # during the stagger, then both claws brace in a wide, readable base.
        hand_y = (0.46 if side == "L" else 1.18) * planted
        hand_z = -0.50 * planted
        move(f"CTRL_{side}_hand", x=x * planted, y=hand_y, z=hand_z)
        turn(f"{side}_clavicle", (1, 0, 0), (4.0 if side == "L" else 36.0) * fold)
        if side == "R":
            turn("R_clavicle", (0, 0, 1), 38.0 * fold)
        turn(f"{side}_hand", (1, 0, 0), 8.0 * planted)


ACTIONS = (
    ("AN_ForestWendigo_Idle", 60, idle, True),
    ("AN_ForestWendigo_Walk", WALK_FRAMES, walk, True),
    ("AN_ForestWendigo_Hit", 14, hit, False),
    ("AN_ForestWendigo_Death", 45, death, False),
)


def key_pose(frame):
    for bone in RIG.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.keyframe_insert(data_path="influence", frame=frame, group=bone.name)


for name, end, pose_function, looping in ACTIONS:
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    RIG.animation_data.action = action
    for frame in range(end + 1):
        SCENE.frame_set(frame)
        reset_pose()
        pose_function(frame)
        if name == "AN_ForestWendigo_Walk":
            solve_walk_floor(frame)
        key_pose(frame)
    action.use_frame_range = True
    action.frame_start = 0
    action.frame_end = end
    if looping:
        assert name == "AN_ForestWendigo_Idle" or name == "AN_ForestWendigo_Walk"
    # Blender 5 uses slotted actions. Preserve their action slot for NLA/export.
    track = RIG.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 0, action)
    strip.action_frame_start = 0
    strip.action_frame_end = end
    strip.repeat = 1.0
    track.mute = True
    RIG.animation_data.action = None

RIG.animation_data.action = bpy.data.actions["AN_ForestWendigo_Idle"]
SCENE.frame_start = 0
SCENE.frame_end = 60
SCENE.frame_set(0)
source_blend = OUT / "Wendigo_Secondary_Actions.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(source_blend))


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


SCENE.render.engine = "BLENDER_EEVEE"
SCENE.render.resolution_x = 640
SCENE.render.resolution_y = 640
SCENE.render.resolution_percentage = 100
SCENE.render.image_settings.file_format = "PNG"
SCENE.world.use_nodes = True
SCENE.world.node_tree.nodes.get("Background").inputs[0].default_value = (0.11, 0.13, 0.14, 1)
SCENE.world.node_tree.nodes.get("Background").inputs[1].default_value = 0.65
SCENE.view_settings.view_transform = "Standard"
SCENE.view_settings.look = "Medium High Contrast"

floor_mesh = bpy.data.meshes.new("Review_Floor_Mesh")
floor_obj = bpy.data.objects.new("Review_Floor", floor_mesh)
SCENE.collection.objects.link(floor_obj)
floor_mesh.from_pydata([(-7, -7, -0.02), (7, -7, -0.02), (7, 7, -0.02), (-7, 7, -0.02)], [], [(0, 1, 2, 3)])
floor_mesh.update()
floor_material = bpy.data.materials.new("Review_Floor_Matte")
floor_material.use_nodes = True
floor_material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.18, 0.19, 0.18, 1)
floor_material.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = 0.95
floor_mesh.materials.append(floor_material)

for name, location, energy, color in (
    ("Review_Key", (3.2, 3.5, 5.5), 700, (1, .92, .80)),
    ("Review_Fill", (-4, 2, 3), 450, (.72, .85, 1)),
    ("Review_Rim", (0, -4, 4.3), 800, (1, .97, .85)),
):
    light = bpy.data.lights.new(name, "AREA")
    light.energy = energy
    light.shape = "DISK"
    light.size = 3
    light.color = color
    obj = bpy.data.objects.new(name, light)
    SCENE.collection.objects.link(obj)
    obj.location = location
    look_at(obj, Vector((0, 0, 1.5)))

camera_data = bpy.data.cameras.new("Review_Camera")
camera = bpy.data.objects.new("Review_Camera", camera_data)
SCENE.collection.objects.link(camera)
SCENE.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = 3.9

review_frames = {
    "Idle": (0, 15, 30, 45, 60),
    "Walk": (0, 2, 3, 4, 6, 8, 9, 11, 12),
    "Hit": (0, 4, 7, 11, 14),
    "Death": (0, 8, 17, 27, 35, 45),
}
views = {"game": Vector((1.0, 1.0, 0.75)), "side": Vector((1.0, 0.0, 0.20))}
for clip, frames in review_frames.items():
    RIG.animation_data.action = bpy.data.actions[f"AN_ForestWendigo_{clip}"]
    for frame in frames:
        SCENE.frame_set(frame)
        for view_name, direction in views.items():
            target = Vector((0, 0, 1.42))
            camera.location = target + direction.normalized() * 8
            look_at(camera, target)
            SCENE.render.filepath = str(OUT / "review" / clip / view_name / f"{frame:03d}.png")
            Path(SCENE.render.filepath).parent.mkdir(parents=True, exist_ok=True)
            bpy.ops.render.render(write_still=True)

report = {
    "source_rig": RIG_SOURCE,
    "source_blend": str(source_blend),
    "fps": SCENE.render.fps,
    "mesh_triangles": sum(len(p.vertices) - 2 for p in MESH.data.polygons),
    "actions": [{"name": n, "frames": [0, end], "loop": loop} for n, end, _, loop in ACTIONS],
    "root_motion": "none",
    "walk_stance_distance_m": 2 * WALK_REACH,
    "walk_stance_duration_frames": WALK_FRAMES * 0.60,
    "walk_stance_equivalent_speed_mps": 2 * WALK_REACH / (WALK_FRAMES * 0.60 / SCENE.render.fps),
    "review_frames": review_frames,
}
(OUT / "secondary_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
