"""Author Wendigo Claw and Leap on the reviewed deformation rig.

Run with Blender 5.2: blender -b Wendigo_Rig_v3.blend --python build_primary.py
Keeps source rig unchanged. Horizontal displacement is owned by simulation.
"""

import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


HERE = Path(__file__).resolve().parent
OUT = HERE / "Wendigo_Primary.blend"
scene = bpy.context.scene
scene.render.fps = 30
rig = next(ob for ob in scene.objects if ob.type == "ARMATURE")
rig.animation_data_create()
for bone in rig.pose.bones:
    bone.rotation_mode = "QUATERNION"


def clear_pose():
    for bone in rig.pose.bones:
        bone.location = (0.0, 0.0, 0.0)
        bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        bone.scale = (1.0, 1.0, 1.0)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.influence = 0.0


def rotate(name, axis, degrees):
    bone = rig.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    world = Quaternion(Vector(axis), math.radians(degrees))
    local = (rest.inverted() @ world.to_matrix() @ rest).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local


def move(name, *, x=0.0, y=0.0, z=0.0):
    bone = rig.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    bone.location = rest.inverted() @ Vector((x, y, z))


def constraint(side, kind, strength=1.0):
    limb = "leg_lower" if kind == "foot" else "arm_lower"
    rig.pose.bones[f"{side}_{limb}"].constraints[
        f"IK_{side}_{kind}_Plant"
    ].influence = strength


def key(frame):
    for bone in rig.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for item in bone.constraints:
            if item.type == "IK":
                item.keyframe_insert(data_path="influence", frame=frame)


def action(name, end):
    a = bpy.data.actions.new(name)
    rig.animation_data.action = a
    a.use_fake_user = True
    a.frame_start = 0
    a.frame_end = end
    return a


def steady_feet():
    for side in ("L", "R"):
        constraint(side, "foot")


def claw_pose(
    frame,
    *,
    pelvis_z=0.0,
    pelvis_turn=0.0,
    chest_turn=0.0,
    lean=0.0,
    hand=(0.0, 0.0, 0.0),
    support_hand=(0.0, 0.0, 0.0),
    head_turn=0.0,
    support_shoulder=0.0,
    wrist_pitch=0.0,
    left_foot_lift=0.0,
):
    scene.frame_set(frame)
    clear_pose()
    steady_feet()
    constraint("R", "hand")
    constraint("L", "hand")
    move("pelvis", z=pelvis_z)
    rotate("pelvis", (0, 0, 1), pelvis_turn)
    rotate("spine_01", (1, 0, 0), lean * 0.55)
    rotate("spine_02", (1, 0, 0), lean * 0.45)
    rotate("spine_02", (0, 0, 1), chest_turn)
    rotate("neck", (1, 0, 0), -lean * 0.42)
    rotate("neck", (0, 0, 1), -chest_turn * 0.35)
    rotate("head", (0, 0, 1), head_turn)
    rotate("R_clavicle", (0, 0, 1), support_shoulder)
    rotate("L_hand", (1, 0, 0), wrist_pitch)
    move("CTRL_L_foot", z=left_foot_lift)
    move("CTRL_L_hand", x=hand[0], y=hand[1], z=hand[2])
    move("CTRL_R_hand", x=support_hand[0], y=support_hand[1], z=support_hand[2])
    key(frame)


def build_claw():
    action("AN_ForestWendigo_Claw", 30)
    # The foremost arm carves a broad low arc; the opposite arm counterbalances.
    # The 140-degree damage sector remains a simulation/telegraph decision.
    claw_pose(0, hand=(0, 0, 0))
    claw_pose(5, pelvis_z=-.055, pelvis_turn=7, chest_turn=8,
              lean=-5, hand=(-.12, -.04, .19), support_hand=(.08, .18, -.04),
              head_turn=-5, support_shoulder=2, left_foot_lift=.02)
    claw_pose(12, pelvis_z=-.12, pelvis_turn=14, chest_turn=25,
              lean=-9, hand=(-.36, -.1, .55), support_hand=(.1, .3, -.14),
              head_turn=-9, support_shoulder=4, wrist_pitch=-6,
              left_foot_lift=.025)
    claw_pose(15, pelvis_z=-.13, pelvis_turn=17, chest_turn=30,
              lean=-10, hand=(-.47, -.06, .66), support_hand=(.11, .34, -.16),
              head_turn=-11, support_shoulder=5, wrist_pitch=-9,
              left_foot_lift=.025)
    claw_pose(16, pelvis_z=-.13, pelvis_turn=12, chest_turn=23,
              lean=-13, hand=(-.29, .1, .54), support_hand=(.12, .35, -.17),
              head_turn=-7, support_shoulder=5, wrist_pitch=3,
              left_foot_lift=.03)
    claw_pose(17, pelvis_z=-.16, pelvis_turn=-5, chest_turn=-15,
              lean=-21, hand=(.37, .34, .04), support_hand=(.12, .35, -.17),
              head_turn=2, support_shoulder=4, wrist_pitch=35,
              left_foot_lift=.04)
    claw_pose(18, pelvis_z=-.2, pelvis_turn=-19, chest_turn=-40,
              lean=-30, hand=(.8, .42, -.35), support_hand=(.08, .32, -.14),
              head_turn=12, support_shoulder=2, wrist_pitch=62,
              left_foot_lift=.045)
    claw_pose(21, pelvis_z=-.19, pelvis_turn=-21, chest_turn=-42,
              lean=-28, hand=(.87, .4, -.37), support_hand=(.05, .24, -.1),
              head_turn=12, support_shoulder=1, wrist_pitch=66,
              left_foot_lift=.045)
    claw_pose(25, pelvis_z=-.1, pelvis_turn=-11, chest_turn=-13,
              lean=-15, hand=(.38, .17, -.15), support_hand=(.02, .11, -.04),
              head_turn=7, support_shoulder=1, wrist_pitch=18,
              left_foot_lift=.035)
    claw_pose(30, hand=(0, 0, 0))


def leap_pose(
    frame,
    *,
    pelvis=(0.0, 0.0, 0.0),
    lean=0.0,
    head_pitch=0.0,
    hands=((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
    feet=((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
    shoulder=0.0,
):
    scene.frame_set(frame)
    clear_pose()
    for side in ("L", "R"):
        constraint(side, "foot")
        constraint(side, "hand")
    move("pelvis", x=pelvis[0], y=pelvis[1], z=pelvis[2])
    rotate("spine_01", (1, 0, 0), lean * .55)
    rotate("spine_02", (1, 0, 0), lean * .45)
    rotate("neck", (1, 0, 0), head_pitch)
    rotate("L_clavicle", (1, 0, 0), shoulder)
    rotate("R_clavicle", (1, 0, 0), shoulder)
    for i, side in enumerate(("L", "R")):
        move(f"CTRL_{side}_hand", x=hands[i][0], y=hands[i][1], z=hands[i][2])
        move(f"CTRL_{side}_foot", x=feet[i][0], y=feet[i][1], z=feet[i][2])
    key(frame)


def build_leap():
    action("AN_ForestWendigo_Leap", 51)
    leap_pose(0)
    leap_pose(8, pelvis=(0, 0, -.09), lean=-8, head_pitch=5,
              hands=((0, 0, -.11), (0, .14, -.12)),
              feet=((0, 0, .03), (0, 0, 0)))
    leap_pose(18, pelvis=(0, .055, -.34), lean=-31, head_pitch=7,
              hands=((-.16, .15, -.44), (.19, .72, -.44)),
              feet=((0, 0, .04), (0, 0, 0)), shoulder=-5)
    leap_pose(23, pelvis=(0, .08, -.4), lean=-36, head_pitch=9,
              hands=((-.18, .2, -.5), (.24, .87, -.49)),
              feet=((0, 0, .04), (0, 0, 0)), shoulder=-7)
    # Launch: pelvis leads vertically; animation never advances world position.
    leap_pose(24, pelvis=(0, .09, -.2), lean=-18, head_pitch=1,
              hands=((-.13, .15, -.22), (.18, .66, -.2)),
              feet=((0, 0, .045), (0, 0, .045)))
    leap_pose(28, pelvis=(0, .21, .99), lean=-27, head_pitch=-6,
              hands=((-.2, .12, .67), (.25, .94, .65)),
              feet=((0, -.18, .83), (0, .11, .8)), shoulder=-9)
    leap_pose(31, pelvis=(0, .18, .55), lean=-31, head_pitch=4,
              hands=((-.2, .2, .38), (.23, .93, .39)),
              feet=((0, -.1, .42), (0, .05, .4)))
    leap_pose(33, pelvis=(0, .14, -.46), lean=-47, head_pitch=14,
              hands=((-.19, .34, -.42), (.26, .94, -.68)),
              feet=((0, 0, .05), (0, 0, .03)), shoulder=4)
    leap_pose(34, pelvis=(0, .14, -.49), lean=-49, head_pitch=17,
              hands=((-.19, .34, -.43), (.26, .94, -.69)),
              feet=((0, 0, .05), (0, 0, .03)), shoulder=4)
    leap_pose(37, pelvis=(0, .11, -.35), lean=-35, head_pitch=10,
              hands=((-.16, .28, -.36), (.22, .78, -.57)),
              feet=((0, 0, .045), (0, 0, .015)), shoulder=3)
    leap_pose(43, pelvis=(0, .03, -.13), lean=-13, head_pitch=5,
              hands=((-.03, .04, -.15), (.06, .34, -.15)),
              feet=((0, 0, .04), (0, 0, 0)))
    leap_pose(51)


build_claw()
build_leap()
rig.animation_data.action = bpy.data.actions["AN_ForestWendigo_Claw"]
scene.frame_set(0)
scene.frame_start = 0
scene.frame_end = 51
bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
print(f"Saved {OUT}")
