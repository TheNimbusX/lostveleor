"""Авторские движения на текущем Mixamo-риге, без поступательного root motion."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path('C:/Users/d.grab/Desktop/the-game')
WORK = ROOT/'ART/PELAG/animation/mobility'
OUT = ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
scene = bpy.data.scenes.get('Pelag_Mobility_Authoring')
if scene is None:
    with bpy.data.libraries.load(str(ROOT/'ART/PELAG/animation/squall/Pelag_Squall_Work.blend'), link=False) as (src,dst):
        dst.scenes = ['Pelag Squall Authoring']
    scene = dst.scenes[0]; scene.name = 'Pelag_Mobility_Authoring'
bpy.context.window.scene = scene
scene.render.fps = 30
preview = bpy.data.objects['Squall_Preview_Rig']
ready = bpy.data.actions['AN_Pelag_Squall_Finish']
preview.animation_data.action = ready; preview.animation_data.action_slot = ready.slots[0]
scene.frame_set(15)
base = {b.name:(b.rotation_quaternion.copy(), b.location.copy()) for b in preview.pose.bones}
feet = {side:preview.pose.bones['mixamorig:'+side+'Foot'].head.copy() for side in ('Left','Right')}
feet_rot = {side:preview.pose.bones['mixamorig:'+side+'Foot'].matrix.to_quaternion().copy() for side in feet}
hip_rest = preview.pose.bones['mixamorig:Hips'].head.copy()
source = bpy.data.objects['Squall_Source_Downward']
rig = source.copy(); rig.data = source.data.copy(); scene.collection.objects.link(rig)
rig.name = 'Mobility_Export_Rig'; rig.animation_data_clear(); rig.animation_data_create(); rig.hide_set(False)
for b in rig.pose.bones:
    b.rotation_mode='QUATERNION'; b.rotation_quaternion=(1,0,0,0); b.location=(0,0,0); b.scale=(1,1,1)

def export(name, animated):
    bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')), use_selection=True, object_types={'ARMATURE'},
        add_leaf_bones=False, bake_anim=animated, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=animated, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y')

scene.frame_start=scene.frame_end=1
bpy.context.view_layer.update(); export('Pelag_AN_MobilityBind',False)

def sample(frame, keys):
    for (a,av),(b,bv) in zip(keys, keys[1:]):
        if frame <= b:
            t=max(0,min(1,(frame-a)/(b-a))); t=t*t*(3-2*t)
            return Vector(av).lerp(Vector(bv),t)
    return Vector(keys[-1][1])

def rotate(name, axis, angle):
    b=rig.pose.bones['mixamorig:'+name]
    b.rotation_quaternion = b.rotation_quaternion @ Quaternion(Vector(axis), math.radians(angle))
    bpy.context.view_layer.update()

def limb(side, leg, target, pole):
    names=[side+n for n in (('UpLeg','Leg','Foot') if leg else ('Arm','ForeArm','Hand'))]
    a,b,c=[rig.pose.bones['mixamorig:'+n] for n in names]
    start=a.head.copy(); mid=b.head.copy(); end=c.head.copy()
    la=(mid-start).length; lb=(end-mid).length
    delta=target-start; d=max(abs(la-lb)+.0001,min(delta.length,(la+lb)*.985))
    axis=delta.normalized(); goal=start+axis*d
    bend=(pole-axis*pole.dot(axis)).normalized()
    along=(la*la-lb*lb+d*d)/(2*d)
    elbow=start+axis*along+bend*math.sqrt(max(0,la*la-along*along))
    # Кратчайший поворот сохраняет roll кистей и не выворачивает пальцы.
    m=(mid-start).rotation_difference(elbow-start).to_matrix().to_4x4() @ a.matrix
    m.translation=start; a.matrix=m; bpy.context.view_layer.update()
    origin=b.head.copy(); m=(c.head-origin).rotation_difference(goal-origin).to_matrix().to_4x4() @ b.matrix
    m.translation=origin; b.matrix=m; bpy.context.view_layer.update()
    if leg:
        m=feet_rot[side].to_matrix().to_4x4(); m.translation=c.head.copy(); c.matrix=m
        bpy.context.view_layer.update()

def keyframe(frame, previous):
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in previous and q.dot(previous[b.name])<0:q.negate()
        b.rotation_quaternion=q; previous[b.name]=q.copy()
        b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
    rig.pose.bones['mixamorig:Hips'].keyframe_insert('location',frame=frame,group='mixamorig:Hips')

clips=[]; clip_actions={}
for kind,frames in [('Skewer',10),('Backblast',9),('FireFlask',19),('WreckA',16),('WreckB',16),('WreckFinish',19)]:
    action=bpy.data.actions.new('AN_Pelag_'+kind); action.use_fake_user=True
    clip_actions[kind]=action
    rig.animation_data.action=action; previous={}
    for frame in range(1,frames+1):
        scene.frame_set(frame)
        for b in rig.pose.bones:
            b.rotation_quaternion, b.location = base[b.name]
        bpy.context.view_layer.update()
        t=(frame-1)/(frames-1)
        h=rig.pose.bones['mixamorig:Hips']; m=h.matrix.copy()
        if kind=='Skewer':
            weight=sample(frame,[(1,(0,-.035,0)),(3,(0,-.16,.07)),(7,(0,-.13,.07)),(10,(0,-.045,0))])
            m.translation=hip_rest+weight; h.matrix=m; bpy.context.view_layer.update()
            spine=rig.pose.bones['mixamorig:Spine']; sm=spine.matrix.copy(); sp=sm.translation.copy()
            sm=Quaternion(Vector((1,0,0)),math.radians(22*math.sin(math.pi*t))).to_matrix().to_4x4() @ sm
            sm.translation=sp;spine.matrix=sm;bpy.context.view_layer.update()
            limb('Right',False,sample(frame,[(1,(-.25,1.03,.25)),(3,(-.16,1.12,.68)),(7,(-.16,1.12,.68)),(10,(-.22,1.02,.35))]),Vector((-1,-.3,-.2)))
            limb('Left',False,sample(frame,[(1,(.24,.90,.1)),(3,(.40,1.05,-.33)),(7,(.38,1.02,-.3)),(10,(.26,.91,.12))]),Vector((1,-.3,-.2)))
            stride=math.sin(math.pi*t)*.26
            limb('Right',True,feet['Right']+Vector((0,.055*math.sin(math.pi*t),-stride)),Vector((0,0,1)))
            limb('Left',True,feet['Left']+Vector((0,.02*math.sin(math.pi*t),stride)),Vector((0,0,1)))
        elif kind=='Backblast':
            weight=sample(frame,[(1,(0,-.12,0)),(3,(0,.04,0)),(5,(0,.14,0)),(7,(0,-.08,0)),(9,(0,-.035,0))])
            m.translation=hip_rest+weight;h.matrix=m;bpy.context.view_layer.update()
            limb('Left',False,sample(frame,[(1,(.16,.57,.25)),(2,(.10,.40,.32)),(4,(.38,1.02,.1)),(9,(.25,.92,.17))]),Vector((1,-.4,0)))
            limb('Right',False,sample(frame,[(1,(-.2,1.08,.3)),(5,(-.12,1.17,.43)),(9,(-.22,1.05,.32))]),Vector((-1,-.3,0)))
            lift=max(0,math.sin(math.pi*t))*.30
            for side,sign in [('Left',1),('Right',-1)]:
                limb(side,True,feet[side]+Vector((sign*.035,lift,-.10*math.sin(math.pi*t))),Vector((0,0,1)))
        elif kind=='FireFlask':
            weight=sample(frame,[(1,(0,-.02,0)),(5,(.02,-.04,-.025)),(8,(.015,-.03,.035)),(19,(0,0,0))])
            m.translation=hip_rest+weight;h.matrix=m;bpy.context.view_layer.update()
            limb('Left',False,sample(frame,[(1,(.25,.79,.03)),(4,(.30,1.18,-.12)),(7,(.24,1.32,.56)),(10,(.25,1.07,.61)),(19,(.24,.92,.18))]),Vector((1,-.15,-.3)))
            for side in feet:limb(side,True,feet[side],Vector((0,0,1)))
        else:
            heavy=kind=='WreckFinish'; reverse=kind=='WreckB'; hit=7
            a=math.sin(math.pi*min(1,(frame-1)/(frames-1)))
            m.translation=hip_rest+Vector((0,-.075*a,.015*a));h.matrix=m;bpy.context.view_layer.update()
            rotate('Spine2',(0,1,0), (-1 if reverse else 1)*35*math.sin(t*math.pi*2))
            if heavy:
                left=sample(frame,[(1,(.26,.92,.2)),(4,(.32,1.52,.38)),(7,(.20,.86,.57)),(11,(.20,.83,.47)),(19,(.26,.92,.2))])
                right=left+Vector((-.22,-.08,.02))
            else:
                sign=-1 if reverse else 1
                left=sample(frame,[(1,(.30*sign,1.05,.13)),(4,(.50*sign,1.11,.05)),(7,(-.22*sign,.94,.56)),(11,(-.38*sign,.96,.23)),(16,(.26,.92,.20))])
                right=left+Vector((-.24,-.08,.08))
            limb('Left',False,left,Vector((1,-.2,-.5)));limb('Right',False,right,Vector((-1,-.2,-.5)))
            for side in feet:limb(side,True,feet[side],Vector((0,0,1)))
        if kind.startswith('Wreck') or kind in ('Backblast','FireFlask'):
            for finger in ['Index','Middle','Ring','Pinky','Thumb']:
                for joint in range(1,4):
                    bone=rig.pose.bones.get(f'mixamorig:LeftHand{finger}{joint}')
                    if bone is None:continue
                    rest=bone.bone.matrix_local.to_quaternion()
                    axis=(rest@Vector((0,1,0))).cross(Vector((0,-1,0))).normalized()
                    angle=([65,85,60] if finger!='Thumb' else [20,35,30])[joint-1]
                    # После выпуска бутылки ладонь раскрывается, у цепи хват остаётся.
                    hold=1 if kind.startswith('Wreck') else (1-min(1,max(0,(frame-(2 if kind=='Backblast' else 6))/2)))
                    bone.rotation_quaternion=Quaternion(rest.inverted()@axis,math.radians(angle*hold))
        keyframe(frame,previous)
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for p in fc.keyframe_points:p.interpolation='LINEAR'
    slot=rig.animation_data.action_slot
    track=rig.animation_data.nla_tracks.new(); strip=track.strips.new(action.name,1,action);strip.action_slot=slot
    rig.animation_data.action=None
    scene.frame_start=1;scene.frame_end=frames
    export('Pelag_AN_'+kind,True)
    rig.animation_data.nla_tracks.remove(track)
    clips.append({'name':kind,'frames':frames,'contactFrame':7 if kind.startswith('Wreck') else 3 if kind=='Backblast' else 7 if kind=='FireFlask' else frames})

preview.animation_data.action=clip_actions['Skewer'];preview.animation_data.action_slot=preview.animation_data.action.slots[0]
scene.frame_start=1;scene.frame_end=10;scene.frame_set(4)
# Только рабочая сцена Пелага: открытый Forest_Bud не перезаписывается.
for img in bpy.data.images:
    if img.source=='FILE' and img.has_data and not img.packed_file:
        try:img.pack()
        except RuntimeError:pass
for obj in scene.objects:
    if obj.name.startswith('Mobility_Export_Rig'):obj.hide_set(True)
bpy.data.libraries.write(str(WORK/'Pelag_Mobility_Work.blend'),{scene,*clip_actions.values()},fake_user=True,compress=True)
(WORK/'clip-contracts.json').write_text(json.dumps(clips,indent=2),encoding='utf-8')
result={'clips':clips,'workspace':str(WORK/'Pelag_Mobility_Work.blend')}
