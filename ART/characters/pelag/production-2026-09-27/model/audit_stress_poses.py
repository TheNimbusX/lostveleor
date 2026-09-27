"""Existing clip pose preflight only, does not create or replace animation assets."""
import bpy,json
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game');OUT=ROOT/'ART/characters/pelag/production-2026-09-27/model'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['ARM_Pelag_v6_Original65'];body=bpy.data.objects['SM_Pelag_v6_Unchanged'];anchor=bpy.data.objects['SM_Pelag_anchor_Candidate']
report=[]
scene.render.resolution_x=720;scene.render.resolution_y=820;scene.render.resolution_percentage=100;scene.cycles.samples=16
for filename,frame,label in [('Pelag_AN_CombatIdle.fbx',1,'idle'),('Pelag_AN_Cleave.fbx',8,'cleave_windup'),('Pelag_AN_Cleave.fbx',13,'cleave_contact'),('Pelag_AN_Roll.fbx',12,'roll_mid')]:
 before=set(bpy.data.objects);path=ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'/filename
 bpy.ops.import_scene.fbx(filepath=str(path),use_anim=True);src=next(o for o in set(bpy.data.objects)-before if o.type=='ARMATURE')
 for o in set(bpy.data.objects)-before:o.hide_render=True
 scene.frame_set(frame);bpy.context.view_layer.update()
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 bpy.context.view_layer.update()
 for p in rig.pose.bones:
  sp=src.pose.bones.get(p.name)
  if sp is None:continue
  m=sp.matrix.to_quaternion().to_matrix().to_4x4();m.translation=p.head.copy()
  if p.name=='mixamorig:Hips':
   ratio=p.bone.length/max(.000001,sp.bone.length);m.translation+=(sp.head-sp.bone.head_local)*ratio
  p.matrix=m;bpy.context.view_layer.update()
 deps=bpy.context.evaluated_depsgraph_get();ev=body.evaluated_get(deps);mesh=ev.to_mesh();points=[ev.matrix_world@v.co for v in mesh.vertices];tree=BVHTree.FromPolygons(points,[tuple(p.vertices) for p in mesh.polygons]);ev.to_mesh_clear()
 values=[];anchor_points=[];inside=0
 for v in anchor.data.vertices:
  p=anchor.matrix_world@v.co;anchor_points.append(p);pos,n,idx,d=tree.find_nearest(p);values.append((d,(p-pos).dot(n)))
  origin=p.copy();direction=Vector((.847,.113,.519)).normalized();hits=0
  for _ in range(16):
   hit=tree.ray_cast(origin,direction,100)
   if hit[0] is None:break
   hits+=1;origin=hit[0]+direction*.00001
  inside+=hits%2
 pictures=[]
 if label!='idle':
  for view,loc in [('game',(3,-4,4.2)),('side',(5,0,1.5))]:
   cam=scene.camera;cam.location=loc;cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.45
   filename_out='pose_preflight_'+label+'_'+view+'.png';scene.render.filepath=str(OUT/filename_out);bpy.ops.render.render(write_still=True);pictures.append(filename_out)
 report.append({'label':label,'existing_clip':str(path.relative_to(ROOT)),'sample_frame':frame,'min_anchor_body_surface_distance_m':min(x[0] for x in values),'anchor_vertices_inside_closed_body_ray_parity':inside,'anchor_floor_clearance_m':min(p.z for p in anchor_points),'body_floor_clearance_m':min(p.z for p in points),'pictures':pictures,'method':'existing clip bone orientations and hip offset mapped to unchanged v6 bone lengths; single-pose preflight only, not engine retargeting or moving/cancel acceptance'})
 for o in list(set(bpy.data.objects)-before):bpy.data.objects.remove(o,do_unlink=True)
for row in report:
 row['render_floor_z_m']=-.015
 row['hip_translation_applied']=True
 row['source_clip_accepted_as_new_animation']=False
 row['limitations']='Sampled poses only. Ray parity tests anchor vertices, not triangle penetration or swept collisions. This is not the Unity Animator or Sim movement/ground alignment.'
(OUT/'stress_pose_preflight.json').write_text(json.dumps(report,indent=2));print('STRESS',json.dumps(report))
candidate=json.loads((OUT/'candidate_report.json').read_text())
candidate['review_status']='static model candidate ready for review'
candidate['pose_compatibility']='NOT PASSED'
candidate['runtime_ready']=False
candidate['owner_accepted']=False
candidate['pose_preflight']={'report':'stress_pose_preflight.json','pictures':[p for row in report for p in row['pictures']],'limitations':report[0]['limitations'],'floor_penetration_samples':[{'label':r['label'],'anchor_min_z_m':r['anchor_floor_clearance_m'],'body_min_z_m':r['body_floor_clearance_m']} for r in report if r['anchor_floor_clearance_m']<-.02]}
candidate['open_findings']=['Existing Roll frame 12 intersects the ground with the back-carried anchor; new base movement must accommodate the approved weapon size.','Deep existing Cleave contact visually separates the rigid mount from the bent back. Back carry must be reviewed throughout new animation curves and action handoffs.','No Unity runtime, swept collision, cancellation or dynamic chain budget acceptance yet.']
(OUT/'candidate_report.json').write_text(json.dumps(candidate,indent=2),encoding='utf-8')
