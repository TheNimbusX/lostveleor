"""Bake the approved Pelag rig's heavy cleave into a planted, in-place clip.

Run with Blender 5.2, opening Pelag_GreatSword_Work.blend first.  The source
action and meshes remain untouched; the output blend keeps the new action and
the exported FBX contains only its armature.
"""

import math
from pathlib import Path

import bpy
from mathutils import Quaternion


ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "ART/PELAG/animation/cleave"
SOURCE_RIG = bpy.data.objects["Cleave_GreatSword_Export"]
CLIP_NAME = "AN_Pelag_Cleave_Polish"
RIG_NAME = "Cleave_Polish_Export"


def smooth(a, b, value):
    t = min(1.0, max(0.0, (value - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def envelope(frame, begin, peak, release, end):
    return smooth(begin, peak, frame) * (1.0 - smooth(release, end, frame))


def quaternion_local_x(radians):
    return Quaternion((1.0, 0.0, 0.0), radians)


# The source is the previously authored Great Sword Slash timing. Its torso
# provides the heavy arc, while its airborne rear step undermines the contact.
# Hold the frame-9 feet through impact and smoothly release them afterward.
bpy.context.scene.frame_set(9)
planted = {
    name: bone.matrix_basis.to_quaternion().copy()
    for name, bone in SOURCE_RIG.pose.bones.items()
    if "Leg" in name or "Foot" in name or "Toe" in name
}

rig = SOURCE_RIG.copy()
rig.data = SOURCE_RIG.data.copy()
bpy.context.collection.objects.link(rig)
rig.name = RIG_NAME
rig.animation_data_clear()
rig.animation_data_create()
action = bpy.data.actions.new(CLIP_NAME)
rig.animation_data.action = action

preview_source = bpy.data.objects["Pelag_v6.007"]
preview = preview_source.copy()
preview.data = preview_source.data.copy()
bpy.context.collection.objects.link(preview)
preview.name = "Cleave_Polish_Preview"
preview.parent = rig
preview.matrix_parent_inverse = preview_source.matrix_parent_inverse.copy()
preview.location = preview_source.location.copy()
preview.rotation_euler = preview_source.rotation_euler.copy()
preview.scale = preview_source.scale.copy()
for modifier in preview.modifiers:
    if modifier.type == "ARMATURE":
        modifier.object = rig

for frame in range(1, 29):
    bpy.context.scene.frame_set(frame)
    plant_weight = envelope(frame, 7, 9, 16, 23)
    windup = envelope(frame, 2, 8, 9, 12)
    strike = envelope(frame, 9, 13, 15, 21)

    for name, source_bone in SOURCE_RIG.pose.bones.items():
        bone = rig.pose.bones[name]
        bone.rotation_mode = "QUATERNION"
        bone.location = source_bone.location.copy()
        bone.scale = source_bone.scale.copy()
        rotation = source_bone.matrix_basis.to_quaternion()

        if name in planted:
            rotation = rotation.slerp(planted[name], plant_weight)
        elif name == "mixamorig:Spine":
            rotation @= quaternion_local_x(math.radians(-7.0 * windup + 13.0 * strike))
        elif name == "mixamorig:Spine1":
            rotation @= quaternion_local_x(math.radians(-5.0 * windup + 8.0 * strike))
        elif name == "mixamorig:Spine2":
            rotation @= quaternion_local_x(math.radians(-3.0 * windup + 5.0 * strike))
        elif name == "mixamorig:Hips":
            # This working scene is scaled down twice relative to game metres.
            # Three hundredths of a rig unit moves the hips about 2% of body height.
            bone.location.z -= 0.03 * windup

        bone.rotation_quaternion = rotation
        bone.keyframe_insert("location", frame=frame, group=name)
        bone.keyframe_insert("rotation_quaternion", frame=frame, group=name)

bpy.context.scene.frame_set(1)
bpy.context.scene.frame_start = 1
bpy.context.scene.frame_end = 28
for label, frame in (("Brace", 4), ("Peak", 9), ("Contact", 13), ("Follow-through", 16)):
    if bpy.context.scene.timeline_markers.get(label) is None:
        bpy.context.scene.timeline_markers.new(label, frame=frame)
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT / "Pelag_Cleave_Polish_Work.blend"))

# FBX importer in Unity already maps this exact Generic rig. Export only the
# new action strip so unrelated historical actions cannot leak into the clip.
rig.animation_data.action = None
track = rig.animation_data.nla_tracks.new()
track.name = "Cleave Polish"
track.strips.new(CLIP_NAME, 1, action)
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=str(OUTPUT / "export/Pelag_AN_Cleave_Polish.fbx"),
    use_selection=True,
    object_types={"ARMATURE"},
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    axis_forward="-Z",
    axis_up="Y",
)
print("[cleave-polish] saved source and FBX")
