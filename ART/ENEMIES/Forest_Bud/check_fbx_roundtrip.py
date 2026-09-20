import bpy,json,math
from pathlib import Path
R=Path(r'C:/Users/d.grab/Desktop/the-game');P=R/'ART/ENEMIES/Forest_Bud';O=R/'artifacts/forest-bud-production'
bpy.ops.wm.open_mainfile(filepath=str(P/'ForestBudRanged_Production.blend'))
rig=bpy.data.objects['ARM_ForestBudRanged'];scene=bpy.context.scene
for tr in rig.animation_data.nla_tracks:tr.mute=True
frames={'Idle':[1,23,46,69,91],'Walk':[1,5,9,13,17,21,25,29,33],'Ranged_Attack':[1,13,20,25,31,37,43,49,58,67],'Death':[1,9,17,25,33,37]}
bones=[b.name for b in rig.data.bones if b.use_deform];reference={}
for name,ff in frames.items():
 ac=bpy.data.actions[name];rig.animation_data.action=ac;rig.animation_data.action_slot=ac.slots[0]
 for frame in ff:
  scene.frame_set(frame);bpy.context.view_layer.update();reference[(name,frame)]={b:list(rig.matrix_world@rig.pose.bones[b].matrix.translation) for b in bones}
for ob in list(bpy.data.objects):bpy.data.objects.remove(ob,do_unlink=True)
for action in list(bpy.data.actions):bpy.data.actions.remove(action)
bpy.ops.import_scene.fbx(filepath=str(R/'razlom/Assets/Resources/Characters/Forest_Bud/ForestBudRanged.fbx'),use_anim=True)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');scene=bpy.context.scene;report={'clips':[],'bone_count':len(rig.data.bones),'sockets':[b.name for b in rig.data.bones if b.name.startswith('Spawn_Fruit_')],'mesh_count':sum(o.type=='MESH' for o in bpy.data.objects)}
for name,ff in frames.items():
 ac=next(a for a in bpy.data.actions if a.name.endswith('|'+name));rig.animation_data.action=ac;rig.animation_data.action_slot=ac.slots[0]
 errors=[]
 for frame in ff:
  scene.frame_set(frame);bpy.context.view_layer.update()
  for bone in bones:
   actual=rig.matrix_world@rig.pose.bones[bone].matrix.translation;expected=reference[(name,frame)][bone]
   errors.append((actual-bpy.mathutils.Vector(expected)).length if hasattr(bpy,'mathutils') else math.sqrt(sum((actual[i]-expected[i])**2 for i in range(3))))
 report['clips'].append({'name':name,'frames':list(ac.frame_range),'max_bone_position_error_m':max(errors)})
report['pass']=all(c['max_bone_position_error_m']<.0001 for c in report['clips']) and len(report['sockets'])==5 and report['bone_count']==40
(O/'fbx_roundtrip.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('FOREST_FBX_ROUNDTRIP',json.dumps(report))
if not report['pass']:raise RuntimeError('FBX roundtrip error')
