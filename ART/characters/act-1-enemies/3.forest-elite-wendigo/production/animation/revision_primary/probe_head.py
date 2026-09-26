import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for act,frames in [('AN_ForestWendigo_Claw',[0,15,18,19]),('AN_ForestWendigo_Leap',[0,23,33])]:
    rig.animation_data.action=bpy.data.actions[act]
    for f in frames:
        bpy.context.scene.frame_set(f)
        for name in ['spine_02','neck','head']:
            b=rig.pose.bones[name]
            direction=(b.tail-b.head).normalized()
            print(act,f,name,'head',tuple(round(x,2) for x in b.head),'dir',tuple(round(x,2) for x in direction))
