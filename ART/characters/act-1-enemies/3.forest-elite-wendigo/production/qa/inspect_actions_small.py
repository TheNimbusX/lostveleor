"""Read-only quick inspection of Wendigo action slots and joint rest positions."""

import bpy

rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
print("SCENE", bpy.context.scene.render.fps)
print("RIG", rig.name, "action", getattr(rig.animation_data.action, "name", None), "slot", getattr(rig.animation_data.action_slot, "identifier", None))
for action in bpy.data.actions:
    if action.name.startswith("AN_ForestWendigo_"):
        print("ACTION", action.name, tuple(action.frame_range), "use_frame_range", action.use_frame_range,
              "manual_limits", action.frame_start, action.frame_end,
              [(s.identifier, s.target_id_type) for s in action.slots])
for name in ("root", "pelvis", "L_foot", "L_toe", "R_foot", "R_toe", "L_hand", "R_hand"):
    b = rig.data.bones.get(name)
    if b:
        print("BONE", name, tuple(round(x, 3) for x in b.head_local), tuple(round(x, 3) for x in b.tail_local))
for action in bpy.data.actions:
    if not action.name.startswith("AN_ForestWendigo_"):
        continue
    rig.animation_data.action = action
    if action.slots:
        rig.animation_data.action_slot = action.slots[0]
    print("SET", action.name, getattr(rig.animation_data.action_slot, "identifier", None), tuple(action.frame_range))
    for frame in (0, int(action.frame_end * .5), int(action.frame_end)):
        bpy.context.scene.frame_set(frame)
        print("POSE", frame,
              tuple(round(v, 3) for v in rig.pose.bones["pelvis"].location),
              tuple(round(v, 3) for v in rig.pose.bones["L_hand"].matrix.translation),
              tuple(round(v, 3) for v in rig.pose.bones["CTRL_L_hand"].location))
