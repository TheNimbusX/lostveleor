import bpy,json
from pathlib import Path
p=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation')
sets={}
for n,file in [('run',p/'charge_loop/Stonehoof_ChargeLoop_r01.blend'),('brake',p/'brake/Stonehoof_Brake_r01.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(file));bpy.context.scene.frame_set(0);bpy.context.view_layer.update();a=bpy.data.objects['ARM_ForestStonehoof'];sets[n]={b.name:b.matrix.copy() for b in a.pose.bones if b.bone.use_deform}
 print(n,[(t,list(bpy.data.objects['CTRL_Hoof_'+t].location),a.pose.bones['MCH_Lower_'+t].constraints[0].pole_angle) for t in ['front_left','front_right','hind_left','hind_right']])
print(sorted([(b,(sets['run'][b].translation-sets['brake'][b].translation).length) for b in sets['run']],key=lambda r:-r[1])[:15])
