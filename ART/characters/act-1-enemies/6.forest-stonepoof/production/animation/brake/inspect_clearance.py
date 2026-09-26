import bpy
p=r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation/brake/'
bpy.ops.wm.open_mainfile(filepath=p+'Stonehoof_Brake_r01.blend');s=bpy.context.scene;o=bpy.data.objects['CTRL_Hoof_front_left']
for layer in o.animation_data.action.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for fc in bag.fcurves:
    if fc.data_path=='["sole_clearance"]':print('propcurve',[(tuple(k.co)) for k in fc.keyframe_points])
for f in [0,1,2,3,5,18,0]:
 s.frame_set(f);bpy.context.view_layer.update();print(f,o['sole_clearance'],tuple(o.location))
