"""Author a fixed-length hoof gallop on the accepted rig, keeping windup immutable."""
import bpy,json,math,sys,ast,hashlib
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion,Euler
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];PREV=HERE.parent/'windup_start'
OUT=PROD/'review'/'animation_charge_loop';OUT.mkdir(exist_ok=True)
MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'keys'
master=PREV/'Stonehoof_WindupStart_r01.blend';source_hash=hashlib.sha256(master.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(master));bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only']
scene.frame_set(36);bpy.context.view_layer.update()
accepted={b.name:b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
accepted_helpers={tag:{'hip':arm.pose.bones['MCH_Upper_'+tag].head.copy(),'knee':arm.pose.bones['MCH_Lower_'+tag].head.copy(),'ankle':arm.pose.bones['MCH_Lower_'+tag].tail.copy()} for tag in ('front_left','front_right','hind_left','hind_right')}
rest={b.name:b.matrix_local.copy() for b in arm.data.bones}
legs={}
for region in ('front','hind'):
 for side in ('left','right'):
    tag=f'{region}_{side}';prefix='leg_'+tag+'_';foot=bpy.data.objects['CTRL_Hoof_'+tag]
    ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[prefix+'bot2'].index and g.weight>.95 for g in v.groups)]
    legs[tag]={'prefix':prefix,'foot':foot,'pole':bpy.data.objects['CTRL_Pole_'+tag],'ankle':Vector(arm.data.bones[prefix+'bot2'].head_local),'rest_rotation':rest[prefix+'bot2'].to_quaternion(),'hoof_ids':ids,'sole_z':min(mesh.data.vertices[i].co.z for i in ids)}
for obj in [arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]:obj.animation_data_clear()
carrier.location=(0,0,0)
# Reuse the two vetted authoring operations, not the old build's side effects.
tree=ast.parse((PREV/'build_clip.py').read_text(encoding='utf8'))
for node in tree.body:
 if isinstance(node,ast.FunctionDef) and node.name in ('body_pose','hoof_key'):
    exec(compile(ast.Module(body=[node],type_ignores=[]),str(PREV/'build_clip.py'),'exec'))

poses=[
 (0,(0,-.060,.070),-1,0,0,-2,0,0),
 (1,(.006,-.056,.078),0,0,-.5,-1,0,-2),
 (2,(.018,-.047,.025),2,.7,1,0,.5,-4),
 (3,(.025,-.030,-.060),4,1.2,2,1,1,-2),
 (4.5,(-.020,-.005,-.145),5,-1.0,3,3,2,3),
 (6,(.010,.020,-.135),-2,.5,1,1,0,4),
 (7.5,(-.018,.015,-.160),-5,-.8,-2,-1,-1,1),
 (9,(0,-.020,.005),-6,0,-3,-3,-1,-4),
 (10.5,(0,-.053,.055),-3,0,-1,-4,-.5,-3),
 (12,(0,-.060,.070),-1,0,0,-2,0,0)]
tracks={
 'front_left':[(0,-.28,.36,-20),(1,-.25,.28,-12),(2,-.16,.10,-4),(3,-.10,0,0),(4.5,.50,0,0),(4.8,.56,.035,8),(5.4,.45,.07,20),(6.5,.20,.12,15),(8,-.20,.28,-12),(10,-.37,.40,-30),(12,-.28,.36,-20)],
 'front_right':[(0,-.32,.33,-24),(1.4,-.25,.28,-16),(2.6,-.18,.10,-7),(3.7,-.10,0,0),(5.2,.50,0,0),(5.5,.55,.035,8),(6.4,.40,.09,20),(7.5,.10,.18,15),(9,-.22,.34,-5),(11,-.34,.36,-27),(12,-.32,.33,-24)],
 'hind_left':[(0,.12,.36,30),(1.5,-.15,.43,20),(3,-.30,.32,0),(4.5,-.32,.22,-8),(5.6,-.38,.08,0),(6.1,-.28,0,0),(7.6,.32,0,0),(8,.39,.07,10),(8.6,.38,.16,25),(9.4,.32,.28,40),(10.5,.24,.37,40),(12,.12,.36,30)],
 'hind_right':[(0,.12,.36,30),(1.5,-.09,.38,22),(4,-.28,.30,-8),(5.7,-.32,.16,-4),(6.3,-.38,.07,0),(6.8,-.28,0,0),(7.9,.16,0,0),(8.3,.29,.07,10),(8.8,.35,.16,25),(10,.25,.29,45),(12,.12,.36,30)]}
contacts={'front_left':(3,4.5),'front_right':(3.7,5.2),'hind_left':(6.1,7.6),'hind_right':(6.8,7.9)}
# Neighbor copies produce identical tangents at the loop seam, not just matching poses.
for offset in (-12,0,12):
 for row in poses:body_pose(row[0]+offset,*row[1:])
 for tag,keys in tracks.items():
    for row in keys:hoof_key(tag,row[0]+offset,*row[1:])
def curves(obj):
 if not obj.animation_data or not obj.animation_data.action:return []
 return [fc for layer in obj.animation_data.action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]
for obj in [arm]+[leg['foot'] for leg in legs.values()]:
 obj.animation_data.action.name='AN_Stonehoof_ChargeLoop_'+('Rig' if obj==arm else obj.name)
 obj.animation_data.action.use_fake_user=True
 for fc in curves(obj):
    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
for tag,leg in legs.items():
 for fc in curves(leg['foot']):
    if fc.data_path!='location' or fc.array_index!=1:continue
    points=list(fc.keyframe_points)
    for n,k in enumerate(points):
        if any(abs(k.co.x-(f+off))<.001 for f in contacts[tag] for off in (-12,0,12)):
            left=(k.co.x-points[n-1].co.x)/3 if n else .1;right=(points[n+1].co.x-k.co.x)/3 if n+1<len(points) else .1
            k.handle_left_type='FREE';k.handle_right_type='FREE'
            k.handle_left=(k.co.x-left,k.co.y-.4*left);k.handle_right=(k.co.x+right,k.co.y+.4*right)
for p in arm.pose.bones:
 for c in p.constraints:
    c.influence=1;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=12)

# Retain the grounded sole correction used by the accepted first clip.
sampled={tag:[] for tag in legs}
for i in range(-48,97):
 f=i/4;scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
 for tag,leg in legs.items():
    foot=leg['foot'];delta=foot.rotation_quaternion@leg['rest_rotation'].inverted()
    low=min((delta@(mesh.data.vertices[v].co-leg['ankle'])).z for v in leg['hoof_ids'])
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
# Resolve pole roll along a continuous anatomical bend plane. A static pole in
# the original asymmetric rig can spin an elbow as a folded hoof passes it.
# Preserve the exact accepted takeoff pose at both ends; blend to a sagittal
# elbow/hock plane in the loaded part of the stride. Length remains fixed.
pole_samples={tag:[] for tag in legs}
for tag,leg in legs.items():
 ref=accepted_helpers[tag];a0=(ref['ankle']-ref['hip']).normalized();v0=ref['knee']-ref['hip'];v0=(v0-a0*v0.dot(a0)).normalized()
 lower=arm.pose.bones['MCH_Lower_'+tag];constraint=lower.constraints[0];base_angle=constraint.pole_angle;previous=base_angle
 for i in range(97):
  f=i/8;scene.frame_set(math.floor(f),subframe=f-math.floor(f));constraint.pole_angle=base_angle;bpy.context.view_layer.update()
  hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();axis=(leg['foot'].location-hip).normalized()
  carried=a0.rotation_difference(axis)@v0;sagittal=Vector((0,1,0));sagittal=(sagittal-axis*sagittal.dot(axis)).normalized()
  weight=math.sin(math.pi*f/12)**2
  desired=carried.lerp(sagittal,weight).normalized()
  def bend():
   v=lower.head-hip;return (v-axis*v.dot(axis)).normalized()
  current=bend();constraint.pole_angle=base_angle+.01;bpy.context.view_layer.update();probe=bend()
  sign=1 if axis.dot(current.cross(probe))>0 else -1
  delta=math.atan2(axis.dot(current.cross(desired)),current.dot(desired));angle=base_angle+delta/sign
  while angle-previous>math.pi:angle-=math.tau
  while angle-previous< -math.pi:angle+=math.tau
  pole_samples[tag].append((f,angle));previous=angle
 for off in (-12,0,12):
  for f,angle in pole_samples[tag]:constraint.pole_angle=angle;constraint.keyframe_insert('pole_angle',frame=f+off)
for fc in curves(arm):
 if fc.data_path.endswith('pole_angle'):
  for k in fc.keyframe_points:k.interpolation='LINEAR'
scene.frame_set(0);bpy.context.view_layer.update()
launch_join={n:{'position_m':(arm.pose.bones[n].matrix.translation-m.translation).length,'matrix_max':max(abs(arm.pose.bones[n].matrix[i][j]-m[i][j]) for i in range(4) for j in range(4))} for n,m in accepted.items()}
scene.frame_start=0;scene.frame_end=12;scene.render.fps=30
scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
for marker in list(scene.timeline_markers):scene.timeline_markers.remove(marker)
for f,name in [(0,'Переход из старта / полёт'),(3,'Передняя опора'),(6,'Подбор задних ног'),(8,'Толчок'),(12,'Начало следующего цикла')]:scene.timeline_markers.new(name,frame=f)
arm['clip_status']='Charge loop candidate: owner review required.';arm['runtime_action']='AN_Stonehoof_ChargeLoop, 12 frames at 30 fps; 12 m/s carrier is not root motion.'
report={'source_master_sha256':source_hash,'frames':12,'fps':30,'speed_m_s':12,'stride_m':4.8,'contacts':contacts,'launch_join':launch_join,'owner_approved':False}
(HERE/'build.json').write_text(json.dumps(report,indent=2));(HERE/'pose_keys.json').write_text(json.dumps({'body':poses,'hooves':tracks},indent=2))
cam=scene.camera
def camera(view,travel=0):
 target=Vector((0,travel,.72));offset=Vector((8,-1,2.3)) if view=='side' else Vector((6,-7,7))
 cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.35
camera('side');bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_ChargeLoop_r01.blend'))
# Low-contrast floor marks belong to the diagnostic set, never the game asset.
verts=[];faces=[]
for grid_step in range(-32,8):
 y=grid_step*1.2
 start=len(verts);verts.extend([(-6,y-.012,-.002),(6,y-.012,-.002),(6,y+.012,-.002),(-6,y+.012,-.002)]);faces.append((start,start+1,start+2,start+3))
grid=bpy.data.meshes.new('QA_FloorMarks');grid.from_pydata(verts,[],faces);go=bpy.data.objects.new('QA_FloorMarks',grid);scene.collection.objects.link(go)
mat=bpy.data.materials.new('MAT_QA_FloorMarks');mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.035,.045,.045,1);mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9;go.data.materials.append(mat)
lights={o:o.location.copy() for o in scene.objects if o.type=='LIGHT'}
def render(view,f,index=None):
 scene.frame_set(math.floor(f),subframe=f-math.floor(f));travel=-.4*f;carrier.location=(0,travel,0);camera(view,travel)
 for light,base in lights.items():light.location=base+Vector((0,travel,0))
 bpy.context.view_layer.update()
 folder=OUT/(view+'_frames');folder.mkdir(exist_ok=True);scene.render.filepath=str(folder/f'{(int(f) if index is None else index):04d}.png');bpy.ops.render.render(write_still=True)
if MODE=='keys':
 for view in ('side','game'):
    for f in (0,2,3,4,5,6,7,8,9,10,12):render(view,f)
elif MODE=='all':
 for view in ('side','game'):
    for f in range(13):render(view,f)
assert hashlib.sha256(master.read_bytes()).hexdigest()==source_hash
print('LOOP_BUILT',json.dumps({'join_position_m':max(x['position_m'] for x in launch_join.values()),'join_matrix_max':max(x['matrix_max'] for x in launch_join.values())}))
