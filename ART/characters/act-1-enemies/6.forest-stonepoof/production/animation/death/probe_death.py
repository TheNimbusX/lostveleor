import bpy,json
from pathlib import Path
p=Path(__file__).parent;bpy.ops.wm.open_mainfile(filepath=str(p/'Stonehoof_Death_r01.blend'));a=bpy.data.objects['ARM_ForestStonehoof'];s=bpy.context.scene
for f in (0,4,5.375,12,20,32,48):
 s.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
 for tag in ('front_left','front_right','hind_left','hind_right'):
  u=a.pose.bones['MCH_Upper_'+tag];l=a.pose.bones['MCH_Lower_'+tag];foot=a.pose.bones['leg_'+tag+'_bot2'];t=bpy.data.objects['CTRL_Hoof_'+tag].location
  print('POSE',json.dumps(dict(f=f,tag=tag,hip=list(u.head),knee=list(l.head),ankle=list(foot.head),target=list(t),reach=(t-u.head).length,limb=u.bone.length+l.bone.length,helper_error=(l.tail-t).length,deform_error=(foot.head-t).length)))
