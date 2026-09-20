"""Один удар из исходного Downward: зеркальный ведущий хват, исходная пластика тела."""
import bpy, math, json
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion

ROOT=Path('C:/Users/d.grab/Desktop/the-game')
WORK=ROOT/'ART/PELAG/animation/anchor-slam'
OUT=ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
old=bpy.context.scene
source=next((o for o in bpy.data.objects if o.type=='ARMATURE' and o.name.startswith('Squall_Source_Downward')),None)
preview=next((o for o in bpy.data.objects if o.type=='ARMATURE' and o.name.startswith('Squall_Preview_Rig')),None)
if source is None or preview is None:
    with bpy.data.libraries.load(str(ROOT/'ART/PELAG/animation/squall/Pelag_Squall_Work.blend'),link=False) as (a,b):
        b.scenes=['Pelag Squall Authoring']
    source=next(o for o in b.scenes[0].objects if o.type=='ARMATURE' and o.name.startswith('Squall_Source_Downward'))
    preview=next(o for o in b.scenes[0].objects if o.type=='ARMATURE' and o.name.startswith('Squall_Preview_Rig'))
scene=bpy.data.scenes.new('Pelag_Source_AnchorSlam')
bpy.context.window.scene=scene
scene.render.fps=30
src=source.copy();src.data=source.data.copy();scene.collection.objects.link(src)
src.name='AnchorSlam_Downward_Source';src.hide_render=True
rig=preview.copy();rig.data=preview.data.copy();scene.collection.objects.link(rig)
rig.name='AnchorSlam_Source_Derived';rig.animation_data_clear();rig.animation_data_create()
for o in preview.users_scene[0].objects:
    if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==preview for m in o.modifiers):
        mesh=o.copy();mesh.data=o.data.copy();scene.collection.objects.link(mesh)
        mesh.parent=rig
        for m in mesh.modifiers:
            if m.type=='ARMATURE':m.object=rig
        mesh.hide_set(False);mesh.hide_render=False
for b in rig.pose.bones:
    b.rotation_mode='QUATERNION';b.matrix_basis=Matrix.Identity(4)

def export(name,anim):
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'ARMATURE'},
        add_leaf_bones=False,bake_anim=anim,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=anim,
        bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')

scene.frame_start=scene.frame_end=1
bpy.context.view_layer.update();export('Pelag_AN_AnchorSlamBind',False)
ready=bpy.data.actions['AN_Pelag_Squall_Finish']
rig.animation_data.action=ready;rig.animation_data.action_slot=ready.slots[0]
scene.frame_set(15)
ready_pose={b.name:(b.rotation_quaternion.copy(),b.location.copy()) for b in rig.pose.bones}
rig.animation_data.action=None
action=bpy.data.actions.new('AN_Pelag_AnchorSlam_Source');action.use_fake_user=True
rig.animation_data.action=action
S=Matrix.Diagonal((-1,1,1,1))

def source_frame(t):
    # Общая монотонная кривая времени, без остановки скорости на каждой позе.
    times=[0,.30,.50,.90];values=[1,12,18,41];slopes=[36.67,33,39,57.5]
    for i in range(3):
        if t<=times[i+1]+1e-6:
            d=times[i+1]-times[i];u=(t-times[i])/d
            return (2*u**3-3*u*u+1)*values[i]+(u**3-2*u*u+u)*d*slopes[i]+(-2*u**3+3*u*u)*values[i+1]+(u**3-u*u)*d*slopes[i+1]
    return 41

def support_hand(target,weight):
    # Сохраняем плоскость локтя исходной записи; правим только вспомогательный хват.
    a,b,c=[rig.pose.bones['mixamorig:'+n] for n in ['RightArm','RightForeArm','RightHand']]
    root=a.head.copy();elbow=b.head.copy();hand=c.head.copy();target=hand.lerp(target,weight)
    axis=(target-root).normalized();la=(elbow-root).length;lb=(hand-elbow).length
    dist=max(abs(la-lb)+.001,min((target-root).length,(la+lb)*.985));target=root+axis*dist
    bend=elbow-root; bend=(bend-axis*bend.dot(axis)).normalized()
    along=(la*la-lb*lb+dist*dist)/(2*dist)
    goal=root+axis*along+bend*math.sqrt(max(0,la*la-along*along))
    q=(elbow-root).rotation_difference(goal-root);m=q.to_matrix().to_4x4()@a.matrix;m.translation=root;a.matrix=m
    bpy.context.view_layer.update()
    origin=b.head.copy();q=(c.head-origin).rotation_difference(target-origin)
    m=q.to_matrix().to_4x4()@b.matrix;m.translation=origin;b.matrix=m;bpy.context.view_layer.update()

previous={};samples=[]
for frame in range(1,29):
    t=(frame-1)/30;sf=source_frame(t)
    scene.frame_set(int(sf),subframe=sf-int(sf))
    # Отражаем движение вместе с bind pose, сохраняя roll и исходный перенос веса.
    matrices={}
    for b in rig.pose.bones:
        name=b.name.replace('Left','TEMP').replace('Right','Left').replace('TEMP','Right')
        s=src.pose.bones[name]
        matrices[b.name]=S@s.matrix@s.bone.matrix_local.inverted()@S@b.bone.matrix_local
    scene.frame_set(frame)
    for b in rig.pose.bones:
        b.matrix=matrices[b.name];bpy.context.view_layer.update()
    lead=rig.pose.bones['mixamorig:LeftHand']
    support=lead.head+Vector((-.24,-.22,.18))
    support.x=min(.02,support.x);support.y=max(.96,support.y);support.z=max(.35,support.z)
    support_hand(support, .96)
    for side in ['Left','Right']:
        for finger in ['Index','Middle','Ring','Pinky','Thumb']:
            for j in range(1,4):
                b=rig.pose.bones.get(f'mixamorig:{side}Hand{finger}{j}')
                if not b:continue
                rest=b.bone.matrix_local.to_quaternion()
                axis=(rest@Vector((0,1,0))).cross(Vector((0,-1,0))).normalized()
                b.rotation_quaternion=Quaternion(rest.inverted()@axis,math.radians(([60,78,55] if finger!='Thumb' else [15,30,25])[j-1]))
    def ease(x):
        x=max(0,min(1,x));return x*x*(3-2*x)
    motion_weight=ease(t/.13)*ease((.9-t)/.13)
    for b in rig.pose.bones:
        b.rotation_quaternion=ready_pose[b.name][0].slerp(b.rotation_quaternion,motion_weight)
        if b.name=='mixamorig:Hips':b.location=ready_pose[b.name][1].lerp(b.location,motion_weight)
        q=b.rotation_quaternion.copy()
        if b.name in previous and q.dot(previous[b.name])<0:q.negate()
        b.rotation_quaternion=q;previous[b.name]=q.copy()
        b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
    rig.pose.bones['mixamorig:Hips'].keyframe_insert('location',frame=frame,group='mixamorig:Hips')
    samples.append({'time':t,'sourceFrame':sf,'grip':list(lead.head)})
for layer in action.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for fc in bag.fcurves:
                for key in fc.keyframe_points:
                    key.interpolation='BEZIER';key.handle_left_type=key.handle_right_type='AUTO_CLAMPED'
slot=rig.animation_data.action_slot
track=rig.animation_data.nla_tracks.new();strip=track.strips.new(action.name,1,action);strip.action_slot=slot
rig.animation_data.action=None;scene.frame_start=1;scene.frame_end=28
export('Pelag_AN_AnchorSlam',True)
rig.animation_data.nla_tracks.remove(track);rig.animation_data.action=action;rig.animation_data.action_slot=slot
for f,n in [(1,'Take chain'),(10,'Loaded shoulder'),(16,'AnchorSlamImpact'),(19,'Pull'),(28,'Ready')]:scene.timeline_markers.new(n,frame=f)
src.hide_set(True);scene.frame_set(10)
bpy.data.libraries.write(str(WORK/'Pelag_Source_AnchorSlam_Work.blend'),{scene,action,src.animation_data.action,ready},fake_user=True,compress=True)
(WORK/'source-slam-motion.json').write_text(json.dumps(samples,indent=2),encoding='utf-8')
result={'scene':scene.name,'action':action.name,'source':src.animation_data.action.name,'duration':.9,'contact':.5}
