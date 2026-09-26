"""Derived runtime package; accepted animation masters are read-only inputs."""
import bpy,math,json,hashlib,ast
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
HERE=Path(__file__).resolve().parent;P=HERE.parents[1];A=HERE.parent
UNITY=Path('C:/Users/d.grab/Desktop/the-game/razlom/Assets/Resources/Characters/Forest_Stonehoof');UNITY.mkdir(parents=True,exist_ok=True)
SOURCES={'Windup':('windup_start/Stonehoof_WindupStart_Baked_r01.blend','AN_Stonehoof_Windup'),'Launch':('windup_start/Stonehoof_WindupStart_Baked_r01.blend','AN_Stonehoof_Launch'),'ChargeLoop':('charge_loop/Stonehoof_ChargeLoop_Baked_r01.blend','AN_Stonehoof_ChargeLoop'),'Brake':('brake/Stonehoof_Brake_Baked_r01.blend','AN_Stonehoof_Brake'),'WallBrace':('collision/Stonehoof_Collision_Baked_r01.blend','AN_Stonehoof_WallBrace'),'WallImpact':('collision/Stonehoof_Collision_Baked_r01.blend','AN_Stonehoof_WallImpact'),'Death':('death_r02/Stonehoof_Death_Baked_r02.blend','AN_Stonehoof_Death')}
bpy.ops.wm.open_mainfile(filepath=str(A/'windup_start/Stonehoof_WindupStart_r01.blend'));bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only'];scene.frame_set(0)
rest={b.name:b.matrix_local.copy() for b in arm.data.bones};names=[b.name for b in arm.data.bones if b.use_deform]
base={b.name:b.matrix_basis.copy() for b in arm.pose.bones};legs={};planes={};tags=('front_left','front_right','hind_left','hind_right')
for tag in tags:
 prefix='leg_'+tag+'_';foot=bpy.data.objects['CTRL_Hoof_'+tag];ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[prefix+'bot2'].index and g.weight>.95 for g in v.groups)]
 legs[tag]={'prefix':prefix,'foot':foot,'pole':bpy.data.objects['CTRL_Pole_'+tag],'ankle':Vector(arm.data.bones[prefix+'bot2'].head_local),'rest_rotation':rest[prefix+'bot2'].to_quaternion(),'hoof_ids':ids,'sole_z':min(mesh.data.vertices[i].co.z for i in ids)}
 upper=arm.data.bones['MCH_Upper_'+tag];lower=arm.data.bones['MCH_Lower_'+tag];axis=(lower.tail_local-upper.head_local).normalized();v=lower.head_local-upper.head_local
 planes[tag]=(axis,(v-axis*v.dot(axis)).normalized(),upper.length,lower.length)
helper=ast.parse((A/'windup_start/build_clip.py').read_text(encoding='utf8'))
for node in helper.body:
 if isinstance(node,ast.FunctionDef) and node.name in ('body_pose','hoof_key'):exec(compile(ast.Module(body=[node],type_ignores=[]),'authoring','exec'))
# body_pose needs Euler for artist-editable body keys.
from mathutils import Euler
controls=[arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]
new_samples={};durations={'Idle':90,'Walk':18,'Hit':12};contacts={};minimum_ground={};walk_feet={t:[] for t in tags}
for role,end in durations.items():
 for ob in controls:ob.animation_data_clear()
 carrier.location=(0,0,0)
 for b in arm.pose.bones:
  b.matrix_basis=base[b.name]
  for c in b.constraints:c.influence=0 if b.name.startswith('MCH_') else 1
 for tag,l in legs.items():hoof_key(tag,0)
 for f in range(-end,end*2+1):
  if role=='Walk':
   phase=f/end;wave=math.sin(phase*math.tau)
   body_pose(f,(.012*wave,0,-.12+.016*math.cos(phase*math.tau*2)),1.2*math.sin(phase*math.tau*2),1.8*wave,0,-.8*wave,0,3*wave)
   for tag,off in [('front_left',0),('front_right',.5),('hind_left',.25),('hind_right',.75)]:
    u=(phase+off)%1;duty=.6;span=1.0*duty
    if u<duty:dy=-span/2+1.0*u;dz=0;pitch=0
    else:
     t=(u-duty)/(1-duty);smooth=t*t*(3-2*t);dy=span/2-span*smooth;dz=.13*math.sin(math.pi*t)**2;pitch=-10*math.sin(math.pi*t)
    hoof_key(tag,f,dy+(.18 if tag.startswith("front") else 0),dz,pitch)
  elif role=='Idle':
   v=math.sin(math.tau*f/end);body_pose(f,(0,0,-.009+.008*v),.35*v,.25*math.cos(math.tau*f/end),0,-.5*v,.35*v,2*v)
   for tag in tags:hoof_key(tag,f)
  else:
   t=max(0,min(1,f/end));w=math.sin(math.pi*t)*math.exp(-2.4*t)*2
   body_pose(f,(0,.035*w,-.045*w),-2*w,1*w,0,-2*w,2*w,2*w)
   for tag in tags:hoof_key(tag,f)
 for ob in [arm]+[l['foot'] for l in legs.values()]:
  for lay in ob.animation_data.action.layers:
   for st in lay.strips:
    for bag in st.channelbags:
     for fc in bag.fcurves:
      for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
 samples=[];solved=[];minz=100;maxreach=0;reach_data={}
 for i in range(end*4+1):
  f=i/4;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
  for tag,l in legs.items():
   upper=arm.pose.bones['MCH_Upper_'+tag];lower=arm.pose.bones['MCH_Lower_'+tag];hip=upper.head.copy();foot=l['foot'];target=foot.location.copy();q=foot.rotation_quaternion@l['rest_rotation'].inverted()
   low=min((q@(mesh.data.vertices[v].co-l['ankle'])).z for v in l['hoof_ids']);target.z=l['sole_z']+foot['sole_clearance']-low;foot.location=target
   axis0,bend0,l1,l2=planes[tag];axis=(target-hip).normalized();d=(target-hip).length;maxreach=max(maxreach,max(0,d-l1-l2))
   if d-l1-l2>reach_data.get(tag,{}).get("error",0):reach_data[tag]={"frame":f,"error":d-l1-l2,"hip":list(hip),"target":list(target),"lengths":[l1,l2]}
   d=max(abs(l1-l2)+.0001,min(l1+l2-.00001,d));target=hip+axis*d
   bend=axis0.rotation_difference(axis)@bend0;along=(l1*l1-l2*l2+d*d)/(2*d);h=math.sqrt(max(0,l1*l1-along*along));knee=hip+axis*along+bend*h
   for bone,pos,to in ((upper,hip,knee),(lower,knee,target)):
    rb=arm.data.bones[bone.name];mat=((rb.tail_local-rb.head_local).normalized().rotation_difference((to-pos).normalized())@rb.matrix_local.to_quaternion()).to_matrix().to_4x4();mat.translation=pos;bone.matrix=mat;bpy.context.view_layer.update()
   if role=='Walk':walk_feet[tag].append(list(arm.pose.bones[l['prefix']+'bot2'].head))
  dg=bpy.context.evaluated_depsgraph_get();ev=mesh.evaluated_get(dg);me=ev.to_mesh();minz=min(minz,min(v.co.z for v in me.vertices));ev.to_mesh_clear()
  samples.append((f,{n:arm.pose.bones[n].matrix.copy() for n in names}))
  solved.append((f,{n:arm.pose.bones[n].matrix_basis.copy() for n in arm.pose.bones.keys() if n.startswith('MCH_')}, {t:l['foot'].location.copy() for t,l in legs.items()}))
 # Save editable control scene with the solved fixed-length limb keys, never overwrite a reviewed clip.
 for f,bs,feet in solved:
  for n,mat in bs.items():
   b=arm.pose.bones[n];b.matrix_basis=mat;b.rotation_mode='QUATERNION';b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f)
  for tag,pos in feet.items():legs[tag]['foot'].location=pos;legs[tag]['foot'].keyframe_insert('location',frame=f)
 scene.frame_start=0;scene.frame_end=end;scene.frame_set(0)
 bpy.ops.wm.save_as_mainfile(filepath=str(HERE/('Stonehoof_'+role+'_Controls.blend')))
 new_samples[role]=samples;minimum_ground[role]={'minimum_skin_z':minz,'maximum_unreachable_target':maxreach,'reach_data':reach_data}
 print('AUTHORED',role,minimum_ground[role],flush=True)
# Start the final package from the accepted rig and weights.
bpy.ops.wm.open_mainfile(filepath=str(A/'windup_start/Stonehoof_WindupStart_Baked_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];arm.animation_data_clear();arm.animation_data_create();arm.parent=None;arm.matrix_world=Matrix.Identity(4)
for b in arm.pose.bones:
 for c in list(b.constraints):b.constraints.remove(c)
for ob in list(bpy.data.objects):
 if ob.name.startswith(('CTRL_','QA_')):bpy.data.objects.remove(ob,do_unlink=True)
clips={};hashes={}
for role,(file,name) in SOURCES.items():
 path=A/file;hashes[file]=hashlib.sha256(path.read_bytes()).hexdigest()
 if path.resolve()==Path(bpy.data.filepath).resolve():act=bpy.data.actions[name].copy()
 else:
  with bpy.data.libraries.load(str(path),link=False) as (src,dst):
   assert name in src.actions,(file,name,src.actions);dst.actions=[name]
  act=dst.actions[0]
 clips[role]=act;act.name='Stonehoof_'+role;act.use_fake_user=True
for role,samples in new_samples.items():
 act=bpy.data.actions.new('Stonehoof_'+role);act.use_fake_user=True;arm.animation_data.action=act;previous={}
 for f,pose in samples:
  for n in names:
   b=arm.pose.bones[n];b.rotation_mode='QUATERNION';b.matrix=pose[n];b.scale=(1,1,1);q=b.rotation_quaternion.copy()
   if n in previous and q.dot(previous[n])<0:q.negate()
   b.rotation_quaternion=q;previous[n]=q;b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f);bpy.context.view_layer.update()
 act.use_frame_range=True;act.frame_start=0;act.frame_end=durations[role];clips[role]=act
for role,act in clips.items():
 for lay in act.layers:
  for st in lay.strips:
   for bag in st.channelbags:
    for fc in list(bag.fcurves):
     if fc.data_path.endswith('.scale'):bag.fcurves.remove(fc)
for act in list(bpy.data.actions):
 if act not in clips.values():bpy.data.actions.remove(act)
for im in bpy.data.images:
 if im.source=='FILE' and im.size[0]>1024:
  im.filepath_raw=str(UNITY/'Stonehoof_BaseColor.png');im.file_format='PNG';im.save();im.pack();break
arm.animation_data.action=clips['Idle'];scene.render.fps=30;scene.frame_start=0;scene.frame_end=90;scene.frame_set(0)
guide=bpy.data.objects.new('FacingGuide',None);scene.collection.objects.link(guide);guide.parent=arm;guide.location=(0,-1,0)
arm['root_motion']='Object travel removed; Sim owns arena motion.'
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ForestStonehoof_Production.blend'))
bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);mesh.select_set(True);guide.select_set(True);bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(UNITY/'ForestStonehoof.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},add_leaf_bones=False,use_armature_deform_only=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=.25,bake_anim_simplify_factor=0,mesh_smooth_type='FACE',use_mesh_modifiers=True,path_mode='AUTO')
report={'source_sha256':hashes,'clips':{k:list(v.frame_range) for k,v in clips.items()},'triangles':sum(len(p.vertices)-2 for p in mesh.data.polygons),'bones':len(names),'fps':30,'root_motion':False,'new_locomotion':minimum_ground,'walk_stride_m':1.0,'walk_feet':walk_feet}
(HERE/'export.json').write_text(json.dumps(report,indent=2));print('PACKAGE_EXPORTED',flush=True)
