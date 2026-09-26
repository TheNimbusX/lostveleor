"""Forehead contact, supported recoil and 1.2 s stun on the accepted rig."""
import bpy,json,math,sys,ast,hashlib
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion,Euler
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];WINDUP=HERE.parent/'windup_start';RUN=HERE.parent/'charge_loop';OUT=PROD/'review'/'animation_collision'
MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'keys'
tags=('front_left','front_right','hind_left','hind_right')
sources=[WINDUP/'Stonehoof_WindupStart_r01.blend',RUN/'Stonehoof_ChargeLoop_r01.blend',HERE.parent/'brake'/'Stonehoof_Brake_r01.blend'];hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
def snapshot():
 a=bpy.data.objects['ARM_ForestStonehoof'];return {'bones':{b.name:b.matrix.copy() for b in a.pose.bones if b.bone.use_deform},'helpers':{t:{'hip':a.pose.bones['MCH_Upper_'+t].head.copy(),'knee':a.pose.bones['MCH_Lower_'+t].head.copy(),'ankle':a.pose.bones['MCH_Lower_'+t].tail.copy()} for t in tags}}
bpy.ops.wm.open_mainfile(filepath=str(sources[0]));bpy.context.scene.frame_set(0);bpy.context.view_layer.update();end_pose=snapshot()
end_poles={t:bpy.data.objects['ARM_ForestStonehoof'].pose.bones['MCH_Lower_'+t].constraints[0].pole_angle for t in tags}
bpy.ops.wm.open_mainfile(filepath=str(sources[1]));bpy.context.scene.frame_set(0);bpy.context.view_layer.update();start_pose=snapshot()
bpy.context.preferences.filepaths.save_version=0;scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only']
rest={b.name:b.matrix_local.copy() for b in arm.data.bones};legs={}
for tag in tags:
 p='leg_'+tag+'_';foot=bpy.data.objects['CTRL_Hoof_'+tag];ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[p+'bot2'].index and g.weight>.95 for g in v.groups)]
 legs[tag]={'prefix':p,'foot':foot,'pole':bpy.data.objects['CTRL_Pole_'+tag],'ankle':Vector(arm.data.bones[p+'bot2'].head_local),'rest_rotation':rest[p+'bot2'].to_quaternion(),'hoof_ids':ids,'sole_z':min(mesh.data.vertices[i].co.z for i in ids)}
prefix=[]
body_names=('body','body_top0','body_top1','body_bot','neck0','head0','tail0')
for i in range(65):
 f=i/8;scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
 prefix.append((f,{n:(arm.pose.bones[n].location.copy(),arm.pose.bones[n].rotation_quaternion.copy()) for n in body_names},{t:(leg['foot'].location.copy(),leg['foot'].rotation_quaternion.copy(),float(leg['foot']['sole_clearance']),arm.pose.bones['MCH_Lower_'+t].constraints[0].pole_angle) for t,leg in legs.items()}))
brace_entry=snapshot()
for o in [arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]:o.animation_data_clear()
carrier.location=(0,0,0)
tree=ast.parse((WINDUP/'build_clip.py').read_text(encoding='utf8'))
for node in tree.body:
 if isinstance(node,ast.FunctionDef) and node.name in ('body_pose','hoof_key'):exec(compile(ast.Module(body=[node],type_ignores=[]),str(WINDUP/'build_clip.py'),'exec'))
poses=[
 (10,(0,-.145,.045),-3,0,-1,7,6,1),
 (12,(0,-.270,.070),-1,0,0,16,12,0),
 (13,(.006,-.200,-.010),1,.5,2,8,3,6),
 (14,(.012,-.080,-.100),4,1.2,2,3,-6,9),
 (16,(.014,.060,-.170),5,1.5,1,1,-5,-7),
 (19,(-.014,.080,-.150),4,-1.2,1,1,-4,-5),
 (22,(-.010,.040,-.140),3,-1,0,3,2,3),
 (26,(.018,.010,-.130),4,1.4,1,4,3,4),
 (30,(-.012,.020,-.130),3,-1,1,4,2,-3),
 (34,(.006,0,-.115),3,.6,.5,2,-1,-1),
 (38,(-.003,0,-.090),2,-.3,.5,0,0,2),
 (42,(.002,0,-.045),1,.2,0,-1,-.5,-1),
 (45,(0,0,-.012),-.5,0,-.2,1,.5,.5),
 (48,(0,0,0),0,0,0,0,0,0)]
tracks={
 'front_left':[(10,-.03,.23,-5),(12,-.05,.20,0),(13,-.035,.07,-2),(14,0,0.,0),(16,0,0.,0),(36,0,0.,0),(48,0,0.,0)],
 'front_right':[(10,-.04,.22,-6),(12,-.05,.21,0),(13,-.035,.09,-2),(14.5,0,0.,0),(16,0,0.,0),(36,0,0.,0),(48,0,0.,0)],
 'hind_left':[(10,.20,.28,25),(12,.10,.26,15),(13,.10,.16,10),(14.5,.04,.05,2),(15.5,0,0.,0),(16,0,0.,0),(36,0,0.,0),(48,0,0.,0)],
 'hind_right':[(10,.20,.25,25),(12,.10,.25,15),(13.5,.11,.14,8),(15,.03,.04,2),(16,0,0.,0),(36,0,0.,0),(48,0,0.,0)]}
contacts={'front_left':14,'front_right':14.5,'hind_left':15.5,'hind_right':16}
head_sway={10:0,12:0,13:0,14:1,16:-2,19:-6,22:7,26:-7,30:4.5,34:-2.5,38:1.2,42:-.4,45:.2,48:0}
for row in poses:
 body_pose(*row)
 f=row[0];head=arm.pose.bones['head0'];r=rest['head0'].to_quaternion();q=Euler((math.radians(row[6]),math.radians(head_sway[f]*.23),math.radians(head_sway[f])),'XYZ').to_quaternion();head.rotation_quaternion=r.inverted()@q@r;head.keyframe_insert('rotation_quaternion',frame=f,group='head0')
for tag,keys in tracks.items():
 for row in keys:hoof_key(tag,row[0],row[1],float(row[2]),row[3])
# Preserve the approved early stride exactly at every eighth-frame sample.
for f,bodies,feet in prefix:
 for name,(loc,q) in bodies.items():
  pb=arm.pose.bones[name];pb.location=loc;pb.rotation_quaternion=q;pb.keyframe_insert('location',frame=f,group=name);pb.keyframe_insert('rotation_quaternion',frame=f,group=name)
 for tag,(loc,q,lift,pole) in feet.items():
  obj=legs[tag]['foot'];obj.location=loc;obj.rotation_quaternion=q;obj['sole_clearance']=float(lift);obj.keyframe_insert('location',frame=f);obj.keyframe_insert('rotation_quaternion',frame=f);obj.keyframe_insert('["sole_clearance"]',frame=f)
  con=arm.pose.bones['MCH_Lower_'+tag].constraints[0];con.pole_angle=pole;con.keyframe_insert('pole_angle',frame=f)
def curves(o):
 if not o.animation_data or not o.animation_data.action:return []
 return [fc for layer in o.animation_data.action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]
for o in [arm]+[leg['foot'] for leg in legs.values()]:
 o.animation_data.action.name='AN_Stonehoof_Collision_'+('Rig' if o==arm else o.name);o.animation_data.action.use_fake_user=True
 for fc in curves(o):
  for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
for b in arm.pose.bones:
 for c in b.constraints:
  c.influence=1;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=48)
# Hoof height follows its actual rotated sole, not an independent ankle curve.
sampled={tag:[] for tag in tags}
for i in range(385):
 f=i/8;scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
 for tag,leg in legs.items():
  foot=leg['foot'];delta=foot.rotation_quaternion@leg['rest_rotation'].inverted();low=min((delta@(mesh.data.vertices[v].co-leg['ankle'])).z for v in leg['hoof_ids'])
  sampled[tag].append((f,leg['sole_z']+foot['sole_clearance']-low))
for tag,leg in legs.items():
 foot=leg['foot']
 for layer in foot.animation_data.action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for fc in list(bag.fcurves):
     if fc.data_path=='location' and fc.array_index==2:bag.fcurves.remove(fc)
 for f,z in sampled[tag]:foot.location.z=z;foot.keyframe_insert('location',index=2,frame=f)
 for fc in curves(foot):
  if fc.data_path=='location' and fc.array_index==2:
   for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
# Gradually recover the initial rig's standing bend plane after the skid.
def plane(ref):
 a=(ref['ankle']-ref['hip']).normalized();v=ref['knee']-ref['hip'];return a,(v-a*v.dot(a)).normalized()
for tag,leg in legs.items():
 a0,v0=plane(brace_entry['helpers'][tag]);a1,v1=plane(end_pose['helpers'][tag]);lower=arm.pose.bones['MCH_Lower_'+tag];constraint=lower.constraints[0];base_angle=constraint.pole_angle;previous=base_angle;keys=[]
 for i in range(64,385):
  f=i/8;scene.frame_set(math.floor(f),subframe=f-math.floor(f));constraint.pole_angle=base_angle;bpy.context.view_layer.update()
  hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();axis=(leg['foot'].location-hip).normalized();vstart=a0.rotation_difference(axis)@v0;vend=a1.rotation_difference(axis)@v1
  u=(f-8)/40;t=u*u*(3-2*u);angle=math.atan2(axis.dot(vstart.cross(vend)),vstart.dot(vend));base=Quaternion(axis,angle*t)@vstart
  sag=Vector((0,1,0));sag=(sag-axis*sag.dot(axis)).normalized();angle=math.atan2(axis.dot(base.cross(sag)),base.dot(sag));desired=Quaternion(axis,angle*.75*math.sin(math.pi*u)**2)@base
  def bend():
   v=lower.head-hip;return (v-axis*v.dot(axis)).normalized()
  current=bend();constraint.pole_angle=base_angle+.01;bpy.context.view_layer.update();probe=bend();sign=1 if axis.dot(current.cross(probe))>0 else -1
  angle=base_angle+math.atan2(axis.dot(current.cross(desired)),current.dot(desired))/sign
  if f==48:angle=end_poles[tag]
  while angle-previous>math.pi:angle-=math.tau
  while angle-previous< -math.pi:angle+=math.tau
  keys.append((f,angle));previous=angle
 for f,angle in keys:constraint.pole_angle=angle;constraint.keyframe_insert('pole_angle',frame=f)
for fc in curves(arm):
 if fc.data_path.endswith('pole_angle'):
  for k in fc.keyframe_points:k.interpolation='LINEAR'
def travel(f):return -.4*min(f,12)
for i in range(193):
 f=i/4;carrier.location=(0,travel(f),0);carrier.keyframe_insert('location',frame=f)
for fc in curves(carrier):
 for k in fc.keyframe_points:k.interpolation='LINEAR'
joins={}
for f,ref,label in [(0,start_pose,'from_accepted_gallop_phase_0'),(48,end_pose,'to_accepted_stance')]:
 scene.frame_set(f);bpy.context.view_layer.update();joins[label]={'max_joint_position_m':max((arm.pose.bones[n].matrix.translation-m.translation).length for n,m in ref['bones'].items()),'max_matrix_error':max(abs(arm.pose.bones[n].matrix[i][j]-m[i][j]) for n,m in ref['bones'].items() for i in range(4) for j in range(4))}
scene.frame_start=0;scene.frame_end=48;scene.render.fps=30;scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
for marker in list(scene.timeline_markers):scene.timeline_markers.remove(marker)
for f,name in [(0,'Вход из галопа'),(8,'Опустить каменный лоб'),(12,'Контакт / начало оглушения'),(14,'Передние копыта ловят вес'),(16,'Четыре опоры'),(26,'Оглушение'),(40,'Выравнивание'),(48,'Конец 1,2 с / стойка')]:scene.timeline_markers.new(name,frame=f)
arm['clip_status']='Wall contact and 1.2-second stun candidate; owner approval pending.';arm['runtime_action']='AN_Stonehoof_WallImpact source 12..48, AN_Stonehoof_WallBrace source 8..12. Preview travel is separate.'
# Place a fixed review obstacle against the actual leading forehead vertex.
scene.frame_set(12);bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();lead=min(range(len(me.vertices)),key=lambda i:(ev.matrix_world@me.vertices[i].co).y);hit_point=ev.matrix_world@me.vertices[lead].co;wall_y=hit_point.y;contact_rest=list(mesh.data.vertices[lead].co);ev.to_mesh_clear()
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,wall_y-.32,.80));wall=bpy.context.object;wall.name='QA_Wall_ReviewOnly';wall.dimensions=(1.08,.64,1.60);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
bevel=wall.modifiers.new('Soft stone edges','BEVEL');bevel.width=.035;bevel.segments=3
wallmat=bpy.data.materials.new('MAT_QA_Stone');wallmat.use_nodes=True;wallmat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.19,.22,.20,1);wallmat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9;wall.data.materials.append(wallmat);wall['review_only']=True
report={'source_sha256':hashes,'joins':joins,'contacts':contacts,'frames':48,'fps':30,'impact_frame':12,'stun_frames':36,'wall_y':wall_y,'forehead_contact_vertex':lead,'forehead_contact_rest':contact_rest,'forehead_contact_world':list(hit_point),'owner_approved':False,'phase_transition_note':'Canonical phase shown. Runtime blends arbitrary gait phase and uses Sim collision tick, with no delayed contact. WallBrace is optional within running time, never extra preparation.'}
(HERE/'build.json').write_text(json.dumps(report,indent=2));(HERE/'pose_keys.json').write_text(json.dumps({'body':poses,'hooves':tracks},indent=2))
cam=scene.camera
def camera(view,offset_y=0):
 target=Vector((0,offset_y-.30,.72));offset=Vector((8,3,2.3)) if view=='side' else Vector((7,4.5,7));cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.9
scene.frame_set(0);camera('side');bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_Collision_r01.blend'))
verts=[];faces=[]
for step in range(-32,8):
 y=step*1.2;n=len(verts);verts.extend([(-6,y-.012,-.002),(6,y-.012,-.002),(6,y+.012,-.002),(-6,y+.012,-.002)]);faces.append((n,n+1,n+2,n+3))
grid=bpy.data.meshes.new('QA_FloorMarks');grid.from_pydata(verts,[],faces);go=bpy.data.objects.new('QA_FloorMarks',grid);scene.collection.objects.link(go)
mat=bpy.data.materials.new('MAT_QA_FloorMarks');mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.035,.045,.045,1);mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9;go.data.materials.append(mat)
lights={o:o.location.copy() for o in scene.objects if o.type=='LIGHT'}
if MODE in ('keys','all'):
 for view in ('side','game'):
  folder=OUT/(view+'_frames');folder.mkdir(exist_ok=True)
  for f in (range(49) if MODE=='all' else (0,8,10,12,13,14,16,19,26,34,42,48)):
   scene.frame_set(f);ty=travel(f);camera(view,ty)
   for light,base in lights.items():light.location=base+Vector((0,ty,0))
   bpy.context.view_layer.update();scene.render.filepath=str(folder/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in hashes.items())
print('COLLISION_BUILT',json.dumps(report))
