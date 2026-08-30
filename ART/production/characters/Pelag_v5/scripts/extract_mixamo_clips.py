"""Export compact Mixamo deliveries with a stable Generic bind pose.

The small skinned mesh is intentionally retained. Blender's FBX exporter uses
the first evaluated animation frame as the skeleton bind when an armature is
exported alone. Generic playback then combines that moving bind with the
runtime body's canonical bind and tips/twists the whole character. The skin
cluster anchors the original Mixamo bind pose; Unity never instantiates these
clip meshes, so they add no rendered objects at runtime.
"""

import bpy
import os
import sys


def action_fcurves(action):
    """Return F-curves on both legacy and Blender 4/5 layered actions."""
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    return [
        curve
        for layer in action.layers
        for strip in layer.strips
        for channel_bag in getattr(strip, "channelbags", [])
        for curve in channel_bag.fcurves
    ]


def remove_horizontal_root_drift(action):
    """Bake Mixamo clips in-place while preserving hip sway and vertical motion.

    Gameplay owns the character transform.  Mixamo stores forward travel on the
    Hips Y channel and a little lateral travel on X.  Subtracting only the linear
    start-to-end displacement keeps the authored body motion, but makes the first
    and last root positions identical so loops do not jump or fight simulation.
    """
    start_frame, end_frame = action.frame_range
    duration = end_frame - start_frame
    if duration <= 0:
        return

    root_path = 'pose.bones["mixamorig:Hips"].location'
    for curve in action_fcurves(action):
        if curve.data_path != root_path or curve.array_index not in (0, 1):
            continue

        start_value = curve.evaluate(start_frame)
        end_value = curve.evaluate(end_frame)
        drift = end_value - start_value
        if abs(drift) < 1e-6:
            continue

        for point in curve.keyframe_points:
            frame = point.co.x
            correction = drift * ((frame - start_frame) / duration)
            point.co.y -= correction

            left_t = (point.handle_left.x - start_frame) / duration
            right_t = (point.handle_right.x - start_frame) / duration
            point.handle_left.y -= drift * left_t
            point.handle_right.y -= drift * right_t
        curve.update()


def export_clip(source, destination):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source, use_anim=True)

    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        raise RuntimeError(f"Expected one armature in {source}, got {len(armatures)}")
    armature = armatures[0]

    actions = list(bpy.data.actions)
    if len(actions) != 1:
        raise RuntimeError(f"Expected one action in {source}, got {len(actions)}")
    action = actions[0]
    armature.animation_data_create()
    armature.animation_data.action = action
    remove_horizontal_root_drift(action)

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"Expected one skinned mesh in {source}, got {len(meshes)}")
    mesh = meshes[0]
    mesh.data.materials.clear()

    # Keep only the canonical armature and its skin anchor. Cameras, lights,
    # empty helpers and embedded material payload are not part of a clip.
    for obj in list(bpy.context.scene.objects):
        if obj not in (armature, mesh):
            bpy.data.objects.remove(obj, do_unlink=True)

    start, end = (int(round(value)) for value in action.frame_range)
    scene = bpy.context.scene
    scene.frame_start = start
    scene.frame_end = end

    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = armature

    os.makedirs(os.path.dirname(destination), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
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
    print(
        f"EXPORTED={os.path.basename(source)}|{os.path.basename(destination)}|"
        f"frames={start}-{end}|bones={len(armature.data.bones)}|action={action.name}"
    )


arguments = sys.argv[sys.argv.index("--") + 1 :]
if len(arguments) % 2:
    raise RuntimeError("Expected source/destination pairs")
for index in range(0, len(arguments), 2):
    export_clip(os.path.abspath(arguments[index]), os.path.abspath(arguments[index + 1]))
