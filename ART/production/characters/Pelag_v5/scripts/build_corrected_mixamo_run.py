"""Build a wider, less cross-legged Pelag run on the canonical Mixamo rig."""

import bpy
import os
import sys
from mathutils import Vector


SIDE_CLEARANCE = 0.045
STRIDE_SCALE = 1.25


def world_head(armature, bone_name):
    return armature.matrix_world @ armature.pose.bones[bone_name].head


def target_track(armature, start, end, side):
    track = {}
    sign = 1.0 if side == "Left" else -1.0
    foot_name = f"mixamorig:{side}Foot"
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        hips = world_head(armature, "mixamorig:Hips")
        foot = world_head(armature, foot_name)
        relative = foot - hips

        # Do not let either ankle pass through the character's centre line.
        # The old clip reached x=-0.006 on the left foot and x=-0.007 on the
        # right, which reads as an inversion from the isometric camera.
        relative.x = sign * max(abs(relative.x), SIDE_CLEARANCE)

        # Slightly lengthen the authored stride without asking the leg to
        # hyperextend. This is an animation/readability correction, not an
        # attempt to fake the simulation's full speed through playback speed.
        relative.y *= STRIDE_SCALE
        track[frame] = hips + relative
    return track


def build(source_path, output_path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(source_path), use_anim=True)
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    action = next(iter(bpy.data.actions))
    armature.animation_data_create()
    armature.animation_data.action = action
    start = int(round(action.frame_range[0]))
    end = int(round(action.frame_range[1]))

    tracks = {
        side: target_track(armature, start, end, side)
        for side in ("Left", "Right")
    }

    for side in ("Left", "Right"):
        target = bpy.data.objects.new(f"QA_{side}AnkleTarget", None)
        bpy.context.scene.collection.objects.link(target)
        for frame, location in tracks[side].items():
            target.location = location
            target.keyframe_insert(data_path="location", frame=frame)

        shin = armature.pose.bones[f"mixamorig:{side}Leg"]
        constraint = shin.constraints.new("IK")
        constraint.name = "Pelag_Run_Ankle_Clearance"
        constraint.target = target
        constraint.chain_count = 2
        constraint.use_rotation = False
        constraint.iterations = 64

    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.nla.bake(
        frame_start=start,
        frame_end=end,
        step=1,
        only_selected=True,
        visual_keying=True,
        clear_constraints=True,
        clear_parents=False,
        use_current_action=False,
        clean_curves=False,
        bake_types={"POSE"},
    )
    bpy.ops.object.mode_set(mode="OBJECT")

    # Helper targets are QA machinery, never runtime content.
    for obj in list(bpy.data.objects):
        if obj.name.startswith("QA_"):
            bpy.data.objects.remove(obj, do_unlink=True)

    baked_action = armature.animation_data.action
    if baked_action is None:
        raise RuntimeError("Constraint bake did not create an armature action")
    # Keep the original FBX take name so Unity's existing .meta clip mapping
    # (takeName: Scene, internalID preserved) remains valid on reimport.
    baked_action.name = "Scene"
    armature.animation_data.action = baked_action
    for candidate in list(bpy.data.actions):
        if candidate != baked_action:
            bpy.data.actions.remove(candidate)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    if armature.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.abspath(output_path),
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_simplify_factor=0.0,
        path_mode="AUTO",
    )
    print(
        f"CORRECTED_RUN={os.path.abspath(output_path)}|frames={start}-{end}|"
        f"side_clearance={SIDE_CLEARANCE}|stride_scale={STRIDE_SCALE}"
    )


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) != 2:
    raise RuntimeError("Expected source FBX and output FBX")
build(arguments[0], arguments[1])
