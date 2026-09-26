import bpy,json
p=r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation/charge_loop/'
bpy.ops.wm.open_mainfile(filepath=p+'Stonehoof_ChargeLoop_r01.blend');a=bpy.data.objects['ARM_ForestStonehoof']
for f in [0,5,5.5,6,6.5,6.625,6.75,7,7.5,8]:
 bpy.context.scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
 print(json.dumps({'f':f,'legs':{t:{'hip':list(a.pose.bones['MCH_Upper_'+t].head),'knee':list(a.pose.bones['MCH_Lower_'+t].head),'foot':list(a.pose.bones['MCH_Lower_'+t].tail),'target':list(bpy.data.objects['CTRL_Hoof_'+t].location),'pole':list(bpy.data.objects['CTRL_Pole_'+t].location)} for t in ['front_left','hind_left']}}))

