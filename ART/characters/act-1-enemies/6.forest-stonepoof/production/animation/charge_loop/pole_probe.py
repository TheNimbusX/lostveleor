import bpy,json,math
from mathutils import Vector
p=r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation/charge_loop/'
bpy.ops.wm.open_mainfile(filepath=p+'Stonehoof_ChargeLoop_r01.blend');a=bpy.data.objects['ARM_ForestStonehoof'];s=bpy.context.scene;s.frame_set(6)
for t in ['front_left','front_right','hind_left','hind_right']:
 up=a.pose.bones['MCH_Upper_'+t];lo=a.pose.bones['MCH_Lower_'+t];c=lo.constraints[0];pole=bpy.data.objects['CTRL_Pole_'+t];hip=up.head.copy();target=bpy.data.objects['CTRL_Hoof_'+t].location.copy();pole.location=hip+Vector((0,1,0));bpy.context.view_layer.update()
 print(t,'orig angle',c.pole_angle)
 for deg in [0,90,180,-90]:
  c.pole_angle=math.radians(deg);bpy.context.view_layer.update();print(deg,tuple(round(v,3) for v in lo.head-hip))
