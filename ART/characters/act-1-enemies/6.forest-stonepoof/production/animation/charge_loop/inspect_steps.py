import bpy,math,json
p=r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation/charge_loop/'
bpy.ops.wm.open_mainfile(filepath=p+'Stonehoof_ChargeLoop_r01.blend');a=bpy.data.objects['ARM_ForestStonehoof'];s=bpy.context.scene;t='hind_right'
for i in range(58,70):
 f=i/8;s.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update();lo=a.pose.bones['MCH_Lower_'+t];up=a.pose.bones['MCH_Upper_'+t];c=lo.constraints[0];print(json.dumps({'f':f,'angle':math.degrees(c.pole_angle),'hip':list(up.head),'knee':list(lo.head),'foot':list(lo.tail),'q':list(lo.matrix.to_quaternion())}))
