"""Reference-led foreleg collapse, chest contact and hindquarter settling."""
import bpy,math,json,ast,sys,hashlib
from pathlib import Path
from mathutils import Vector,Quaternion,Euler,Matrix
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
 (4,(0,.005,-.040),0,0,-1,-2,-2,1),
 (8,(.005,-.010,-.090),3,1,2,2,0,3),
 (12,(.015,-.040,-.180),8,2,3,0,-5,5),
 (16,(.025,-.060,-.310),15,3,2,-12,-20,7),
 (20,(.030,-.085,-.420),17,5,1,-16,-22,5),
 (24,(.035,-.090,-.500),12,7,0,-18,-22,-1),
 (28,(.040,-.100,-.480),7,9,0,-10,-8,-5),
 (32,(.045,-.120,-.475),2,12,0,22,-27,-7),
 (36,(.047,-.130,-.480),0,16,0,37,-37,-5),
 (40,(.050,-.130,-.470),0,18,0,45,-45,-3),
 (48,(.050,-.130,-.480),0,18,0,45,-45,-4),
 (60,(.050,-.130,-.480),0,18,0,45,-45,-4)]
for row in poses:body_pose(*row)
# Hindquarters settle after the chest; local pelvic fold avoids a hovering rump.
for f,angle in ((0,0),(12,0),(20,-3),(24,-8),(28,-17),(32,-24),(36,-28),(40,-27),(48,-28),(60,-28)):
    b=arm.pose.bones['body_bot'];r=rest['body_bot'].to_quaternion();b.rotation_quaternion=r.inverted()@Quaternion((1,0,0),math.radians(angle))@r;b.keyframe_insert('rotation_quaternion',frame=f,group='body_bot')
for tag in tags:
    front=tag.startswith('front');sign=1 if tag.endswith('left') else -1
    if front:
        keys=[(0,0,0.,0,0),(6,0,0.,0,0),(12,.025,.030,15,sign*.025),(16,-.03,.03,28,sign*.06),(20,-.08,.015,24,sign*.10),(26,-.12,0.,18,sign*.13),(34,-.14,0.,18,sign*.14),(48,-.14,0.,18,sign*.14),(60,-.14,0.,18,sign*.14)]
    else:
        keys=[(0,0,0.,0,0),(12,0,0.,0,0),(16,.02,.07,0,sign*.025),(20,.08,.16,8,sign*.07),(24,.19,.18,15,sign*.10),(28,.26,.12,15,sign*.12),(34,.34,.04,8,sign*.10),(38,.34,0.,4,sign*.10),(48,.34,0.,4,sign*.10),(60,.34,0.,4,sign*.10)]
    # The far front leg loses support slightly after the near one.
    for f,dy,dz,pitch,dx in keys:hoof_key(tag,f+(1.5 if tag=='front_right' and 6<f<34 else 0),dy,float(dz),pitch,dx)
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
# Analytic fixed-length two-bone solution avoids pole singularities in the deep fold.
# This action explicitly disables only helper IK; deform followers stay enabled.
rest_planes={}
for tag in tags:
    for name in ('MCH_Upper_'+tag,'MCH_Lower_'+tag):arm.pose.bones[name].rotation_mode='QUATERNION'
for tag,leg in legs.items():
    upper=arm.data.bones['MCH_Upper_'+tag];lower=arm.data.bones['MCH_Lower_'+tag]
    axis=(lower.tail_local-upper.head_local).normalized();bend=lower.head_local-upper.head_local;bend=(bend-axis*bend.dot(axis)).normalized()
    rest_planes[tag]=(axis,bend,axis.cross(bend).normalized(),upper.length,lower.length)
    c=arm.pose.bones[lower.name].constraints[0];c.influence=0;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=60)

def basis(direction,normal):
    y=direction.normalized();z=(normal-y*normal.dot(y)).normalized();x=y.cross(z).normalized()
    return Matrix((x,y,z)).transposed()

def solve_legs(f):
    for tag,leg in legs.items():
        upper=arm.pose.bones['MCH_Upper_'+tag];lower=arm.pose.bones['MCH_Lower_'+tag]
        hip=upper.head.copy();target=leg['foot'].location.copy()
        base_axis,base_bend,normal,l1,l2=rest_planes[tag]
        # Relaxed hoof may retract along ground; never scale a limb or lift a planted sole.
        horizontal=Vector((target.x-hip.x,target.y-hip.y,0));reach=math.sqrt(max(.0001,(l1+l2-(.00001+.002*min(1,f/8)))**2-(target.z-hip.z)**2))
        if horizontal.length>reach:
            horizontal.normalize();target.x=hip.x+horizontal.x*reach;target.y=hip.y+horizontal.y*reach;leg['foot'].location=target
        axis=(target-hip).normalized();d=(target-hip).length
        d=max(abs(l1-l2)+.001,min(l1+l2-.00001,d));target=hip+axis*d
        bend=base_axis.rotation_difference(axis)@base_bend
        sign=1 if tag.endswith('left') else -1
        v=Vector((sign*.9,.9,.05)) if tag.startswith('front') else Vector((sign*.55,-.65,-.05));v=(v-axis*v.dot(axis)).normalized()
        u=max(0,min(1,(f-7)/25));u=u*u*(3-2*u)
        angle=math.atan2(axis.dot(bend.cross(v)),bend.dot(v));bend=Quaternion(axis,angle*u)@bend
        along=(l1*l1-l2*l2+d*d)/(2*d);height=math.sqrt(max(0,l1*l1-along*along));knee=hip+axis*along+bend*height
        n=axis.cross(bend).normalized()
        for bone,pos,end in ((upper,hip,knee),(lower,knee,target)):
            rb=arm.data.bones[bone.name];r=((rb.tail_local-rb.head_local).normalized().rotation_difference((end-pos).normalized())@rb.matrix_local.to_quaternion()).to_matrix()
            mat=r.to_4x4();mat.translation=pos;bone.matrix=mat;bpy.context.view_layer.update()

# Limit torso against ground, then place relaxed muzzle with the authored neck/head arc.
torso_ids=[v.index for v in mesh.data.vertices]
ground_keys=[];ground_corrections=[];root=arm.pose.bones['body'];up=rest['body'].to_quaternion().inverted()@Vector((0,0,1));solutions=[];foot_solutions=[]
for i in range(961):
    f=i/16;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update();lift=0.
    for iteration in range(8):
        solve_legs(f)
        ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();low=min(me.vertices[idx].co.z for idx in torso_ids);ev.to_mesh_clear()
        if low>=-.0005:break
        correction=.001-low;lift+=correction;root.location+=up*correction;bpy.context.view_layer.update()
    solve_legs(f)
    ground_keys.append((f,root.location.copy()));ground_corrections.append((f,lift))
    foot_solutions.append((f,{tag:leg['foot'].location.copy() for tag,leg in legs.items()}))
    solutions.append((f,{name:(arm.pose.bones[name].location.copy(),arm.pose.bones[name].rotation_quaternion.copy(),arm.pose.bones[name].scale.copy()) for tag in tags for name in ('MCH_Upper_'+tag,'MCH_Lower_'+tag)}))
for f,loc in ground_keys:root.location=loc;root.keyframe_insert('location',frame=f,group='body')
for f,values in foot_solutions:
    for tag,loc in values.items():
        foot=legs[tag]['foot'];foot.location=loc;foot.keyframe_insert('location',frame=f)
previous={}
for f,values in solutions:
    for name,(loc,q,scale) in values.items():
        b=arm.pose.bones[name];b.rotation_mode='QUATERNION'
        if name in previous and q.dot(previous[name])<0:q.negate()
        b.location=loc;b.rotation_quaternion=q;b.scale=(1,1,1);previous[name]=q.copy()
        b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f);b.keyframe_insert('scale',frame=f)
for fc in curves(arm):
    if 'MCH_' in fc.data_path or fc.data_path=='pose.bones["body"].location':
        for k in fc.keyframe_points:k.interpolation='LINEAR'

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
