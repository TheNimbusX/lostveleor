import bpy,json
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'Stonehoof_WindupStart_r01.blend'));bpy.context.scene.frame_set(0);a=bpy.data.objects['ARM_ForestStonehoof'];bpy.context.view_layer.update()
for name in ['leg_front_left_top0','leg_front_left_top1','leg_front_left_bot0','leg_front_left_bot1','leg_front_left_bot2','MCH_Upper_front_left','MCH_Lower_front_left','MCH_Drive_leg_front_left_top1','MCH_Drive_leg_front_left_bot1']:
 b=a.pose.bones[name];print(name,json.dumps({'head':list(b.head),'tail':list(b.tail),'bind_head':list(b.bone.head_local),'bind_tail':list(b.bone.tail_local),'delta':b.matrix.to_quaternion().rotation_difference(b.bone.matrix_local.to_quaternion()).angle,'basis':[list(x) for x in b.matrix_basis]}))
print('FOOT',list(bpy.data.objects['CTRL_Hoof_front_left'].location))
