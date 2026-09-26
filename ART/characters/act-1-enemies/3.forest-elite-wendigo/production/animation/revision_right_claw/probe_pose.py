import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for f in (0,7,11,15,16,17,18,19,22,30):
    bpy.context.scene.frame_set(f)
    print('POSE',f)
    for side in ('L','R'):
        shoulder=rig.pose.bones[f'{side}_arm_upper'].head
        hand=rig.pose.bones[f'{side}_hand'].head
        target=rig.pose.bones[f'CTRL_{side}_hand'].head
        print(side,'shoulder',tuple(round(x,2) for x in shoulder),'hand',tuple(round(x,2) for x in hand),'target',tuple(round(x,2) for x in target),'error',round((hand-target).length,2))
