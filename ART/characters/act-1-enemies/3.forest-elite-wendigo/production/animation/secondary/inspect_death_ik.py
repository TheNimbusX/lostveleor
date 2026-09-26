import bpy

rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
rig.animation_data.action = bpy.data.actions["AN_ForestWendigo_Death"]
bpy.context.scene.frame_set(45)
for side in ("L", "R"):
    for name in (f"{side}_clavicle", f"{side}_arm_upper", f"{side}_arm_lower", f"{side}_hand", f"CTRL_{side}_hand", f"CTRL_{side}_elbow"):
        b = rig.pose.bones[name]
        print(name, "head", tuple(round(v, 3) for v in rig.matrix_world @ b.head), "tail", tuple(round(v, 3) for v in rig.matrix_world @ b.tail), "length", round(b.bone.length, 3))
    lower = rig.pose.bones[f"{side}_arm_lower"]
    c = lower.constraints[f"IK_{side}_hand_Plant"]
    print("IK", side, "influence", c.influence, "chain", c.chain_count, "pole", c.pole_angle)
