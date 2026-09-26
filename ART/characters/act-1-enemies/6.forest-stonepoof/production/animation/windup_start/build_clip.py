"""Editable hoof IK and authored preparation/launch, on the accepted mesh."""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion, Euler
HERE=Path(__file__).resolve().parent; PROD=HERE.parents[1]
OUT=PROD/'review'/'animation_windup_start';OUT.mkdir(exist_ok=True)
MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'keys'
bpy.ops.wm.open_mainfile(filepath=str(PROD/'Stonehoof_ModelCandidate_r03.blend'))
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0']
scene.frame_start=0;scene.frame_end=36;scene.render.fps=30
scene.render.resolution_x=960;scene.render.resolution_y=640
scene.render.image_settings.file_format='PNG'
scene.render.engine='BLENDER_EEVEE';scene.eevee.taa_render_samples=24
rest={b.name:b.matrix_local.copy() for b in arm.data.bones}
ctrl_col=bpy.data.collections.new('COL_Stonehoof_AnimationControls');scene.collection.children.link(ctrl_col)
def empty(name,loc):
    obj=bpy.data.objects.new(name,None);ctrl_col.objects.link(obj);obj.location=loc;obj.empty_display_type='SPHERE';obj.empty_display_size=.06;obj.rotation_mode='QUATERNION';return obj
carrier=empty('CTRL_PreviewMotion_Only',(0,0,0));carrier['note']='Review-only displacement. Exclude from engine export; Sim owns arena movement.'
arm.parent=carrier
legs={}
for region in ('front','hind'):
    for side in ('left','right'):
        prefix=f'leg_{region}_{side}_';tag=f'{region}_{side}'
        hip=Vector(arm.data.bones[prefix+'top1'].head_local);knee=Vector(arm.data.bones[prefix+'bot1'].head_local);ankle=Vector(arm.data.bones[prefix+'bot2'].head_local)
        foot=empty('CTRL_Hoof_'+tag,ankle);foot.parent=carrier;foot.rotation_quaternion=rest[prefix+'bot2'].to_quaternion()
        direction=(ankle-hip).normalized();knee_side=knee-(hip+direction*(knee-hip).dot(direction));knee_side.normalize()
        pole=empty('CTRL_Pole_'+tag,(hip+ankle)/2+knee_side*.9);pole.parent=carrier
        legs[tag]={'prefix':prefix,'hip':hip,'knee':knee,'ankle':ankle,'foot':foot,'pole':pole,'rest_rotation':foot.rotation_quaternion.copy()}

# Preserve stone hoof shapes; transition gradually into the existing pastern skin.
skin_changed=0
for v in mesh.data.vertices:
    x,y,z=v.co
    region='front' if y<-.20 else ('hind' if y>.34 else None)
    if region is None or abs(x)<.12:continue
    low,high=(.105,.205) if region=='front' else (.080,.155)
    if z>=high:continue
    tag=region+('_left' if x>0 else '_right');name=legs[tag]['prefix']+'bot2'
    weight=1 if z<=low else 1-(lambda t:t*t*(3-2*t))((z-low)/(high-low))
    old={g.group:g.weight*(1-weight) for g in v.groups};idx=mesh.vertex_groups[name].index;old[idx]=old.get(idx,0)+weight
    keep=sorted(old.items(),key=lambda p:-p[1])[:4];total=sum(w for _,w in keep)
    for g in list(v.groups):mesh.vertex_groups[g.group].remove([v.index])
    for i,w in keep:
        if w>1e-7:mesh.vertex_groups[i].add([v.index],w/total,'REPLACE')
    skin_changed+=1

bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for tag,leg in legs.items():
    eb=arm.data.edit_bones;p=leg['prefix']
    up=eb.new('MCH_Upper_'+tag);up.head=leg['hip'];up.tail=leg['knee'];up.parent=eb[p+'top0'];up.use_deform=False
    low=eb.new('MCH_Lower_'+tag);low.head=leg['knee'];low.tail=leg['ankle'];low.parent=up;low.use_connect=True;low.use_deform=False
    for suffix,parent in [('top1',up),('bot0',up),('bot1',low)]:
        source=eb[p+suffix];driver=eb.new('MCH_Drive_'+p+suffix)
        driver.head=source.head;driver.tail=source.tail;driver.roll=source.roll;driver.parent=parent;driver.use_deform=False
bpy.ops.object.mode_set(mode='OBJECT')
for tag,leg in legs.items():
    p=leg['prefix'];lower=arm.pose.bones['MCH_Lower_'+tag]
    ik=lower.constraints.new('IK');ik.name='Hoof plant · fixed limb lengths';ik.target=leg['foot'];ik.pole_target=leg['pole'];ik.chain_count=2;ik.use_stretch=False;ik.iterations=100
    for name in ['MCH_Upper_'+tag,'MCH_Lower_'+tag]:arm.pose.bones[name].ik_stretch=0
    # Fit pole roll to the bind pose rather than guessing it from side labels.
    best=(1e9,0)
    for step in range(145):
        angle=-math.pi+step*math.tau/144;ik.pole_angle=angle;bpy.context.view_layer.update()
        target=arm.pose.bones['MCH_Lower_'+tag].head
        error=(target-leg['knee']).length
        if error<best[0]:best=(error,angle)
    ik.pole_angle=best[1];leg['pole_fit_error']=best[0]
    for suffix in ('top1','bot0','bot1'):
        c=arm.pose.bones[p+suffix].constraints.new('COPY_TRANSFORMS');c.name='Follow editable IK';c.target=arm;c.subtarget='MCH_Drive_'+p+suffix;c.target_space='POSE';c.owner_space='POSE'
    c=arm.pose.bones[p+'bot2'].constraints.new('COPY_ROTATION');c.name='Hoof orientation';c.target=leg['foot'];c.target_space='WORLD';c.owner_space='WORLD'
    for b in (lower,arm.pose.bones['MCH_Upper_'+tag]):
        for c in b.constraints:
            c.influence=1;c.keyframe_insert('influence',frame=0);c.keyframe_insert('influence',frame=36)
    # Lowest sole vertices define a meaningful ground contact point.
    ids=[v.index for v in mesh.data.vertices if any(g.group==mesh.vertex_groups[p+'bot2'].index and g.weight>.95 for g in v.groups)]
    sole=min(mesh.data.vertices[i].co.z for i in ids)
    leg['sole_z']=sole;leg['hoof_ids']=ids;leg['sole_ids']=[i for i in ids if mesh.data.vertices[i].co.z<sole+.008]

def body_pose(frame,offset,pitch,roll,chest,neck,head,tail):
    angles={'body':(pitch,roll,0),'body_top0':(chest,0,0),'body_top1':(chest*.35,0,0),'body_bot':(-chest*.3,0,0),'neck0':(neck,0,0),'head0':(head,0,0),'tail0':(0,tail,tail*.5)}
    for name,values in angles.items():
        pb=arm.pose.bones[name];pb.rotation_mode='QUATERNION';r=rest[name].to_quaternion();q=Euler(tuple(math.radians(a) for a in values),'XYZ').to_quaternion()
        pb.rotation_quaternion=r.inverted()@q@r;pb.keyframe_insert('rotation_quaternion',frame=frame,group=name)
    pb=arm.pose.bones['body'];pb.location=rest['body'].to_quaternion().inverted()@Vector(offset);pb.keyframe_insert('location',frame=frame,group='body')

# Body motion continues between scrapes: load the far foreleg, gather, then push.
poses=[
 (0,(0,0,0),0,0,0,0,0,0),
 (3,(-.027,.018,-.035),1,-1.6,1,1,0,1),
 (6,(-.047,.035,-.055),1.5,-2.5,1.5,2,0,2),
 (9,(-.047,-.012,-.070),3,-2,2,2,1,-1),
 (12,(-.038,-.030,-.073),3.5,-1.8,2.5,2.5,1,-2),
 (15,(-.042,.015,-.060),2.8,-2.2,1.8,3,1,1),
 (18,(-.040,.020,-.077),3.5,-1.8,2,3.5,1.5,2),
 (21,(-.025,-.040,-.090),4.5,-1.0,2.5,4,2,-2),
 (24,(-.010,.010,-.100),4,0,2,4.5,1,-3),
 (27,(0,.045,-.155),3,0,2.5,5,1,0),
 (30,(0,.008,-.145),4.5,0,2,5,1,2),
 (32,(0,-.055,-.055),1,0,-1,2,-1,5),
 (34,(0,-.080,.035),-3,0,-2,-1,-1,3),
 (36,(0,-.060,.070),-1,0,0,-2,0,0)]
for row in poses:body_pose(*row)

def hoof_key(tag,frame,dy=0,dz=0,pitch=0,dx=0):
    leg=legs[tag];foot=leg['foot'];q=Quaternion((1,0,0),math.radians(pitch))
    pivot=Vector((leg['ankle'].x,leg['ankle'].y,leg['sole_z']))
    rotated_low=min((pivot+q@(mesh.data.vertices[i].co-pivot)).z for i in leg['hoof_ids'])
    foot.location=pivot+q@(leg['ankle']-pivot)+Vector((dx,dy,dz+leg['sole_z']-rotated_low))
    foot.rotation_quaternion=q@leg['rest_rotation']
    foot.keyframe_insert('location',frame=frame);foot.keyframe_insert('rotation_quaternion',frame=frame)
    foot['sole_clearance']=dz;foot.keyframe_insert('["sole_clearance"]',frame=frame)

# One front hoof makes two distinct strokes: airborne reach, contact, backward scrape.
scrapes=[(0,0,0,0),(3,0,.015,-4),(6,-.07,.16,-22),(8,-.09,.04,-8),(9,-.09,0,0),(12,.13,0,9),(14,.18,.075,2),(16,-.07,.15,-20),(17,-.08,.07,-8),(18,-.08,0,0),(21,.16,0,9),(23,.14,.075,0),(25,-.045,0,0),(28,-.045,0,0),(30,-.045,0,0),(32,-.17,.12,-18),(34,-.32,.28,-32),(36,-.28,.36,-20)]
for f,dy,dz,pitch in scrapes:hoof_key('front_left',f,dy,dz,pitch)
for tag in ('front_right','hind_left','hind_right'):
    for f in (0,3,9,15,21,24,27,30):hoof_key(tag,f)
for row in [(31,-.015,.04,-7),(33,-.25,.22,-25),(36,-.32,.33,-24)]:hoof_key('front_right',*row)
for tag in ('hind_left','hind_right'):
    hoof_key(tag,31,.03333,0,5)
    hoof_key(tag,32,.13333,0,12)
    hoof_key(tag,33,.24,.08,20)
    hoof_key(tag,34,.22,.19,25)
    hoof_key(tag,36,.12,.36,30)

# Explicit stationary phase followed by the actual 0.2-second acceleration envelope.
for f in range(37):
    t=max(0,(f-30)/30);distance=30*t*t if t<=.2 else 1.2+(t-.2)*12
    carrier.location=(0,-distance,0);carrier.keyframe_insert('location',frame=f)
if arm.animation_data and arm.animation_data.action:arm.animation_data.action.name='AN_Stonehoof_WindupStart_Controls'
for obj in [arm,carrier]+[leg[k] for leg in legs.values() for k in ('foot','pole')]:
    if not obj.animation_data or not obj.animation_data.action:continue
    action=obj.animation_data.action;action.use_fake_user=True
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points:
                        kp.interpolation='BEZIER';kp.handle_left_type='AUTO_CLAMPED';kp.handle_right_type='AUTO_CLAMPED'
                    # Ground level and fixed supports must not overshoot near contact.
                    if obj!=arm and fc.data_path=='["sole_clearance"]':
                        for kp in fc.keyframe_points:
                            kp.handle_left_type='AUTO_CLAMPED';kp.handle_right_type='AUTO_CLAMPED'

# Quaternion and height curves interpolate differently. Reconstruct ankle height
# from the actual rigid sole at quarter frames, retaining the authored lift curve.
# This prevents toe penetration between contact keys without lengthening the leg.
ground_samples={tag:[] for tag in legs}
for i in range(145):
    f=i/4;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
    for tag,leg in legs.items():
        foot=leg['foot'];delta=foot.rotation_quaternion@leg['rest_rotation'].inverted()
        offset=min((delta@(mesh.data.vertices[v].co-leg['ankle'])).z for v in leg['hoof_ids'])
        ground_samples[tag].append((f,leg['sole_z']+foot['sole_clearance']-offset))
for tag,leg in legs.items():
    foot=leg['foot']
    for layer in foot.animation_data.action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in list(bag.fcurves):
                    if fc.data_path=='location' and fc.array_index==2:bag.fcurves.remove(fc)
    for f,z in ground_samples[tag]:
        foot.location.z=z;foot.keyframe_insert('location',index=2,frame=f)
    for layer in foot.animation_data.action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    if fc.data_path=='location' and fc.array_index==2:
                        for kp in fc.keyframe_points:kp.interpolation='BEZIER';kp.handle_left_type='AUTO_CLAMPED';kp.handle_right_type='AUTO_CLAMPED'

for f,name in [(0,'Начало предупреждения'),(9,'Первый скребок — контакт'),(18,'Второй скребок — контакт'),(27,'Подсед'),(30,'Старт движения Sim'),(33,'Толчок задними'),(36,'Переход в бег')]:scene.timeline_markers.new(name,frame=f)
arm['root_motion']='No arena translation in deform rig. CTRL_PreviewMotion_Only represents Sim and is excluded from export.'
arm['clip_status']='Owner review required. First clip only; not imported into Unity.'
arm['controls']='CTRL_Hoof_* and CTRL_Pole_* are editable, fixed-length two-link IK. Explicit influence keys prevent state leakage.'
arm.show_in_front=True
scene.frame_set(0)
report={'stage':'authored_windup_start_candidate','frames':[0,36],'fps':30,'windup_frames':30,'launch_frames':6,'skin_vertices_local_hoof_cleanup':skin_changed,'legs':{tag:{'pole_bind_error_m':leg['pole_fit_error'],'sole_z':leg['sole_z'],'sole_ids':leg['sole_ids']} for tag,leg in legs.items()},'root_motion':'carrier excluded from runtime','source_model':'Stonehoof_ModelCandidate_r03.blend'}
(HERE/'rig_build.json').write_text(json.dumps(report,indent=2))

cam=scene.camera
def camera(view):
    target=Vector((0,-.60,.72));offset=Vector((8,-1.0,2.3)) if view=='side' else Vector((6,-7,7))
    cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=4.25 if view=='side' else 4.1
def render(view,frame):
    camera(view);scene.frame_set(frame)
    folder=OUT/(view+'_frames');folder.mkdir(exist_ok=True)
    scene.render.filepath=str(folder/f'{frame:04d}.png');bpy.ops.render.render(write_still=True)
camera('side');bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_WindupStart_r01.blend'))
if MODE=='keys':
    for view in ('side','game'):
        for frame in (0,6,9,12,17,18,21,27,30,33,36):render(view,frame)
elif MODE=='all':
    for view in ('side','game'):
        for frame in range(37):render(view,frame)
print('BUILD_COMPLETE',json.dumps(report))
