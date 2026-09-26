import bpy

scene = bpy.context.scene
rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
rig.animation_data.action = bpy.data.actions["AN_ForestWendigo_Leap"]
for frame in range(12, 39):
    scene.frame_set(frame)
    values = []
    for side in ("L", "R"):
        hand = rig.pose.bones[f"{side}_hand"].head
        target = rig.pose.bones[f"CTRL_{side}_hand"].head
        values.append(round((hand - target).length, 3))
    print("LEAP_REACH", frame, *values)
