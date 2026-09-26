import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for action_name,f in [('AN_ForestWendigo_Death',20),('AN_ForestWendigo_Death',30),('AN_ForestWendigo_Death',45),('AN_ForestWendigo_Death',60)]:
 rig.animation_data.action=bpy.data.actions[action_name]; bpy.context.scene.frame_set(f); bpy.context.view_layer.update()
 print('POSE',f)
 for n in ['pelvis','spine_02','L_clavicle','L_arm_upper','L_arm_lower','L_hand','CTRL_L_hand','R_clavicle','R_arm_upper','R_arm_lower','R_hand','CTRL_R_hand']:
  b=rig.pose.bones[n]; print(n,tuple(round(v,3) for v in b.head),tuple(round(v,3) for v in b.tail))
