"""Independent Forest Wendigo claw performance on the skin candidate rig.

Run Blender in background with ForestWendigo_Rig_SkinCandidate.blend and this
script. The file that Blender opened is never saved; this writes a new .blend
beside the script. Sim owns displacement; the rig root remains at (0, 0, 0).
"""

from pathlib import Path
import json
import math

import bpy
from mathutils import Quaternion, Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / "ForestWendigo_Claw_ArtRevision.blend"
SCENE = bpy.context.scene
SCENE.render.fps = 30
SCENE.frame_start = 0
SCENE.frame_end = 30
RIG = next(obj for obj in SCENE.objects if obj.type == "ARMATURE")
RIG.animation_data_create()
MESH = next(obj for obj in SCENE.objects if obj.type == "MESH" and obj.find_armature() == RIG)
GROUPS = {group.name: group.index for group in MESH.vertex_groups}
FOOT_VERTICES = {}
for side in ("L", "R"):
    foot_groups = {GROUPS[name] for name in (f"{side}_foot", f"{side}_toe") if name in GROUPS}
    FOOT_VERTICES[side] = [v.index for v in MESH.data.vertices
                           if any(g.group in foot_groups and g.weight >= .25
                                  for g in v.groups)]

# This deliverable holds one independently authored clip. The source .blend is
# untouched; old actions are only removed from the new in-memory copy.
RIG.animation_data.action = None
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
for bone in RIG.pose.bones:
    bone.rotation_mode = "QUATERNION"


def reset_pose():
    for bone in RIG.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)
        for con in bone.constraints:
            if con.type == "IK":
                con.influence = 0


def translate(name, xyz):
    bone = RIG.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    bone.location = rest.inverted() @ Vector(xyz)


def rotate(name, axis, degrees):
    bone = RIG.pose.bones[name]
    rest = bone.bone.matrix_local.to_3x3()
    turn = Quaternion(Vector(axis), math.radians(degrees))
    bone.rotation_quaternion = bone.rotation_quaternion @ (
        rest.inverted() @ turn.to_matrix() @ rest
    ).to_quaternion()


def ik(side, limb):
    lower = "leg_lower" if limb == "foot" else "arm_lower"
    RIG.pose.bones[f"{side}_{lower}"].constraints[f"IK_{side}_{limb}_Plant"].influence = 1


def key(frame):
    for bone in RIG.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for con in bone.constraints:
            if con.type == "IK":
                con.keyframe_insert(data_path="influence", frame=frame)


def sample(points, frame):
    ordered = sorted(points.items())
    if frame <= ordered[0][0]:
        return ordered[0][1]
    if frame >= ordered[-1][0]:
        return ordered[-1][1]
    for i in range(len(ordered)-1):
        t0, a = ordered[i]
        t1, b = ordered[i+1]
        if t0 <= frame <= t1:
            tp, ap = ordered[max(i-1, 0)]
            tn, bn = ordered[min(i+2, len(ordered)-1)]
            tangent_a = (b-ap) / max(1, t1-tp)
            tangent_b = (bn-a) / max(1, tn-t0)
            dt = t1-t0
            u = (frame-t0)/dt
            u2, u3 = u*u, u*u*u
            return ((2*u3-3*u2+1)*a + (u3-2*u2+u)*dt*tangent_a
                    + (-2*u3+3*u2)*b + (u3-u2)*dt*tangent_b)
    raise AssertionError(frame)


DEFAULT = dict(px=0, py=0, pz=0, hip=0, lower_yaw=0, chest=0,
               pitch=0, roll=0, head_yaw=0, head_pitch=0,
               rx=0, ry=0, rz=0, lx=0, ly=0, lz=0,
               rclav_yaw=0, rclav_pitch=0, lclav_yaw=0,
               rwrist=0, rwrist_yaw=0, lwrist=0, lfoot_x=0, lfoot_y=0,
               lfoot_z=0, rfoot_x=0, rfoot_y=0, rfoot_z=0,
               rfoot_roll_x=0, rfoot_roll_y=0, rfoot_roll_z=0,
               rtoe=0, ltoe=0)

# 0-7 load; 8-15 broad held silhouette; 16-18 three-frame cut;
# 19-22 head and torso overshoot; 23-30 asymmetric recovery.
# The right foot is the invariant support. The left foot plants at f10, eight
# frames ahead of contact. The fixed shoulder-to-target reach is audited later.
POSES = {
    0: {},
    3: dict(px=-.015, py=-.02, pz=-.045, hip=-3, chest=-4,
            pitch=-3, head_yaw=2, rx=.05, ry=.02, rz=.08,
            lx=-.03, ly=.05, lfoot_y=.04, lfoot_z=.035,
            rfoot_z=-.025),
    7: dict(px=-.035, py=-.06, pz=-.11, hip=-9, lower_yaw=-3,
            chest=-11, pitch=-8, head_yaw=7, head_pitch=3,
            rx=.23, ry=.04, rz=.55, lx=-.15, ly=.13, lz=-.02,
            rclav_yaw=-5, rclav_pitch=8,
            lfoot_x=-.04, lfoot_y=.15, lfoot_z=.055,
            rfoot_z=-.03),
    10: dict(px=-.05, py=-.09, pz=-.145, hip=-15, lower_yaw=-7,
             chest=-20, pitch=-11, head_yaw=11, head_pitch=4,
             rx=.02, ry=-.09, rz=.82, lx=-.18, ly=.16, lz=-.06,
             rclav_yaw=-9, rclav_pitch=10, rwrist=-10,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.07,
             rfoot_z=-.03),
    14: dict(px=-.055, py=-.11, pz=-.17, hip=-19,
             lower_yaw=-8, chest=-25, pitch=-13, head_yaw=13,
             head_pitch=5, rx=-.10, ry=-.13, rz=.88,
             lx=-.20, ly=.17, lz=-.07, rclav_yaw=-12,
             rclav_pitch=12, rwrist=-16,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.07,
             rfoot_z=-.03),
    15: dict(px=-.055, py=-.11, pz=-.17, hip=-19,
             lower_yaw=-8, chest=-26, pitch=-13, head_yaw=13,
             head_pitch=5, rx=-.09, ry=-.14, rz=.89,
             lx=-.20, ly=.17, lz=-.07, rclav_yaw=-12,
             rclav_pitch=12, rwrist=-17,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.07,
             rfoot_z=-.03),
    16: dict(px=-.055, py=-.045, pz=-.24, hip=-14,
             lower_yaw=-7, chest=-22, pitch=-17, head_yaw=12,
             head_pitch=4, rx=.11, ry=.18, rz=.74,
             lx=-.21, ly=.20, lz=-.05, rclav_yaw=-9,
             rclav_pitch=9, rwrist=-7, rwrist_yaw=-8,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.035,
             rfoot_z=-.03,
             rfoot_roll_x=10, rfoot_roll_y=5, rfoot_roll_z=3),
    17: dict(px=-.075, py=.045, pz=-.47, hip=-3,
             lower_yaw=-3, chest=-8, pitch=-24,
             head_yaw=8, head_pitch=3,
             rx=.14, ry=.76, rz=.13,
             lx=-.32, ly=-.10, lz=.22,
             rclav_yaw=-4, rclav_pitch=3, rwrist=-17, rwrist_yaw=-20,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.015,
             rfoot_z=-.03,
             rfoot_roll_x=25, rfoot_roll_y=15, rfoot_roll_z=8),
    18: dict(px=-.12, py=.11, pz=-.73, hip=12,
             lower_yaw=3, chest=13, pitch=-31, roll=-3,
             head_yaw=-7, head_pitch=12,
             rx=-.41, ry=1.16, rz=-.57,
             lx=-.50, ly=-.25, lz=.37,
             rclav_yaw=12, rclav_pitch=-6, rwrist=-60, rwrist_yaw=-35,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.035,
             rfoot_z=-.015,
             rfoot_roll_x=35, rfoot_roll_y=20, rfoot_roll_z=10),
    19: dict(px=-.13, py=.13, pz=-.75, hip=20,
             lower_yaw=6, chest=25, pitch=-34, roll=-5,
             head_yaw=-16, head_pitch=16,
             rx=-.42, ry=1.22, rz=-.56,
             lx=-.55, ly=-.40, lz=.40,
             rclav_yaw=16, rclav_pitch=-9, rwrist=-25, rwrist_yaw=-35,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.035,
             rfoot_z=-.005,
             rfoot_roll_x=35, rfoot_roll_y=10, rfoot_roll_z=20),
    22: dict(px=-.11, py=.12, pz=-.69, hip=18,
             lower_yaw=6, chest=23, pitch=-30, roll=-4,
             head_yaw=-18, head_pitch=18,
             rx=-.41, ry=1.21, rz=-.54,
             lx=-.50, ly=-.35, lz=.35,
             rclav_yaw=12, rclav_pitch=-7, rwrist=-20, rwrist_yaw=-22,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.03,
             rfoot_z=-.012,
             rfoot_roll_x=35, rfoot_roll_y=20, rfoot_roll_z=10),
    25: dict(px=-.06, py=.06, pz=-.40, hip=12,
             lower_yaw=4, chest=15, pitch=-19,
             head_yaw=-11, head_pitch=10,
             rx=-.21, ry=.91, rz=-.335,
             lx=-.25, ly=-.10, lz=.18,
             rclav_yaw=7, rclav_pitch=-3, rwrist=-10, rwrist_yaw=-8,
             lfoot_x=-.08, lfoot_y=.20, lfoot_z=-.02,
             rfoot_z=-.03,
             rfoot_roll_x=16, rfoot_roll_y=8, rfoot_roll_z=3),
    28: dict(px=-.015, py=.025, pz=-.07, hip=4,
             chest=6, pitch=-8, head_yaw=-4, head_pitch=3,
             rx=-.08, ry=.33, rz=-.06, lx=-.02, ly=.02,
             rclav_yaw=1, rwrist=9,
             lfoot_x=-.04, lfoot_y=.10, lfoot_z=.04,
             rfoot_z=-.02),
    30: {},
}


def pose(frame, values):
    SCENE.frame_set(frame)
    reset_pose()
    for side in ("L", "R"):
        ik(side, "foot")
        ik(side, "hand")
    translate("pelvis", (values["px"], values["py"], values["pz"]))
    rotate("pelvis", (0,0,1), values["hip"])
    rotate("spine_01", (0,0,1), values["lower_yaw"])
    rotate("spine_01", (1,0,0), values["pitch"]*.48)
    rotate("spine_02", (0,0,1), values["chest"])
    rotate("spine_02", (1,0,0), values["pitch"]*.52)
    rotate("spine_02", (0,1,0), values["roll"])
    rotate("neck", (0,0,1), values["head_yaw"]*.6)
    rotate("head", (0,0,1), values["head_yaw"]*.4)
    rotate("neck", (1,0,0), values["head_pitch"]*.55)
    rotate("head", (1,0,0), values["head_pitch"]*.45)
    rotate("R_clavicle", (0,0,1), values["rclav_yaw"])
    rotate("R_clavicle", (1,0,0), values["rclav_pitch"])
    rotate("L_clavicle", (0,0,1), values["lclav_yaw"])
    rotate("R_hand", (1,0,0), values["rwrist"])
    rotate("R_hand", (0,0,1), values["rwrist_yaw"])
    rotate("L_hand", (1,0,0), values["lwrist"])
    rotate("R_toe", (1,0,0), values["rtoe"])
    rotate("L_toe", (1,0,0), values["ltoe"])
    rotate("R_foot", (1,0,0), values["rfoot_roll_x"])
    rotate("R_foot", (0,1,0), values["rfoot_roll_y"])
    rotate("R_foot", (0,0,1), values["rfoot_roll_z"])
    translate("CTRL_R_hand", (values["rx"], values["ry"], values["rz"]))
    translate("CTRL_L_hand", (values["lx"], values["ly"], values["lz"]))
    translate("CTRL_R_foot", (values["rfoot_x"], values["rfoot_y"],
                              values["rfoot_z"]))
    translate("CTRL_L_foot", (values["lfoot_x"], values["lfoot_y"],
                              values["lfoot_z"]))
    # Both source rest arms are almost fully extended. Limit controllers to
    # reachable radius after torso and clavicle rotation. This prevents the
    # hands from visually lagging their keyed paths.
    bpy.context.view_layer.update()
    for side in ("L", "R"):
        shoulder = RIG.matrix_world @ RIG.pose.bones[f"{side}_arm_upper"].head
        target = RIG.matrix_world @ RIG.pose.bones[f"CTRL_{side}_hand"].head
        offset = target - shoulder
        upper = RIG.pose.bones[f"{side}_arm_upper"].bone.length
        lower = RIG.pose.bones[f"{side}_arm_lower"].bone.length
        reach = upper + lower - .015
        if offset.length > reach:
            corrected = shoulder + offset.normalized() * reach
            local_rest = RIG.matrix_world @ RIG.pose.bones[f"CTRL_{side}_hand"].bone.head_local
            translate(f"CTRL_{side}_hand", corrected - local_rest)
    # Foot controller XY is fixed during support. The skin changes toe height
    # slightly as the knees fold, so compensate its local Z against evaluated
    # sole vertices. This keeps true mesh contact at z=0 without sliding.
    planted = ["R"]
    if frame in (0, 30) or 10 <= frame <= 25:
        planted.append("L")
    for _ in range(2):
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = MESH.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        mins = {side: min((evaluated.matrix_world @ mesh.vertices[i].co).z
                          for i in FOOT_VERTICES[side]) for side in planted}
        evaluated.to_mesh_clear()
        if all(abs(z) < .001 for z in mins.values()):
            break
        for side, height in mins.items():
            channel = f"{side.lower()}foot_z"
            values[channel] -= height
            translate(f"CTRL_{side}_foot",
                      (values[f"{side.lower()}foot_x"],
                       values[f"{side.lower()}foot_y"], values[channel]))
    key(frame)


ACTION = bpy.data.actions.new("AN_ForestWendigo_Claw")
ACTION.use_fake_user = True
ACTION.frame_start = 0
ACTION.frame_end = 30
RIG.animation_data.action = ACTION
points = {}
for name, default in DEFAULT.items():
    channel = {0: default, 30: default}
    for frame, values in POSES.items():
        if name in values:
            channel[frame] = values[name]
    points[name] = channel
for frame in range(31):
    values = {name: sample(channel, frame) for name, channel in points.items()}
    pose(frame, values)

SCENE.timeline_markers.clear()
for name, frame in (("WINDUP_OPEN", 15), ("CONTACT", 18), ("RECOVER", 30)):
    marker = SCENE.timeline_markers.new(name, frame=frame)

# Keep action assigned for editing. A muted NLA strip makes the export range
# discoverable without stacking the animation twice in Blender.
track = RIG.animation_data.nla_tracks.new()
track.name = "Claw_Export"
track.strips.new(ACTION.name, 0, ACTION)
track.mute = True
RIG.animation_data.action = ACTION
SCENE.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
print("CLAW_ART_SAVED", OUT)
