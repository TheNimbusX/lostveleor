"""Author a short, asymmetric impact reaction on the approved Wendigo rig.

The impact enters the near (left) shoulder and ribs, propagates to the torso,
then reaches the skull two frames later. The rear paw catches the mass. The
root stays fixed because gameplay owns world travel.

Run with ForestWendigo_Rig.blend open in Blender 5.2:
  blender -b ForestWendigo_Rig.blend --python build_hit_v2.py
"""

import json
import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


HERE = Path(__file__).resolve().parent
SCENE = bpy.context.scene
SCENE.render.fps = 30
RIG = next(o for o in SCENE.objects if o.type == "ARMATURE")
MESH = next(o for o in SCENE.objects if o.type == "MESH" and o.find_armature() == RIG)
REST = {name: bone.bone.matrix_local.copy() for name, bone in RIG.pose.bones.items()}
RIG.animation_data_create()
for bone in RIG.pose.bones:
    bone.rotation_mode = "QUATERNION"


def channel(frame, points):
    """Cubic pose interpolation with a continuous velocity through break poses."""
    if frame <= points[0][0]:
        return points[0][1]
    if frame >= points[-1][0]:
        return points[-1][1]
    for i in range(len(points) - 1):
        f0, v0 = points[i]
        f1, v1 = points[i + 1]
        if not f0 <= frame <= f1:
            continue
        before = points[i - 1] if i > 0 else None
        after = points[i + 2] if i + 2 < len(points) else None
        m0 = ((v1 - before[1]) / (f1 - before[0])) if before else 0.0
        m1 = ((after[1] - v0) / (after[0] - f0)) if after else 0.0
        d = f1 - f0
        t = (frame - f0) / d
        return ((2*t*t*t - 3*t*t + 1)*v0
                + (t*t*t - 2*t*t + t)*m0*d
                + (-2*t*t*t + 3*t*t)*v1
                + (t*t*t - t*t)*m1*d)
    raise AssertionError(frame)


CURVES = {
    # Mass moves after the struck shoulder, then sinks onto the catch paw.
    "pelvis_x": [(0, 0), (2, 0.01), (5, 0.075), (7, 0.095), (10, 0.045), (14, 0.0)],
    "pelvis_y": [(0, 0), (2, -0.01), (5, -0.10), (7, -0.13), (10, -0.035), (14, 0.0)],
    "pelvis_z": [(0, 0), (2, -0.005), (5, -0.055), (7, -0.085), (10, -0.025), (14, 0.0)],
    "pelvis_yaw": [(0, 0), (2, 1), (5, 7), (7, 9), (10, -3), (14, 0)],
    "pelvis_roll": [(0, 0), (3, 0), (6, 5), (8, 3), (11, -1), (14, 0)],
    "spine_low_pitch": [(0, 0), (2, 1), (5, 12), (7, 14), (10, -2), (14, 0)],
    "spine_low_yaw": [(0, 0), (2, 2), (5, 9), (7, 10), (10, -3), (14, 0)],
    "chest_pitch": [(0, 0), (1, 3), (3, 14), (5, 25), (7, 14), (10, -3), (14, 0)],
    "chest_yaw": [(0, 0), (1, 4), (3, 17), (5, 25), (7, 15), (10, -5), (14, 0)],
    "chest_roll": [(0, 0), (1, 1), (3, 10), (5, 16), (7, 8), (10, -3), (14, 0)],
    # The antler mass is visibly late, then overshoots the returning torso.
    "neck_pitch": [(0, 0), (2, 0), (4, 3), (6, 12), (8, 14), (11, -4), (14, 0)],
    "neck_yaw": [(0, 0), (2, 0), (4, 3), (6, 9), (8, 13), (11, -3), (14, 0)],
    "head_pitch": [(0, 0), (2, 0), (4, -3), (6, 15), (8, 21), (11, -7), (14, 0)],
    "head_yaw": [(0, 0), (2, 0), (4, -2), (6, 12), (8, 16), (11, -5), (14, 0)],
    "head_roll": [(0, 0), (2, 0), (4, -2), (6, 7), (8, 10), (11, -4), (14, 0)],
    # Left side collapses first. The huge arms respond in different arcs.
    "l_clav_yaw": [(0, 0), (1, 13), (3, 27), (6, 20), (9, -6), (14, 0)],
    "l_clav_roll": [(0, 0), (1, 7), (3, 16), (6, 12), (10, -3), (14, 0)],
    "l_upper_pitch": [(0, 0), (1, -9), (3, -34), (6, -42), (9, 3), (14, 0)],
    "l_upper_yaw": [(0, 0), (1, 7), (3, 16), (6, 19), (10, -5), (14, 0)],
    "l_lower_pitch": [(0, 0), (2, 1), (4, 13), (7, 24), (10, -7), (14, 0)],
    "l_hand_pitch": [(0, 0), (3, 0), (6, -12), (9, 11), (14, 0)],
    "r_clav_pitch": [(0, 0), (2, 0), (5, -8), (8, -16), (11, 5), (14, 0)],
    "r_upper_pitch": [(0, 0), (2, 0), (5, -10), (8, -26), (11, 6), (14, 0)],
    "r_lower_pitch": [(0, 0), (3, 0), (6, -8), (9, -17), (12, 5), (14, 0)],
    # One short back-and-out catch step; paw lands while body is deepest.
    "step_x": [(0, 0), (2, 0), (4, 0.06), (7, 0.11), (9, 0.11), (11, 0.075), (14, 0)],
    "step_y": [(0, 0), (2, 0), (4, -0.085), (7, -0.16), (9, -0.16), (11, -0.105), (14, 0)],
    "step_z": [(0, 0), (2, 0), (4, 0.105), (6, 0.045), (7, 0), (9, 0),
               (10, 0.035), (11, 0.075), (12, 0.08), (13, 0.035), (14, 0)],
    "toe_roll": [(0, 0), (2, 0), (4, -10), (6, 3), (7, 0), (14, 0)],
}


def value(name, frame):
    return channel(frame, CURVES[name])


def move(name, x=0, y=0, z=0):
    bone = RIG.pose.bones[name]
    bone.location += REST[name].to_3x3().inverted() @ Vector((x, y, z))


def turn(name, axis, degrees):
    bone = RIG.pose.bones[name]
    rest_axes = REST[name].to_3x3()
    world_rotation = Quaternion(Vector(axis), math.radians(degrees))
    local_rotation = (rest_axes.inverted() @ world_rotation.to_matrix() @ rest_axes).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local_rotation


def reset_pose():
    for bone in RIG.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.influence = 0.0


def pose(frame):
    p = lambda name: value(name, frame)
    for side in ("L", "R"):
        RIG.pose.bones[f"{side}_leg_lower"].constraints[f"IK_{side}_foot_Plant"].influence = 1.0
    move("pelvis", p("pelvis_x"), p("pelvis_y"), p("pelvis_z"))
    turn("pelvis", (0, 0, 1), p("pelvis_yaw"))
    turn("pelvis", (0, 1, 0), p("pelvis_roll"))
    turn("spine_01", (1, 0, 0), p("spine_low_pitch"))
    turn("spine_01", (0, 0, 1), p("spine_low_yaw"))
    turn("spine_02", (1, 0, 0), p("chest_pitch"))
    turn("spine_02", (0, 0, 1), p("chest_yaw"))
    turn("spine_02", (0, 1, 0), p("chest_roll"))
    turn("neck", (1, 0, 0), p("neck_pitch"))
    turn("neck", (0, 0, 1), p("neck_yaw"))
    turn("head", (1, 0, 0), p("head_pitch"))
    turn("head", (0, 0, 1), p("head_yaw"))
    turn("head", (0, 1, 0), p("head_roll"))
    turn("L_clavicle", (0, 0, 1), p("l_clav_yaw"))
    turn("L_clavicle", (0, 1, 0), p("l_clav_roll"))
    turn("L_arm_upper", (1, 0, 0), p("l_upper_pitch"))
    turn("L_arm_upper", (0, 0, 1), p("l_upper_yaw"))
    turn("L_arm_lower", (1, 0, 0), p("l_lower_pitch"))
    turn("L_hand", (1, 0, 0), p("l_hand_pitch"))
    turn("R_clavicle", (1, 0, 0), p("r_clav_pitch"))
    turn("R_arm_upper", (1, 0, 0), p("r_upper_pitch"))
    turn("R_arm_lower", (1, 0, 0), p("r_lower_pitch"))
    move("CTRL_R_foot", p("step_x"), p("step_y"), p("step_z"))
    turn("R_foot", (1, 0, 0), p("toe_roll"))


FOOT_INDICES = {}
for side in ("L", "R"):
    groups = {MESH.vertex_groups[f"{side}_foot"].index,
              MESH.vertex_groups[f"{side}_toe"].index}
    FOOT_INDICES[side] = [v.index for v in MESH.data.vertices
                          if sum(g.weight for g in v.groups if g.group in groups) >= 0.60]


def settle_planted_paw(frame):
    """Bake a small authored contact correction into the animated IK targets."""
    for side in ("L", "R"):
        # The right paw makes one catch step, then resets during recovery.
        if side == "R" and (3 <= frame <= 6 or 10 <= frame <= 13):
            continue
        for _ in range(16):
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            evaluated = MESH.evaluated_get(depsgraph)
            geom = evaluated.to_mesh()
            min_z = min((evaluated.matrix_world @ geom.vertices[i].co).z
                        for i in FOOT_INDICES[side])
            evaluated.to_mesh_clear()
            error = 0.002 - min_z
            if abs(error) < 0.0025:
                break
            move(f"CTRL_{side}_foot", z=max(-0.035, min(0.035, error * 0.8)))


action = bpy.data.actions.new("AN_ForestWendigo_Hit")
action.use_fake_user = True
RIG.animation_data.action = action
for frame in range(15):
    SCENE.frame_set(frame)
    reset_pose()
    pose(frame)
    settle_planted_paw(frame)
    for bone in RIG.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.keyframe_insert(data_path="influence", frame=frame, group=bone.name)

action.use_frame_range = True
action.frame_start = 0
action.frame_end = 14
track = RIG.animation_data.nla_tracks.new()
track.name = action.name
strip = track.strips.new(action.name, 0, action)
strip.action_frame_start = 0
strip.action_frame_end = 14
strip.mute = True
RIG.animation_data.action = action
SCENE.frame_start = 0
SCENE.frame_end = 14
SCENE.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "ForestWendigo_Hit_v2.blend"))

report = {
    "action": action.name,
    "frames": [0, 14],
    "fps": 30,
    "root_motion": "none; root XY remains zero",
    "impact_side": "left shoulder and upper ribs",
    "reaction_phases": {
        "shoulder_compression": [1, 3],
        "torso_recoil": [3, 7],
        "head_antler_lag": [5, 9],
        "right_paw_catch": [4, 7],
        "right_paw_reset": [10, 14],
        "return_ready": [10, 14],
    },
    "right_paw_offset_at_end_m": [0.0, 0.0, 0.0],
    "mesh_triangles": sum(len(poly.vertices) - 2 for poly in MESH.data.polygons),
}
(HERE / "hit_v2_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
