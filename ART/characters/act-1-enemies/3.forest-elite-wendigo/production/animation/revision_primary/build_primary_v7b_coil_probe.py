"""Second Wendigo performance pass: supported claw cut and four-point leap.

The action root stays still. The Sim owns travel and the gameplay contact frames.
All keys are baked at 30 Hz so staggered motion survives FBX export.
"""

from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


HERE = Path(__file__).resolve().parent
OUT = HERE / "Wendigo_Primary_v7b_CoilProbe.blend"
SCENE = bpy.context.scene
SCENE.render.fps = 30
RIG = next(obj for obj in SCENE.objects if obj.type == "ARMATURE")
RIG.animation_data_create()

for bone in RIG.pose.bones:
    bone.rotation_mode = "QUATERNION"


def reset():
    for bone in RIG.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)
        for con in bone.constraints:
            if con.type == "IK":
                con.influence = 0


def world_move(name, x=0, y=0, z=0):
    bone = RIG.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    bone.location = rest.inverted() @ Vector((x, y, z))


def world_turn(name, axis, deg):
    bone = RIG.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    world = Quaternion(Vector(axis), math.radians(deg))
    local = (rest.inverted() @ world.to_matrix() @ rest).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local


def ik(side, limb):
    target = "leg_lower" if limb == "foot" else "arm_lower"
    RIG.pose.bones[f"{side}_{target}"].constraints[f"IK_{side}_{limb}_Plant"].influence = 1


def bake_key(frame):
    for bone in RIG.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for con in bone.constraints:
            if con.type == "IK":
                con.keyframe_insert(data_path="influence", frame=frame)


# Deliberate key moments, then time-aware cubic Hermite between them. Unlike
# Bezier auto handles, the hand retains velocity through the contact frame.
def sample(points, frame):
    ordered = sorted(points.items())
    if frame <= ordered[0][0]:
        return ordered[0][1]
    if frame >= ordered[-1][0]:
        return ordered[-1][1]
    for i in range(len(ordered) - 1):
        t0, v0 = ordered[i]
        t1, v1 = ordered[i + 1]
        if t0 <= frame <= t1:
            prev_t, prev_v = ordered[max(0, i - 1)]
            next_t, next_v = ordered[min(len(ordered) - 1, i + 2)]
            slope0 = (v1 - prev_v) / max(1, t1 - prev_t)
            slope1 = (next_v - v0) / max(1, next_t - t0)
            dt = t1 - t0
            u = (frame - t0) / dt
            u2, u3 = u * u, u * u * u
            return ((2*u3 - 3*u2 + 1)*v0 + (u3 - 2*u2 + u)*dt*slope0
                    + (-2*u3 + 3*u2)*v1 + (u3-u2)*dt*slope1)
    raise AssertionError(frame)


def channels(stations, defaults, end):
    result = {}
    for name, base in defaults.items():
        points = {0: base, end: base}
        for frame, values in stations.items():
            if name in values:
                points[frame] = values[name]
        result[name] = points
    return result


DEFAULT = dict(px=0, py=0, pz=0, hip_yaw=0, chest_yaw=0,
               chest_pitch=0, chest_roll=0, head_yaw=0, head_pitch=0,
               lhand_x=0, lhand_y=0, lhand_z=0, rhand_x=0, rhand_y=0,
               rhand_z=0, lfoot_x=0, lfoot_y=0, lfoot_z=0,
               rfoot_x=0, rfoot_y=0, rfoot_z=0, lclav_yaw=0,
               rclav_yaw=0, lclav_pitch=0, rclav_pitch=0,
               lwrist_pitch=0, rwrist_pitch=0, ltoe=0, rtoe=0,
               r_knee_pole=0)


def pose(frame, values):
    SCENE.frame_set(frame)
    reset()
    for side in ("L", "R"):
        ik(side, "foot")
        ik(side, "hand")
    world_move("pelvis", values["px"], values["py"], values["pz"])
    world_turn("pelvis", (0, 0, 1), values["hip_yaw"])
    world_turn("spine_01", (1, 0, 0), values["chest_pitch"]*.52)
    world_turn("spine_02", (1, 0, 0), values["chest_pitch"]*.48)
    world_turn("spine_02", (0, 0, 1), values["chest_yaw"])
    world_turn("spine_02", (0, 1, 0), values["chest_roll"])
    world_turn("neck", (1, 0, 0), values["head_pitch"]*.55)
    world_turn("head", (1, 0, 0), values["head_pitch"]*.45)
    world_turn("neck", (0, 0, 1), values["head_yaw"]*.6)
    world_turn("head", (0, 0, 1), values["head_yaw"]*.4)
    RIG.pose.bones["CTRL_R_knee"].location.x += values["r_knee_pole"]
    for side, low in (("L", "l"), ("R", "r")):
        world_turn(f"{side}_clavicle", (0, 0, 1), values[f"{low}clav_yaw"])
        world_turn(f"{side}_clavicle", (1, 0, 0), values[f"{low}clav_pitch"])
        world_turn(f"{side}_hand", (1, 0, 0), values[f"{low}wrist_pitch"])
        world_turn(f"{side}_toe", (1, 0, 0), values[f"{low}toe"])
        world_move(f"CTRL_{side}_hand", values[f"{low}hand_x"],
                   values[f"{low}hand_y"], values[f"{low}hand_z"])
        world_move(f"CTRL_{side}_foot", values[f"{low}foot_x"],
                   values[f"{low}foot_y"], values[f"{low}foot_z"])
    # Once the complete pose has evaluated, place unattainable IK goals at
    # the wrist actually achieved by the limb. The visible pose barely
    # changes, but the target no longer snaps when it crosses maximum reach.
    bpy.context.view_layer.update()
    for side in ("L", "R"):
        hand = RIG.pose.bones[f"{side}_hand"].head.copy()
        control = RIG.pose.bones[f"CTRL_{side}_hand"]
        discrepancy = hand - control.head
        if discrepancy.length > .015:
            rest = control.bone.matrix_local.to_3x3()
            control.location += rest.inverted() @ discrepancy
    bpy.context.view_layer.update()
    bake_key(frame)


def make_action(name, end, stations):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.frame_start, action.frame_end = 0, end
    RIG.animation_data.action = action
    curves = channels(stations, DEFAULT, end)
    for frame in range(end + 1):
        values = {name: sample(points, frame) for name, points in curves.items()}
        # Grounded contact must remain grounded even if cubic interpolation
        # between a lifted and planted foot would dip under the floor.
        values["lfoot_z"] = max(0, values["lfoot_z"])
        values["rfoot_z"] = max(0, values["rfoot_z"])
        pose(frame, values)
    print("BUILT", name, end)


CLAW = {
    0: {},
    3: dict(pz=-.035, py=-.025, hip_yaw=4, chest_yaw=3,
            head_yaw=-4, lhand_x=-.10, lhand_z=.10,
            rhand_y=.08, rhand_z=-.04,
            rfoot_y=-.10, rfoot_z=.06),
    7: dict(pz=-.09, py=-.07, hip_yaw=12, chest_yaw=13,
            chest_pitch=-5, head_yaw=-8, lhand_x=-.38,
            lhand_y=-.10, lhand_z=.47, rhand_y=.25,
            rhand_z=-.08, lfoot_x=-.04, lfoot_y=.06,
            lfoot_z=.065, rfoot_x=.08, rfoot_y=-.24,
            rfoot_z=0, lclav_yaw=9),
    12: dict(pz=-.13, py=-.09, hip_yaw=19, chest_yaw=27,
             chest_pitch=-9, head_yaw=-12, head_pitch=3,
             lhand_x=-.52, lhand_y=-.26, lhand_z=.82,
             rhand_x=.10, rhand_y=.38, rhand_z=-.10,
             lfoot_x=-.10, lfoot_y=.15, lfoot_z=0,
             rfoot_x=.08, rfoot_y=-.24, rfoot_z=0,
             lclav_yaw=14, lwrist_pitch=-8),
    14: dict(pz=-.15, py=-.08, hip_yaw=20, chest_yaw=34,
             chest_pitch=-10, head_yaw=-12, head_pitch=3,
             lhand_x=-.51, lhand_y=-.31, lhand_z=.87,
             rhand_x=.13, rhand_y=.42, rhand_z=-.07,
             lfoot_x=-.10, lfoot_y=.15, lfoot_z=0,
             rfoot_x=.08, rfoot_y=-.24, rfoot_z=0,
             lclav_yaw=17, lwrist_pitch=-10),
    15: dict(pz=-.15, py=-.055, hip_yaw=18, chest_yaw=31,
             chest_pitch=-11, head_yaw=-11, head_pitch=3,
             lhand_x=-.17, lhand_y=-.16, lhand_z=.52,
             rhand_x=.12, rhand_y=.36, rhand_z=-.06,
             lfoot_x=-.10, lfoot_y=.15,
             rfoot_x=.08, rfoot_y=-.24,
             lclav_yaw=15, lwrist_pitch=-3),
    16: dict(pz=-.17, py=-.01, hip_yaw=12, chest_yaw=20,
             chest_pitch=-13, head_yaw=-10, head_pitch=2,
             lhand_x=.13, lhand_y=.14, lhand_z=.37,
             rhand_x=.10, rhand_y=.28, rhand_z=-.02,
             lfoot_x=-.10, lfoot_y=.15, lclav_yaw=12,
             rfoot_x=.08, rfoot_y=-.24,
             lwrist_pitch=4),
    17: dict(pz=-.25, py=.10, hip_yaw=0, chest_yaw=0,
             chest_pitch=-21, head_yaw=-5, head_pitch=1,
             lhand_x=.73, lhand_y=.34, lhand_z=-.03,
             rhand_x=-.08, rhand_y=-.10, rhand_z=.19,
             lfoot_x=-.10, lfoot_y=.15, lclav_yaw=-7,
             rfoot_x=.08, rfoot_y=-.24,
             lwrist_pitch=32),
    18: dict(pz=-.36, py=.18, hip_yaw=-15, chest_yaw=-31,
             chest_pitch=-30, chest_roll=3, head_yaw=20,
             head_pitch=22, lhand_x=1.33, lhand_y=-.06,
             lhand_z=-.43, rhand_x=-.35, rhand_y=-.04,
             rhand_z=.37, lfoot_x=-.10, lfoot_y=.15,
             rfoot_x=.08, rfoot_y=-.24,
             lclav_yaw=-15, lwrist_pitch=54),
    19: dict(pz=-.42, py=.20, hip_yaw=-27, chest_yaw=-51,
             chest_pitch=-34, chest_roll=5, head_yaw=27,
             head_pitch=20, lhand_x=1.35, lhand_y=-.01,
             lhand_z=-.48, rhand_x=-.38, rhand_y=-.05,
             rhand_z=.38, lfoot_x=-.10, lfoot_y=.15,
             rfoot_x=.08, rfoot_y=-.24,
             lclav_yaw=-18, lwrist_pitch=61),
    21: dict(pz=-.35, py=.17, hip_yaw=-23, chest_yaw=-49,
             chest_pitch=-30, chest_roll=3, head_yaw=25,
             head_pitch=16, lhand_x=1.27, lhand_y=.03,
             lhand_z=-.43, rhand_x=-.30, rhand_y=0,
             rhand_z=.28, lfoot_x=-.10, lfoot_y=.15,
             rfoot_x=.08, rfoot_y=-.24,
             lclav_yaw=-13, lwrist_pitch=55),
    24: dict(pz=-.23, py=.10, hip_yaw=-15, chest_yaw=-25,
             chest_pitch=-19, head_yaw=15, head_pitch=9,
             lhand_x=.82, lhand_y=.10, lhand_z=-.25,
             rhand_x=-.12, rhand_y=-.18, rhand_z=.10,
             lfoot_x=-.10, lfoot_y=.15, lclav_yaw=-6,
             rfoot_x=.08, rfoot_y=-.24,
             lwrist_pitch=30),
    27: dict(pz=-.065, py=.03, hip_yaw=-6, chest_yaw=-8,
             chest_pitch=-8, head_yaw=5, lhand_x=.30,
             lhand_y=.25, lhand_z=-.16, rhand_x=-.03,
             rhand_y=-.04, rhand_z=.03, lfoot_x=-.06,
             lfoot_y=.09, lfoot_z=.06, rfoot_x=.05,
             rfoot_y=-.12, rfoot_z=.05, lwrist_pitch=10),
    30: {},
}


LEAP = {
    0: {},
    6: dict(pz=-.09, py=-.05, chest_pitch=-8,
            head_pitch=4, lhand_z=-.15, rhand_y=.15,
            rhand_z=-.15),
    12: dict(pz=-.31, py=-.07, chest_pitch=-27,
             head_pitch=12, lhand_x=-.18, lhand_y=.20,
             lhand_z=-.55, rhand_x=.04, rhand_y=1.18,
             rhand_z=-.64, lclav_pitch=-8,
             rclav_yaw=45, rclav_pitch=22, r_knee_pole=.30),
    18: dict(pz=-.52, py=0, chest_pitch=-38,
             head_pitch=34, lhand_x=-.47, lhand_y=.04,
             lhand_z=-.425, rhand_x=.44, rhand_y=1.26,
             rhand_z=-.63, lfoot_z=.025, rfoot_z=.025,
             rwrist_pitch=-50,
             lclav_pitch=-12,
             rclav_yaw=60, rclav_pitch=30, r_knee_pole=.40),
    23: dict(pz=-.57, py=.04, chest_pitch=-39,
             head_pitch=42, lhand_x=-.47, lhand_y=.04,
             lhand_z=-.45, rhand_x=.44, rhand_y=1.26,
             rhand_z=-.63, lclav_pitch=-14,
             rwrist_pitch=-50,
             rclav_yaw=60, rclav_pitch=30,
             lfoot_y=0, rfoot_y=0, lfoot_z=.03, rfoot_z=.03,
             r_knee_pole=.40),
    24: dict(pz=-.18, py=.12, chest_pitch=-25,
             head_pitch=28, lhand_x=-.43, lhand_y=.13,
             lhand_z=-.25, rhand_x=.38, rhand_y=1.27,
             rhand_z=-.43, lfoot_y=0, rfoot_y=0,
             lfoot_z=.03, rfoot_z=.03,
             lclav_pitch=-9, rclav_yaw=55, rclav_pitch=26,
             rwrist_pitch=-35,
             r_knee_pole=.32),
    26: dict(pz=.42, py=.18, chest_pitch=-9,
             head_pitch=13, lhand_x=-.40, lhand_y=.40,
             lhand_z=.52, rhand_x=.40, rhand_y=1.42,
             rhand_z=.52, lfoot_y=-.30, lfoot_z=.37,
             rfoot_y=-.20, rfoot_z=.35, lclav_pitch=4,
             lwrist_pitch=45, rwrist_pitch=47,
             rclav_yaw=48, rclav_pitch=22, r_knee_pole=.20),
    28: dict(pz=.73, py=.23, chest_pitch=-3,
             head_pitch=12, lhand_x=-.49, lhand_y=.44,
             lhand_z=.83, rhand_x=.52, rhand_y=1.48,
             rhand_z=.83, lfoot_y=-.42, lfoot_z=.67,
             rfoot_y=-.32, rfoot_z=.61, lclav_pitch=8,
             lwrist_pitch=58, rwrist_pitch=52,
             rclav_yaw=43, rclav_pitch=18, r_knee_pole=.14),
    30: dict(pz=.55, py=.26, chest_pitch=-17,
             head_pitch=18, lhand_x=-.49, lhand_y=.40,
             lhand_z=.65, rhand_x=.52, rhand_y=1.45,
             rhand_z=.65, lfoot_y=-.34, lfoot_z=.51,
             rfoot_y=-.25, rfoot_z=.46, lclav_pitch=3,
             lwrist_pitch=41, rwrist_pitch=39,
             rclav_yaw=47, rclav_pitch=20, r_knee_pole=.18),
    31: dict(pz=.23, py=.25, chest_pitch=-30,
             head_pitch=23, lhand_x=-.43, lhand_y=.20,
             lhand_z=.24, rhand_x=.42, rhand_y=1.37,
             rhand_z=.28, lfoot_y=-.18, lfoot_z=.28,
             rfoot_y=-.10, rfoot_z=.26,
             lwrist_pitch=18, rwrist_pitch=18,
             rclav_yaw=53, rclav_pitch=23, r_knee_pole=.25),
    32: dict(pz=-.15, py=.23, chest_pitch=-38,
             head_pitch=31, lhand_x=-.47, lhand_y=.08,
             lhand_z=-.30, rhand_x=.44, rhand_y=1.28,
             rhand_z=-.22, lfoot_z=.15, rfoot_z=.15,
             rclav_yaw=58, rclav_pitch=27, r_knee_pole=.34),
    33: dict(pz=-.53, py=.19, chest_pitch=-39,
             head_pitch=43, lhand_x=-.47, lhand_y=.04,
             lhand_z=-.39, rhand_x=.44, rhand_y=1.26,
             rhand_z=-.63, lfoot_y=0, rfoot_y=0,
             lfoot_z=.13, rfoot_z=.13,
             lclav_pitch=-4, rclav_yaw=60, rclav_pitch=30,
             r_knee_pole=.40),
    35: dict(pz=-.62, py=.19, chest_pitch=-46,
             head_pitch=48, lhand_x=-.47, lhand_y=.04,
             lhand_z=-.39, rhand_x=.44, rhand_y=1.26,
             rhand_z=-.60, lfoot_z=.05, rfoot_z=.05,
             lclav_pitch=-11,
             rclav_yaw=60, rclav_pitch=30, r_knee_pole=.40),
    38: dict(pz=-.47, py=.16, chest_pitch=-39,
             head_pitch=39, lhand_x=-.47, lhand_y=.04,
             lhand_z=-.39, rhand_x=.44, rhand_y=1.26,
             rhand_z=-.60, lfoot_z=.04, rfoot_z=.04,
             lclav_pitch=-9,
             rclav_yaw=60, rclav_pitch=30, r_knee_pole=.36),
    43: dict(pz=-.25, py=.08, chest_pitch=-29,
             head_pitch=8, lhand_x=-.15, lhand_y=.28,
             lhand_z=-.38, rhand_x=.02, rhand_y=.83,
             rhand_z=-.46, lclav_pitch=-4,
             rclav_yaw=42, rclav_pitch=21, r_knee_pole=.24),
    48: dict(pz=-.05, py=.01, chest_pitch=-8,
             head_pitch=2, lhand_x=-.05, lhand_y=.10,
             lhand_z=-.10, rhand_x=.04, rhand_y=.23,
             rhand_z=-.11),
    51: {},
}


make_action("AN_ForestWendigo_Claw", 30, CLAW)
make_action("AN_ForestWendigo_Leap", 51, LEAP)
RIG.animation_data.action = bpy.data.actions["AN_ForestWendigo_Claw"]
SCENE.frame_set(0)
SCENE.frame_start, SCENE.frame_end = 0, 51
bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
print("SAVED", OUT)
