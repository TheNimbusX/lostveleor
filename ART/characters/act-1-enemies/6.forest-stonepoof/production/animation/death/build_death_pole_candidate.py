"""Reference-led foreleg collapse, chest contact and hindquarter settling."""
import bpy,math,json,ast,sys,hashlib
from pathlib import Path
from mathutils import Vector,Quaternion,Euler
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];OUT=PROD/'review'/'animation_death';WINDUP=HERE.parent/'windup_start'
MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'keys'
source=WINDUP/'Stonehoof_WindupStart_r01.blend';source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.frame_set(0);arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only']
start={b.name:b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform};rest={b.name:b.matrix_local.copy() for b in arm.data.bones}
tags=('front_left','front_right','hind_left','hind_right');legs={};planes={}
for tag in tags:
    prefix='leg_'+tag+'_';foot=bpy.data.objects['CTRL_Hoof_'+tag];ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[prefix+'bot2'].index and g.weight>.95 for g in v.groups)]
    legs[tag]={'prefix':prefix,'foot':foot,'pole':bpy.data.objects['CTRL_Pole_'+tag],'ankle':Vector(arm.data.bones[prefix+'bot2'].head_local),'rest_rotation':rest[prefix+'bot2'].to_quaternion(),'hoof_ids':ids,'sole_z':min(mesh.data.vertices[i].co.z for i in ids)}
    hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();low=arm.pose.bones['MCH_Lower_'+tag];axis=(low.tail-hip).normalized();v=low.head-hip;planes[tag]=(axis,(v-axis*v.dot(axis)).normalized(),low.constraints[0].pole_angle)
for obj in [arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]:obj.animation_data_clear()
carrier.location=(0,0,0)
tree=ast.parse((WINDUP/'build_clip.py').read_text(encoding='utf8'))
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ('body_pose','hoof_key'):exec(compile(ast.Module(body=[node],type_ignores=[]),'helpers','exec'))
poses=[
 (0,(0,0,0),0,0,0,0,0,0),
 (4,(0,.010,-.025),-2,0,-1,-2,-2,1),
 (8,(.005,-.010,-.090),3,1,2,2,0,3),
 (12,(.015,-.040,-.180),8,2,3,0,-5,5),
 (16,(.025,-.060,-.310),15,3,2,-12,-20,7),
 (20,(.030,-.085,-.420),17,5,1,-16,-22,5),
 (24,(.035,-.090,-.500),12,7,0,-18,-22,-1),
 (28,(.040,-.100,-.560),7,9,0,-19,-22,-5),
 (32,(.045,-.120,-.600),1,12,0,-18,-5,-7),
 (36,(.047,-.130,-.625),-1,14,0,-18,5,-5),
 (40,(.050,-.130,-.620),-1,14,0,-18,5,-3),
 (48,(.050,-.130,-.627),-1,14,0,-18,5,-4),
 (60,(.050,-.130,-.627),-1,14,0,-18,5,-4)]
for row in poses:body_pose(*row)
for tag in tags:
    front=tag.startswith('front');sign=1 if tag.endswith('left') else -1
    if front:
        keys=[(0,0,0.,0,0),(6,0,0.,0,0),(12,.045,.020,12,sign*.025),(16,.14,.025,25,sign*.06),(20,.25,.012,10,sign*.10),(26,.30,0.,5,sign*.14),(34,.32,0.,5,sign*.16),(48,.32,0.,5,sign*.16),(60,.32,0.,5,sign*.16)]
    else:
        keys=[(0,0,0.,0,0),(12,0,0.,0,0),(18,.035,0.,0,sign*.025),(23,.12,.055,8,sign*.10),(28,.24,.09,15,sign*.16),(34,.26,.02,8,sign*.20),(38,.25,0.,4,sign*.22),(48,.25,0.,4,sign*.22),(60,.25,0.,4,sign*.22)]
    # The far front leg loses support slightly after the near one.
    for f,dy,dz,pitch,dx in keys:hoof_key(tag,f,dy,float(dz),pitch,dx)
for b in arm.pose.bones:
    for c in b.constraints:c.influence=1;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=60)
def curves(obj):
    if not obj.animation_data or not obj.animation_data.action:return []
    return [fc for l in obj.animation_data.action.layers for s in l.strips for bag in s.channelbags for fc in bag.fcurves]
for obj in [arm]+[leg['foot'] for leg in legs.values()]:
    obj.animation_data.action.name='AN_Stonehoof_Death_'+('Rig' if obj==arm else obj.name);obj.animation_data.action.use_fake_user=True
    for fc in curves(obj):
        for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
# Reconstruct grounded soles throughout the arc of each hoof.
heights={tag:[] for tag in tags}
for i in range(241):
    f=i/4;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
    for tag,leg in legs.items():
        foot=leg['foot'];q=foot.rotation_quaternion@leg['rest_rotation'].inverted();low=min((q@(mesh.data.vertices[v].co-leg['ankle'])).z for v in leg['hoof_ids'])
        heights[tag].append((f,leg['sole_z']+foot['sole_clearance']-low))
for tag,leg in legs.items():
    foot=leg['foot']
    for l in foot.animation_data.action.layers:
        for s in l.strips:
            for bag in s.channelbags:
                for fc in list(bag.fcurves):
                    if fc.data_path=='location' and fc.array_index==2:bag.fcurves.remove(fc)
    for f,z in heights[tag]:foot.location.z=z;foot.keyframe_insert('location',index=2,frame=f)
# Keep elbows/knees on a continuous outward plane as the torso collapses.
for tag,leg in legs.items():
    base_axis,base_bend,base_angle=planes[tag];con=arm.pose.bones['MCH_Lower_'+tag].constraints[0];previous=base_angle;keys=[]
    for i in range(241):
        f=i/4;scene.frame_set(int(f),subframe=f-int(f));con.pole_angle=base_angle;bpy.context.view_layer.update()
        hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();axis=(leg['foot'].location-hip).normalized();base=base_axis.rotation_difference(axis)@base_bend
        sign=1 if tag.endswith('left') else -1;v=Vector((sign*.65,-1,.55));v=(v-axis*v.dot(axis)).normalized();u=max(0,min(1,(f-8)/24));u=u*u*(3-2*u)
        angle=math.atan2(axis.dot(base.cross(v)),base.dot(v));desired=Quaternion(axis,angle*u)@base
        def bend():
            v=arm.pose.bones['MCH_Lower_'+tag].head-hip;return (v-axis*v.dot(axis)).normalized()
        current=bend();con.pole_angle=base_angle+.01;bpy.context.view_layer.update();probe=bend();sgn=1 if axis.dot(current.cross(probe))>0 else -1
        angle=base_angle+math.atan2(axis.dot(current.cross(desired)),current.dot(desired))/sgn
        while angle-previous>math.pi:angle-=math.tau
        while angle-previous< -math.pi:angle+=math.tau
        keys.append((f,angle));previous=angle
    for f,angle in keys:con.pole_angle=angle;con.keyframe_insert('pole_angle',frame=f)
for obj in [arm]+[leg['foot'] for leg in legs.values()]:
    for fc in curves(obj):
        for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
# Contact-limited torso height. Solve on the evaluated skin, including head and knees.
torso_ids=[v.index for v in mesh.data.vertices if not mesh.vertex_groups[max(v.groups,key=lambda g:g.weight).group].name.startswith(('leg_','tail'))]
ground_keys=[];ground_corrections=[];root=arm.pose.bones['body'];up=rest['body'].to_quaternion().inverted()@Vector((0,0,1))
for i in range(241):
    f=i/4;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update();lift=0.
    for iteration in range(6):
        ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();low=min(me.vertices[idx].co.z for idx in torso_ids);ev.to_mesh_clear()
        if low>=-.001:break
        correction=-low+.001;lift+=correction;root.location+=up*correction;bpy.context.view_layer.update()
    ground_keys.append((f,root.location.copy()));ground_corrections.append((f,lift))
for f,loc in ground_keys:root.location=loc;root.keyframe_insert('location',frame=f,group='body')
for fc in curves(arm):
    if fc.data_path=='pose.bones["body"].location':
        for k in fc.keyframe_points:k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
for tag,leg in legs.items():
    base_axis,base_bend,base_angle=planes[tag];con=arm.pose.bones['MCH_Lower_'+tag].constraints[0];previous=base_angle;keys=[]
    for i in range(241):
        f=i/4;scene.frame_set(int(f),subframe=f-int(f));con.pole_angle=base_angle;bpy.context.view_layer.update()
        hip=arm.pose.bones['MCH_Upper_'+tag].head.copy();axis=(leg['foot'].location-hip).normalized();base=base_axis.rotation_difference(axis)@base_bend
        sign=1 if tag.endswith('left') else -1;v=Vector((sign*.85,-.7,1.2));v=(v-axis*v.dot(axis)).normalized();u=max(0,min(1,(f-8)/24));u=u*u*(3-2*u)
        angle=math.atan2(axis.dot(base.cross(v)),base.dot(v));desired=Quaternion(axis,angle*u)@base
        def bend():
            v=arm.pose.bones['MCH_Lower_'+tag].head-hip;return (v-axis*v.dot(axis)).normalized()
        current=bend();con.pole_angle=base_angle+.01;bpy.context.view_layer.update();probe=bend();sgn=1 if axis.dot(current.cross(probe))>0 else -1
        angle=base_angle+math.atan2(axis.dot(current.cross(desired)),current.dot(desired))/sgn
        while angle-previous>math.pi:angle-=math.tau
        while angle-previous< -math.pi:angle+=math.tau
        keys.append((f,angle));previous=angle
    for f,angle in keys:con.pole_angle=angle;con.keyframe_insert('pole_angle',frame=f)

scene.frame_start=0;scene.frame_end=60;scene.render.fps=30
scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
for m in list(scene.timeline_markers):scene.timeline_markers.remove(m)
for f,name in [(0,'Потеря опоры'),(12,'Подламываются передние ноги'),(20,'Грудь принимает вес'),(32,'Оседает таз'),(48,'Затухание'),(60,'Неподвижное тело')]:scene.timeline_markers.new(name,frame=f)
cam=scene.camera
def camera(view):
    target=Vector((0,-.03,.60));offset=Vector((8,-1,2.6)) if view=='side' else Vector((6,-7,7));cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.4
scene.frame_set(0);camera('side');arm['clip_status']='Death candidate; owner approval pending';arm['root_motion']='No carrier motion. Torso displacement expresses collapse only.'
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_Death_r01.blend'))
report={'source_sha256':{str(source):source_hash},'frames':60,'fps':30,'owner_approved':False,'reference':'../../references/death.mp4','poses':poses,'ground_corrections':ground_corrections}
(HERE/'build.json').write_text(json.dumps(report,indent=2))
if MODE in ('keys','all'):
    for view in ('side','game'):
        camera(view);folder=OUT/(view+'_frames');folder.mkdir(exist_ok=True)
        for f in (range(61) if MODE=='all' else (0,8,12,16,20,24,28,32,36,40,48,60)):
            scene.frame_set(f);bpy.context.view_layer.update();scene.render.filepath=str(folder/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
print('DEATH_CANDIDATE_SAVED')
