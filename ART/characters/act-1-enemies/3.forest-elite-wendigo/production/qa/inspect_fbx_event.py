"""Temporary read-only event-pose probe for the imported Wendigo FBX."""
import bpy
import sys

path = sys.argv[sys.argv.index("--") + 1]
disconnect = "--disconnect" in sys.argv
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=path)
rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
if disconnect:
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for edit_bone in rig.data.edit_bones:
        if edit_bone.parent:
            edit_bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
print("NLA", [(track.name, track.mute, [(strip.name, strip.frame_start, strip.frame_end) for strip in track.strips])
              for track in rig.animation_data.nla_tracks])
print("PELVIS", rig.pose.bones["pelvis"].rotation_mode,
      [(c.name, c.type, c.influence) for c in rig.pose.bones["pelvis"].constraints],
      "inherit_scale", rig.data.bones["pelvis"].inherit_scale,
      "use_local_location", rig.data.bones["pelvis"].use_local_location,
      "use_connect", rig.data.bones["pelvis"].use_connect,
      "parent", rig.data.bones["pelvis"].parent.name if rig.data.bones["pelvis"].parent else None)
for track in rig.animation_data.nla_tracks:
    track.mute = True
for action in bpy.data.actions:
    if not ("Claw" in action.name or "Leap" in action.name):
        continue
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]
    for frame in (1, 19 if "Claw" in action.name else 34, 31 if "Claw" in action.name else 52):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        deps = bpy.context.evaluated_depsgraph_get()
        evaluated = rig.evaluated_get(deps)
        print("EVENT", action.name, frame, "rig_loc", tuple(round(v, 4) for v in rig.location),
              "root_loc", tuple(round(v, 4) for v in rig.pose.bones["root"].location),
              "pelvis_loc", tuple(round(v, 4) for v in rig.pose.bones["pelvis"].location),
              "pelvis_head", tuple(round(v, 4) for v in rig.pose.bones["pelvis"].head),
              "pelvis_matrix", tuple(round(v, 4) for v in rig.pose.bones["pelvis"].matrix.translation),
              "evaluated_head", tuple(round(v, 4) for v in evaluated.pose.bones["pelvis"].head),
              "evaluated_matrix", tuple(round(v, 4) for v in evaluated.pose.bones["pelvis"].matrix.translation))
