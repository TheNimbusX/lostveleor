import bpy,json,math,hashlib
from pathlib import Path
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE.parent/'windup_start'/'Stonehoof_WindupStart_r01.blend'))
bpy.context.scene.frame_set(0);arm=bpy.data.objects['ARM_ForestStonehoof'];start={b.name:b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Death_r02.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];previous={};rows=[];max_step=(0,None,None);length_error=0;hoof_error=0;hoof_at=None
for i in range(481):
 f=i/8;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();low=min((v.co.z,v.index) for v in me.vertices);ev.to_mesh_clear()
 if i==0:entry=max((arm.pose.bones[n].matrix.translation-m.translation).length for n,m in start.items())
 for b in arm.pose.bones:
  if not b.bone.use_deform:continue
  q=b.matrix.to_quaternion().normalized();length_error=max(length_error,abs((b.tail-b.head).length/b.bone.length-1))
  if b.name in previous:
   angle=math.degrees(2*math.acos(min(1,abs(q.dot(previous[b.name])))))
   if angle>max_step[0]:max_step=(angle,f,b.name)
  previous[b.name]=q
 for tag in ('front_left','front_right','hind_left','hind_right'):
  e=(arm.pose.bones['leg_'+tag+'_bot2'].head-bpy.data.objects['CTRL_Hoof_'+tag].location).length
  if e>hoof_error:hoof_error=e;hoof_at=(f,tag)
 rows.append({'frame':f,'minimum_z':low,'root':list(arm.pose.bones['body'].matrix.translation)})
report={'samples':len(rows),'minimum_mesh_z':min((r['minimum_z'][0],r['frame'],r['minimum_z'][1]) for r in rows),'entry_joint_error_m':entry,'bone_length_error':length_error,'max_hoof_target_error_m':hoof_error,'max_hoof_error_at':hoof_at,'max_eighth_frame_rotation_degrees':max_step,'detail':rows,'owner_approved':False}
(HERE/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='detail'}))
