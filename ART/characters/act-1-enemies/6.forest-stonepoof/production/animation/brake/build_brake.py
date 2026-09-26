"""Editable 0.6 s open-edge skid; accepted gallop and start remain immutable."""
import bpy,json,math,sys,ast,hashlib
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion,Euler
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];WINDUP=HERE.parent/'windup_start';RUN=HERE.parent/'charge_loop';OUT=PROD/'review'/'animation_brake'
MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'keys'
tags=('front_left','front_right','hind_left','hind_right')
sources=[WINDUP/'Stonehoof_WindupStart_r01.blend',RUN/'Stonehoof_ChargeLoop_r01.blend'];hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
def snapshot():
 a=bpy.data.objects['ARM_ForestStonehoof'];return {'bones':{b.name:b.matrix.copy() for b in a.pose.bones if b.bone.use_deform},'helpers':{t:{'hip':a.pose.bones['MCH_Upper_'+t].head.copy(),'knee':a.pose.bones['MCH_Lower_'+t].head.copy(),'ankle':a.pose.bones['MCH_Lower_'+t].tail.copy()} for t in tags}}
bpy.ops.wm.open_mainfile(filepath=str(sources[0]));bpy.context.scene.frame_set(0);bpy.context.view_layer.update();end_pose=snapshot()
bpy.ops.wm.open_mainfile(filepath=str(sources[1]));bpy.context.scene.frame_set(0);bpy.context.view_layer.update();start_pose=snapshot()
bpy.context.preferences.filepaths.save_version=0;scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only']
rest={b.name:b.matrix_local.copy() for b in arm.data.bones};legs={}
for tag in tags:
 p='leg_'+tag+'_';foot=bpy.data.objects['CTRL_Hoof_'+tag];ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[p+'bot2'].index and g.weight>.95 for g in v.groups)]
 legs[tag]={'prefix':p,'foot':foot,'pole':bpy.data.objects['CTRL_Pole_'+tag],'ankle':Vector(arm.data.bones[p+'bot2'].head_local),'rest_rotation':rest[p+'bot2'].to_quaternion(),'hoof_ids':ids,'sole_z':min(mesh.data.vertices[i].co.z for i in ids)}
for o in [arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]:o.animation_data_clear()
carrier.location=(0,0,0)
tree=ast.parse((WINDUP/'build_clip.py').read_text(encoding='utf8'))
for node in tree.body:
 if isinstance(node,ast.FunctionDef) and node.name in ('body_pose','hoof_key'):exec(compile(ast.Module(body=[node],type_ignores=[]),str(WINDUP/'build_clip.py'),'exec'))
poses=[
 (0,(0,-.060,.070),-1,0,0,-2,0,0),
 (1,(.004,-.058,.064),0,.3,0,-1,0,-2),
 (2,(.014,-.050,.006),2,.7,1,1,1,-3),
 (3,(.022,-.050,-.082),2,1.3,1.5,3,1,-2),
 (4.5,(.020,-.045,-.175),-1,1.5,-1,7,2,3),
 (6,(.008,-.025,-.200),-5,.5,-2,10,2,7),
 (8,(-.018,-.016,-.180),-6,-1.2,-1.5,9,1,4),
 (10,(-.010,-.008,-.128),-4,-1,-.8,5,0,-3),
 (12,(.006,0,-.066),-2.5,.4,-.5,1,-1,-4),
 (14,(.008,.006,-.025),-1.5,.6,-.3,-2,-.5,2),
 (16,(-.003,.002,-.004),.5,-.2,.3,1,.5,1),
 (18,(0,0,0),0,0,0,0,0,0)]
tracks={
 'front_left':[(0,-.28,.36,-20),(1,-.25,.28,-12),(2,-.16,.10,-4),(3,-.10,0,0),(4.5,-.14,0,0),(6,-.085,0,0),(8,-.045,0,0),(11,-.006,0,0),(14,0,0,0),(18,0,0,0)],
 'front_right':[(0,-.32,.33,-24),(1.4,-.25,.28,-16),(2.6,-.18,.10,-7),(3.7,-.10,0,0),(5,-.13,0,0),(7,-.06,0,0),(10,-.012,0,0),(14,0,0,0),(18,0,0,0)],
 'hind_left':[(0,.12,.36,30),(1.5,-.08,.30,18),(3,-.14,.12,3),(4.3,-.12,0,0),(6,.015,0,0),(8,.10,0,0),(10,.075,0,0),(12,.026,0,0),(14,0,0,0),(18,0,0,0)],
 'hind_right':[(0,.12,.36,30),(2,-.015,.23,14),(4,-.05,.07,2),(5,-.03,0,0),(7,.07,0,0),(9,.105,0,0),(12,.024,0,0),(14,0,0,0),(18,0,0,0)]}
contacts={'front_left':3,'front_right':3.7,'hind_left':4.3,'hind_right':5}
for row in poses:body_pose(*row)
for tag,keys in tracks.items():
 for row in keys:
  f=row[0];w=math.sin(math.pi*min(f,14)/14)**2;dx=(.025 if tag.endswith('left') else -.025)*w
  # ID properties inherit the assigned type. Integer zero truncates subsequent
  # interpolated sub-metre clearance values in Blender: always keep a float.
  hoof_key(tag,row[0],row[1],float(row[2]),row[3],dx=dx)
def curves(o):
 if not o.animation_data or not o.animation_data.action:return []
 return [fc for layer in o.animation_data.action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]
for o in [arm]+[leg['foot'] for leg in legs.values()]:
 o.animation_data.action.name='AN_Stonehoof_Brake_'+('Rig' if o==arm else o.name);o.animation_data.action.use_fake_user=True
 for fc in curves(o):
  for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
for b in arm.pose.bones:
 for c in b.constraints:
  c.influence=1;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=18)
# Hoof height follows its actual rotated sole, not an independent ankle curve.
sampled={tag:[] for tag in tags}
for i in range(145):
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
 a0,v0=plane(start_pose['helpers'][tag]);a1,v1=plane(end_pose['helpers'][tag]);lower=arm.pose.bones['MCH_Lower_'+tag];constraint=lower.constraints[0];base_angle=constraint.pole_angle;previous=base_angle;keys=[]
 for i in range(145):
  f=i/8;scene.frame_set(math.floor(f),subframe=f-math.floor(f));constraint.pole_angle=base_angle;bpy.context.view_layer.update()
  hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();axis=(leg['foot'].location-hip).normalized();vstart=a0.rotation_difference(axis)@v0;vend=a1.rotation_difference(axis)@v1
  t=f/18;t=t*t*(3-2*t);angle=math.atan2(axis.dot(vstart.cross(vend)),vstart.dot(vend));base=Quaternion(axis,angle*t)@vstart
  sag=Vector((0,1,0));sag=(sag-axis*sag.dot(axis)).normalized();angle=math.atan2(axis.dot(base.cross(sag)),base.dot(sag));desired=Quaternion(axis,angle*.75*math.sin(math.pi*f/18)**2)@base
  def bend():
   v=lower.head-hip;return (v-axis*v.dot(axis)).normalized()
  current=bend();constraint.pole_angle=base_angle+.01;bpy.context.view_layer.update();probe=bend();sign=1 if axis.dot(current.cross(probe))>0 else -1
  angle=base_angle+math.atan2(axis.dot(current.cross(desired)),current.dot(desired))/sign
  while angle-previous>math.pi:angle-=math.tau
  while angle-previous< -math.pi:angle+=math.tau
  keys.append((f,angle));previous=angle
 for f,angle in keys:constraint.pole_angle=angle;constraint.keyframe_insert('pole_angle',frame=f)
for fc in curves(arm):
 if fc.data_path.endswith('pole_angle'):
  for k in fc.keyframe_points:k.interpolation='LINEAR'
def travel(f):
 if f<=3:return -.4*f
 if f>=14:return -3.4
 t=(f-3)/30;return -(1.2+12*t-(180/11)*t*t)
for i in range(73):
 f=i/4;carrier.location=(0,travel(f),0);carrier.keyframe_insert('location',frame=f)
for fc in curves(carrier):
 for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
joins={}
for f,ref,label in [(0,start_pose,'from_accepted_gallop_phase_0'),(18,end_pose,'to_accepted_stance')]:
 scene.frame_set(f);bpy.context.view_layer.update();joins[label]={'max_joint_position_m':max((arm.pose.bones[n].matrix.translation-m.translation).length for n,m in ref['bones'].items()),'max_matrix_error':max(abs(arm.pose.bones[n].matrix[i][j]-m[i][j]) for n,m in ref['bones'].items() for i in range(4) for j in range(4))}
scene.frame_start=0;scene.frame_end=18;scene.render.fps=30;scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
for marker in list(scene.timeline_markers):scene.timeline_markers.remove(marker)
for f,name in [(0,'Вход из галопа'),(3,'Передние копыта принимают вес'),(5,'Четыре опоры / занос'),(8,'Гашение инерции'),(14,'Остановка'),(18,'Стойка')]:scene.timeline_markers.new(name,frame=f)
arm['clip_status']='Open-edge brake candidate, 0.6 s; owner approval pending.';arm['runtime_action']='AN_Stonehoof_Brake; Sim owns travel. Entry canonical gallop phase 0, final accepted stance.'
report={'source_sha256':hashes,'joins':joins,'contacts':contacts,'frames':18,'fps':30,'stop_frame':14,'braking_distance_preview_m':3.4,'owner_approved':False,'phase_transition_note':'Canonical phase-0 join shown. Runtime must blend arbitrary gait phase within the braking interval, never wait past the arena boundary.'}
(HERE/'build.json').write_text(json.dumps(report,indent=2));(HERE/'pose_keys.json').write_text(json.dumps({'body':poses,'hooves':tracks},indent=2))
cam=scene.camera
def camera(view,offset_y=0):
 target=Vector((0,offset_y,.72));offset=Vector((8,-1,2.3)) if view=='side' else Vector((6,-7,7));cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.35
scene.frame_set(0);camera('side');bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_Brake_r01.blend'))
verts=[];faces=[]
for step in range(-32,8):
 y=step*1.2;n=len(verts);verts.extend([(-6,y-.012,-.002),(6,y-.012,-.002),(6,y+.012,-.002),(-6,y+.012,-.002)]);faces.append((n,n+1,n+2,n+3))
grid=bpy.data.meshes.new('QA_FloorMarks');grid.from_pydata(verts,[],faces);go=bpy.data.objects.new('QA_FloorMarks',grid);scene.collection.objects.link(go)
mat=bpy.data.materials.new('MAT_QA_FloorMarks');mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.035,.045,.045,1);mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9;go.data.materials.append(mat)
lights={o:o.location.copy() for o in scene.objects if o.type=='LIGHT'}
if MODE in ('keys','all'):
 for view in ('side','game'):
  folder=OUT/(view+'_frames');folder.mkdir(exist_ok=True)
  for f in (range(19) if MODE=='all' else (0,2,3,5,7,9,11,14,16,18)):
   scene.frame_set(f);ty=travel(f);camera(view,ty)
   for light,base in lights.items():light.location=base+Vector((0,ty,0))
   bpy.context.view_layer.update();scene.render.filepath=str(folder/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in hashes.items())
print('BRAKE_BUILT',json.dumps(report))
