import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for side in ('L','R'):
    for n in (f'{side}_clavicle',f'{side}_arm_upper',f'{side}_arm_lower',f'{side}_hand',f'CTRL_{side}_hand',f'{side}_leg_upper',f'{side}_leg_lower',f'{side}_foot',f'CTRL_{side}_foot'):
        b=rig.pose.bones[n]
        print(n,'head',tuple(round(v,3) for v in b.head),'tail',tuple(round(v,3) for v in b.tail),'length',round((b.tail-b.head).length,3))
