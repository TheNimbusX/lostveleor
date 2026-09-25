"""Author revised Wendigo locomotion and collapse on the final approved rig.

Run Blender 5.2 background with ForestWendigo_Rig.blend open, then pass an output
directory after ``--``. This script never writes to the source rig directory.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


OUT = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == "ARMATURE")
meshes = [o for o in scene.objects if o.type == "MESH" and o.find_armature() == rig]
assert len(meshes) == 1, meshes
mesh = meshes[0]
rig_source = bpy.data.filepath
scene.render.fps = 30
rig.animation_data_create()
rest = {b.name: b.bone.matrix_local.copy() for b in rig.pose.bones}
for b in rig.pose.bones:
    b.rotation_mode = "QUATERNION"


def clamp(v):
    return max(0.0, min(1.0, v))


def ease(v):
    v = clamp(v)
    return v * v * (3.0 - 2.0 * v)


def ramp(f, start, end):
    return ease((f - start) / (end - start))


def bell(f, start, peak, end):
    if f < peak:
        return ramp(f, start, peak)
    return 1.0 - ramp(f, peak, end)


def local_translation(name, xyz):
    rig.pose.bones[name].location = rest[name].to_3x3().inverted() @ Vector(xyz)


def turn(name, axis, degrees):
    bone = rig.pose.bones[name]
    axes = rest[name].to_3x3()
    world_rotation = Quaternion(Vector(axis), math.radians(degrees))
    local_rotation = (axes.inverted() @ world_rotation.to_matrix() @ axes).to_quaternion()
    bone.rotation_quaternion = bone.rotation_quaternion @ local_rotation


def ik(side, part, value):
    chain = "leg_lower" if part == "foot" else "arm_lower"
    rig.pose.bones[f"{side}_{chain}"].constraints[f"IK_{side}_{part}_Plant"].influence = value


def neutral():
    for bone in rig.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)
        for constraint in bone.constraints:
            if constraint.type == "IK":
                constraint.influence = 0.0


CYCLE = 18
STANCE = 8
SPEED = 3.6
STEP = SPEED / 30.0
REACH = STANCE * STEP / 2.0
FOOT_REST = {s: Vector(rest[f"CTRL_{s}_foot"].translation) for s in ("L", "R")}
FOOT_VERTEX_IDS = {}
for side in ("L", "R"):
    relevant = {mesh.vertex_groups[f"{side}_foot"].index,
                mesh.vertex_groups[f"{side}_toe"].index}
    FOOT_VERTEX_IDS[side] = [v.index for v in mesh.data.vertices
                            if sum(g.weight for g in v.groups if g.group in relevant) >= 0.60]


def foot_path(t):
    """One physically scaled step; stance matches Sim's 3.6 m/s exactly."""
    if t < STANCE:
        return REACH - STEP * t, 0.0, True
    u = (t - STANCE) / (CYCLE - STANCE)
    # The return is suspended; tip clears the floor before passing the other
    # planted foot and decelerates into the next contact.
    y = -REACH + 2.0 * REACH * ease(u)
    z = 0.13 * math.sin(math.pi * u) ** 1.35
    return y, z, False


def walk(frame):
    cyc = math.tau * frame / CYCLE
    # A hunter's bound has a short braced phase and a brief airborne transfer.
    # The pelvis does not travel forward; the gameplay simulation owns it.
    hip_z = -0.045 - 0.045 * math.cos(2 * cyc - 2.09)
    local_translation("pelvis", (-0.064 * math.cos(cyc),
                                  0.025 * math.sin(cyc) + 0.070 * math.cos(2 * cyc), hip_z))
    turn("pelvis", (0, 0, 1), -7.5 * math.cos(cyc))
    turn("pelvis", (0, 1, 0), 4.0 * math.cos(cyc - 0.25))
    turn("pelvis", (1, 0, 0), -5.0 + 2.3 * math.cos(2 * cyc - 0.6))
    # Ribcage counter-rotates; lower spine takes the weight, skull lags behind.
    turn("spine_01", (1, 0, 0), -7.5 - 2.4 * math.cos(2 * cyc - 0.3))
    turn("spine_01", (0, 0, 1), 6.0 * math.cos(cyc - 0.35))
    turn("spine_02", (1, 0, 0), -1.5 + 3.7 * math.sin(2 * cyc - 0.9))
    turn("spine_02", (0, 0, 1), 5.0 * math.cos(cyc - 0.55))
    turn("spine_02", (0, 1, 0), -3.6 * math.cos(cyc - 0.55))
    turn("neck", (1, 0, 0), 3.0 - 3.0 * math.sin(2 * cyc - 1.15))
    turn("head", (1, 0, 0), 3.0 - 2.0 * math.sin(2 * cyc - 1.65))
    turn("head", (0, 0, 1), -3.0 * math.cos(cyc - 1.0))
    for side, offset, arm_sign, center in (("L", 0, -1, 0.07), ("R", CYCLE // 2, 1, -0.07)):
        phase = (frame + offset) % CYCLE
        fore_aft, lift, stance = foot_path(phase)
        ik(side, "foot", 1.0)
        foot = FOOT_REST[side]
        # Foot centers are lightly offset in depth because source anatomy is
        # deliberately asymmetric. Planting velocity is still -0.12 m/frame.
        local_translation(f"CTRL_{side}_foot", (0.03 * math.sin(cyc + offset),
                          center + fore_aft - foot.y, lift))
        toe_release = ramp(phase, 3.0, 6.0) * (1.0 - ramp(phase, 6.0, 8.0))
        turn(f"{side}_foot", (1, 0, 0), 14.0 * toe_release)
        turn(f"{side}_toe", (1, 0, 0), 10.0 * toe_release)
        # Shoulder leads. Forearm, wrist and skull each follow at a different
        # phase instead of all rotating with one sine wave.
        swing = math.cos(cyc - 0.15)
        delayed = math.cos(cyc - 0.65)
        wrist = math.cos(cyc - 1.05)
        baseline = 0.0 if side == "L" else 40.0
        turn(f"{side}_clavicle", (1, 0, 0), (1.0 if side == "L" else 8.0) + arm_sign * 8.0 * swing)
        turn(f"{side}_clavicle", (0, 0, 1), arm_sign * 5.0 * math.sin(cyc - 0.4))
        turn(f"{side}_arm_upper", (1, 0, 0), baseline + arm_sign * 31.0 * swing)
        turn(f"{side}_arm_lower", (1, 0, 0), 5.0 + arm_sign * 12.0 * delayed)
        turn(f"{side}_hand", (1, 0, 0), -4.0 + arm_sign * 7.0 * wrist)


def death(frame):
    f = float(frame)
    # The left foot takes the initial weight. A rightward stagger, kneebuckle,
    # then staggered left/right hand plants make a clear change of support.
    recoil = bell(f, 0, 5, 11)
    loss = ramp(f, 5, 19)
    fall = ramp(f, 18, 42)
    left_brace = ramp(f, 12, 20)
    right_brace = ramp(f, 25, 39)
    compression = bell(f, 34, 41, 49)
    settle = ramp(f, 45, 60)
    head_lag = ramp(f, 35, 52)
    for side in ("L", "R"):
        ik(side, "foot", 1.0)
        ik(side, "hand", 1.0)
    # Brief toe correction as the left planted leg absorbs the stagger.
    local_translation("CTRL_L_foot", (0, 0, 0.040 * bell(f, 8, 13, 20)))
    local_translation("pelvis", (-0.10 * loss + 0.04 * settle,
                                  -0.11 * recoil + 0.11 * fall,
                                  -0.045 * recoil - 0.19 * loss - 0.36 * fall
                                  - 0.065 * compression + 0.02 * settle))
    turn("pelvis", (1, 0, 0), 7.0 * recoil - 6.0 * loss - 11.0 * fall)
    turn("pelvis", (0, 0, 1), 10.0 * recoil - 9.0 * fall)
    turn("pelvis", (0, 1, 0), -8.0 * loss + 5.0 * settle)
    turn("spine_01", (1, 0, 0), 11.0 * recoil - 8.0 * loss - 34.0 * fall + 4.0 * settle)
    turn("spine_01", (0, 0, 1), -9.0 * loss + 5.0 * fall)
    turn("spine_02", (1, 0, 0), 8.0 * recoil - 5.0 * loss - 24.0 * fall - 4.0 * compression)
    turn("spine_02", (0, 0, 1), 11.0 * recoil - 6.0 * fall)
    # After the chest hits, the skull continues forward, then its own weight
    # rolls the mask out from under the horn mass. This keeps the dead face
    # readable from gameplay height without lifting the torso back to idle.
    turn("neck", (1, 0, 0), -13.0 * loss + 8.0 * compression
         + 35.0 * ramp(f, 28, 42))
    turn("head", (1, 0, 0), 13.0 * recoil + 10.0 * loss - 18.0 * head_lag
         + 12.0 * compression + 4.0 * settle
         + 50.0 * ramp(f, 30, 47) - 5.0 * ramp(f, 51, 60))
    turn("head", (0, 0, 1), -11.0 * recoil + 9.0 * fall)
    turn("R_clavicle", (1, 0, 0), 35.0 * fall)
    turn("R_clavicle", (0, 0, 1), 28.0 * fall)
    # Pole locations keep elbows outside the rib cage during the crash.
    local_translation("CTRL_L_elbow", (-0.12 * left_brace, 0.30 * left_brace, -0.38 * fall))
    local_translation("CTRL_R_elbow", (1.28 * right_brace, 0.55 * right_brace, -0.30 * fall))
    # Action-only pole correction: the right knee folds outside the ribcage,
    # while the original rig and all other actions retain their IK planes.
    rig.pose.bones["CTRL_R_knee"].location.x += 0.40 * ramp(f, 27, 42)
    # Paw controls target the floor; the old action stopped 0.5m above it.
    # Left catches first, right slaps down after the mass has already moved.
    local_translation("CTRL_L_hand", (-0.15 * left_brace,
                                      -0.12 * recoil + 0.25 * left_brace,
                                      0.07 * recoil - 0.48 * left_brace - 0.025 * compression))
    local_translation("CTRL_R_hand", (0.05 * right_brace,
                                      0.15 * recoil + 1.11 * right_brace,
                                      0.10 * recoil - 0.73 * right_brace - 0.025 * compression))
    turn("L_hand", (1, 0, 0), -9.0 * left_brace)
    turn("R_hand", (1, 0, 0), -28.0 * right_brace)
    # Toes remain weighted to the ground as the knees fold.
    turn("L_foot", (1, 0, 0), 12.0 * fall)
    turn("R_foot", (1, 0, 0), 6.0 * fall)


def key_all(frame):
    for bone in rig.pose.bones:
        bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
        for con in bone.constraints:
            if con.type == "IK":
                con.keyframe_insert(data_path="influence", frame=frame, group=bone.name)


def settle_foot_contacts(frame):
    """Bake ground support into controls, including the swinging claw tips."""
    for side in ("L", "R"):
        phase = (frame + (0 if side == "L" else CYCLE // 2)) % CYCLE
        planted = phase < STANCE
        control = rig.pose.bones[f"CTRL_{side}_foot"]
        for _ in range(6):
            bpy.context.view_layer.update()
            dep = bpy.context.evaluated_depsgraph_get()
            obj = mesh.evaluated_get(dep)
            evaluated = obj.to_mesh()
            minimum = min((obj.matrix_world @ evaluated.vertices[i].co).z
                          for i in FOOT_VERTEX_IDS[side])
            obj.to_mesh_clear()
            target = 0.006 if planted else max(0.006, minimum)
            error = target - minimum
            if abs(error) < 0.002:
                break
            control.location += rest[control.name].to_3x3().inverted() @ Vector((0, 0, error))


for name, final_frame, function in (("AN_ForestWendigo_Walk", CYCLE, walk),
                                    ("AN_ForestWendigo_Death", 60, death)):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    for f in range(final_frame + 1):
        scene.frame_set(f)
        neutral()
        function(f)
        if name.endswith("Walk"):
            settle_foot_contacts(f)
        key_all(f)
    action.use_frame_range = True
    action.frame_start = 0
    action.frame_end = final_frame
    track = rig.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 0, action)
    strip.action_frame_start = 0
    strip.action_frame_end = final_frame
    track.mute = True
    rig.animation_data.action = None

rig.animation_data.action = bpy.data.actions["AN_ForestWendigo_Walk"]
scene.frame_start, scene.frame_end = 0, CYCLE
scene.frame_set(0)
blend_path = OUT / "ForestWendigo_WalkDeath_Revised.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
report = {
    "source_rig": rig_source,
    "blend": str(blend_path),
    "fps": 30,
    "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
    "actions": {"AN_ForestWendigo_Walk": [0, CYCLE],
                "AN_ForestWendigo_Death": [0, 60]},
    "walk_speed_mps": SPEED,
    "walk_stance_frames": STANCE,
    "walk_foot_travel_m": 2 * REACH,
    "root_horizontal_motion": 0,
}
(OUT / "revision_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
