import bpy, os, math, json
from mathutils import Vector, Quaternion, Matrix, Euler
ROOT=r'C:\Users\d.grab\Desktop\the-game'
OUT=os.path.join(ROOT,r'ART\PELAG\animation\anchor_sweep_review')
os.makedirs(OUT,exist_ok=True)
BODY=os.path.join(ROOT,r'razlom\Assets\Resources\Characters\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx')
HEAD=os.path.join(ROOT,r'razlom\Assets\Resources\Weapons\Pelag\AnchorChain\Pelag_AnchorHead.fbx')
GRIP=os.path.join(ROOT,r'razlom\Assets\Resources\Weapons\Pelag\AnchorChain\Pelag_AnchorGrip.fbx')
# clean
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.render.fps=30; scene.frame_start=1; scene.frame_end=60
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
# import body
bpy.ops.import_scene.fbx(filepath=BODY,automatic_bone_orientation=False)
arm=next(o for o in scene.objects if o.type=='ARMATURE'); body=next(o for o in scene.objects if o.type=='MESH')
arm.name='ARM_Pelag_v6'; body.name='Pelag_Body'
# normalize imported cm scale to meters/human readable by uniform 100
arm.scale*=100.0; body.scale*=100.0
# material
mat=bpy.data.materials.new('MAT_Pelag_DeepTeal'); mat.diffuse_color=(0.035,0.16,0.19,1); mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(0.025,0.12,0.15,1); bs.inputs['Roughness'].default_value=.48; bs.inputs['Metallic'].default_value=.12
body.data.materials.clear(); body.data.materials.append(mat)
# source pose samples
src_act=arm.animation_data.action if arm.animation_data else None
fr0,fr1=(1,17)
if src_act:
    try: fr0,fr1=map(int,src_act.frame_range)
    except: pass
scene.frame_set(fr0); bpy.context.view_layer.update()
bones=list(arm.pose.bones)
def capture():
    d={}
    for b in bones:
        q=b.rotation_quaternion.copy() if b.rotation_mode=='QUATERNION' else b.rotation_euler.to_quaternion()
        d[b.name]=(b.location.copy(),q,b.scale.copy())
    return d
samples=[]
for sf in range(fr0,fr1+1):
    scene.frame_set(sf); bpy.context.view_layer.update(); samples.append(capture())
# detach source
arm.animation_data_clear()
act=bpy.data.actions.new('AnchorSweep_Authored_60f'); arm.animation_data_create(); arm.animation_data.action=act
# clear any old fcurves impossible layered; fresh action
def hand_world():
    b=arm.pose.bones.get('mixamorig:LeftHand') or arm.pose.bones.get('mixamorig:RightHand')
    return arm.matrix_world @ b.matrix.translation if b else Vector((0,0,1))
# helper key all channels
for f in range(1,61):
    t=(f-1)/59*(len(samples)-1); i=min(len(samples)-1,int(math.floor(t))); u=t-i; a=samples[i]; z=samples[min(i+1,len(samples)-1)]
    for b in bones:
        la,qa,sa=a[b.name]; lz,qz,sz=z[b.name]
        b.location=la.lerp(lz,u); b.rotation_mode='QUATERNION'; b.rotation_quaternion=qa.slerp(qz,u); b.scale=sa.lerp(sz,u)
    # authored body weight: anticipation, release, pull, recoil
    def qrot(x,y,z): return Quaternion((1,0,0,0)) if (x==y==z==0) else Euler((math.radians(x),math.radians(y),math.radians(z)),'XYZ').to_quaternion()
    phase=f
    hips=arm.pose.bones.get('mixamorig:Hips'); s1=arm.pose.bones.get('mixamorig:Spine1'); s2=arm.pose.bones.get('mixamorig:Spine2')
    if hips and s1 and s2:
        if phase<=10: vals=(0, -7*math.sin(math.pi*(phase-1)/18), 0)
        elif phase<=18: vals=(0, 10*math.sin(math.pi*(phase-10)/16), 0)
        elif phase<=34: vals=(-4*math.sin(math.pi*(phase-18)/16), 18*math.sin(math.pi*(phase-18)/16), 0)
        elif phase<=44: vals=(5*math.sin(math.pi*(phase-34)/10), -12*math.sin(math.pi*(phase-34)/10), 0)
        else: vals=(0,0,0)
        for b,fac in ((hips,.35),(s1,.55),(s2,.75)):
            b.rotation_quaternion=(b.rotation_quaternion @ qrot(vals[0]*fac,vals[1]*fac,vals[2]*fac)).normalized()
    for b in bones:
        b.keyframe_insert('location',frame=f); b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('scale',frame=f)
# set bezier and simplify scale fcurves
if hasattr(act,'fcurves'):
    for fc in act.fcurves:
        for kp in fc.keyframe_points: kp.interpolation='BEZIER'
# import props one by one; scale to suitable size
bpy.ops.import_scene.fbx(filepath=GRIP,automatic_bone_orientation=False); grip=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('Anchor_Grip')); grip.name='PROP_AnchorGrip'; grip.scale=(1.0,1.0,1.0)
bpy.ops.import_scene.fbx(filepath=HEAD,automatic_bone_orientation=False); hook=next(o for o in scene.objects if o.type=='MESH' and o.name.startswith('Anchor_Head')); hook.name='PROP_AnchorHead'; hook.scale=(1.0,1.0,1.0)
for o in (grip,hook):
    o.data.materials.clear(); o.data.materials.append(mat)
# prop root and target
prop=bpy.data.objects.new('CTRL_AnchorRig',None); scene.collection.objects.link(prop)
grip.parent=prop; hook.parent=prop
# place grip near left hand and hook initially same
# chain material
cm=bpy.data.materials.new('MAT_Chain_Iron'); cm.diffuse_color=(0.08,0.11,0.12,1); cm.use_nodes=True
cbs=cm.node_tree.nodes.get('Principled BSDF'); cbs.inputs['Base Color'].default_value=(0.06,0.08,0.085,1); cbs.inputs['Metallic'].default_value=.9; cbs.inputs['Roughness'].default_value=.26
# create actual oval-ish torus links, alternating planes
links=[]
for i in range(18):
    bpy.ops.mesh.primitive_torus_add(major_radius=.065,minor_radius=.015,major_segments=16,minor_segments=6,location=(0,0,0))
    o=bpy.context.object; o.name=f'PROP_ChainLink_{i+1:02d}'; o.data.materials.append(cm); o.scale=(1.0,0.58,1.0) # oval
    links.append(o)
# target and ground
red=bpy.data.materials.new('MAT_Target_Red'); red.diffuse_color=(0.55,0.025,0.018,1); red.use_nodes=True; rbs=red.node_tree.nodes.get('Principled BSDF'); rbs.inputs['Base Color'].default_value=(0.6,0.02,0.012,1); rbs.inputs['Roughness'].default_value=.38
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3,radius=.22,location=(1.45,0.15,.28)); target=bpy.context.object; target.name='REVIEW_Target'; target.data.materials.append(red)
bpy.ops.mesh.primitive_torus_add(major_radius=.32,minor_radius=.018,major_segments=32,minor_segments=8,location=(1.45,.15,.05)); ring=bpy.context.object; ring.name='REVIEW_TargetRing'; ring.data.materials.append(red)
fm=bpy.data.materials.new('MAT_Ground'); fm.diffuse_color=(0.018,0.028,0.032,1); fm.use_nodes=True; fbs=fm.node_tree.nodes.get('Principled BSDF'); fbs.inputs['Base Color'].default_value=(0.018,0.028,0.032,1); fbs.inputs['Roughness'].default_value=.9
bpy.ops.mesh.primitive_plane_add(size=8,location=(0,0,0)); floor=bpy.context.object; floor.name='REVIEW_Ground'; floor.data.materials.append(fm)
# camera
bpy.ops.object.camera_add(location=(3.5,-5.8,2.45)); cam=bpy.context.object; cam.name='CAM_AnchorSweep'; scene.camera=cam; cam.data.lens=58
def aim(obj,pt): obj.rotation_euler=(Vector(pt)-obj.location).to_track_quat('-Z','Y').to_euler()
aim(cam,(.65,.1,1.0))
# lights
bpy.ops.object.light_add(type='AREA', location=(-2.0,-2.0,4.2)); key=bpy.context.object; key.name='LIGHT_Key'; key.data.energy=1100; key.data.shape='DISK'; key.data.size=4.0; aim(key,(.4,0,1))
bpy.ops.object.light_add(type='AREA', location=(3.0,-1.0,2.2)); fill=bpy.context.object; fill.name='LIGHT_Rim'; fill.data.energy=700; fill.data.color=(0.18,0.35,1.0); fill.data.size=3; aim(fill,(1,0,1))
# animate prop and links based on left hand and target
hand=arm.pose.bones.get('mixamorig:LeftHand') or arm.pose.bones.get('mixamorig:RightHand')
# store hand world each frame with body action
hp={}
for f in range(1,61): scene.frame_set(f); bpy.context.view_layer.update(); hp[f]=arm.matrix_world @ hand.matrix.translation
# anchor hook path
start=hp[1]+Vector((0.08,0,0.10)); targetP=Vector((1.45,.15,.52)); behind=Vector((1.70,.28,.66))
for f in range(1,61):
    scene.frame_set(f)
    if f<=10: p=start
    elif f<=22: p=start.lerp(behind,(f-10)/12)
    elif f<=34: p=behind
    elif f<=42: p=behind.lerp(targetP,(f-34)/8)
    elif f<=49: p=targetP
    elif f<=60: p=targetP.lerp(hp[f]+Vector((.08,0,.10)),(f-49)/11)
    prop.location=p; prop.rotation_mode='QUATERNION'; d=(p-hp[f]); prop.rotation_quaternion=(d.to_track_quat('Z','Y') if d.length>1e-4 else Quaternion((1,0,0,0))); prop.keyframe_insert('location',frame=f); prop.keyframe_insert('rotation_quaternion',frame=f)
    # grip sits at player hand, hook object offset from prop by local offset
    grip.location=(hp[f]-p) if f<49 else (hp[f]-p)
    hook.location=(0,0,0)
    grip.keyframe_insert('location',frame=f)
    # chain payout: visible link count 0->18, taut -> retract
    if f<10: prog=0
    elif f<24: prog=(f-10)/14
    elif f<49: prog=1
    else: prog=max(0,(60-f)/11)
    a=hp[f]; b=p; bend=(a+b)*.5 + Vector((0, .22 if f<24 else .05, .22 if f<24 else .02))
    for i,o in enumerate(links):
        t=(i+0.5)/len(links); vis=prog*len(links)-i
        u=min(1,max(0,vis)); # fade scale only last link
        q=(1-u)*0.02+u
        # quadratic curve
        pp=(1-t)*(1-t)*a+2*(1-t)*t*bend+t*t*b
        nxt=min(1,t+.03); pp2=(1-nxt)*(1-nxt)*a+2*(1-nxt)*nxt*bend+nxt*nxt*b
        tan=pp2-pp
        o.location=pp; o.rotation_mode='QUATERNION'; o.rotation_quaternion=(tan.to_track_quat('Z','Y') if tan.length>1e-4 else Quaternion((1,0,0,0)))
        if i%2: o.rotation_quaternion=o.rotation_quaternion @ Quaternion((0,0,0,1)) # 180 around Z alternation
        o.scale=(q,q*.58,q); o.keyframe_insert('location',frame=f); o.keyframe_insert('rotation_quaternion',frame=f); o.keyframe_insert('scale',frame=f)
# interpolation
for o in [prop,grip]+links:
    if o.animation_data and o.animation_data.action and hasattr(o.animation_data.action,'fcurves'):
        for fc in o.animation_data.action.fcurves:
            for kp in fc.keyframe_points: kp.interpolation='BEZIER'
# annotations as empties? save
scene.frame_set(1); scene.render.filepath=os.path.join(OUT,'frame_')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'Pelag_AnchorSweep_Complete.blend'))
# export selected authored package
bpy.ops.object.select_all(action='DESELECT')
for o in [arm,body,prop,grip,hook]+links: o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Pelag_AnchorSweep_Complete.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
# render contact frames
for f in [1,7,12,18,24,30,34,40,46,52,60]:
    scene.frame_set(f); scene.render.filepath=os.path.join(OUT,f'frame_{f:02d}.png'); bpy.ops.render.render(write_still=True)
result={'out':OUT,'blend':os.path.join(OUT,'Pelag_AnchorSweep_Complete.blend'),'fbx':os.path.join(OUT,'Pelag_AnchorSweep_Complete.fbx'),'frames':11,'source_range':[fr0,fr1]}
