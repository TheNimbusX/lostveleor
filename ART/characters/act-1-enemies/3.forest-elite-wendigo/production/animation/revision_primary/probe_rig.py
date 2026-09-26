import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for name in ('L_clavicle','L_arm_upper','L_arm_lower','L_hand','R_clavicle','R_arm_upper','R_arm_lower','R_hand','CTRL_L_hand','CTRL_L_elbow','CTRL_R_hand','CTRL_R_elbow','CTRL_L_foot','CTRL_R_foot','CTRL_L_knee','CTRL_R_knee'):
    b=rig.data.bones[name]
    print('BONE',name,'parent',b.parent.name if b.parent else None,'head',tuple(round(x,3) for x in b.head_local),'tail',tuple(round(x,3) for x in b.tail_local))
for name in ('L_arm_lower','R_arm_lower','L_leg_lower','R_leg_lower'):
    print('CONS',name,[(c.name,c.chain_count,c.pole_angle) for c in rig.pose.bones[name].constraints if c.type=='IK'])
