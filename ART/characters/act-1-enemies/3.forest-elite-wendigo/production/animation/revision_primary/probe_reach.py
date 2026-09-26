import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for action_name,frames in (('AN_ForestWendigo_Claw',(0,12,15,16,17,18,19)),('AN_ForestWendigo_Leap',(18,23,28,33,35))):
    rig.animation_data.action=bpy.data.actions[action_name]
    for f in frames:
        bpy.context.scene.frame_set(f)
        print('POSE',action_name,f)
        for side in ('L','R'):
            sh=rig.pose.bones[f'{side}_arm_upper'].head
            hand=rig.pose.bones[f'{side}_hand'].head
            target=rig.pose.bones[f'CTRL_{side}_hand'].head
            print(side,'shoulder',tuple(round(x,2) for x in sh),'hand',tuple(round(x,2) for x in hand),'target',tuple(round(x,2) for x in target),'error',round((hand-target).length,2))
