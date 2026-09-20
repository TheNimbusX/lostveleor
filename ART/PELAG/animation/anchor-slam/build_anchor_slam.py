"""Удар якорем: исходный Downward, закреплённые стопы и двуручная тяга."""
import bpy, math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

ROOT = Path('C:/Users/d.grab/Desktop/the-game')
OUT = ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
WORK = ROOT/'ART/PELAG/animation/anchor-slam'
if 'Squall_Source_Downward' not in bpy.data.objects:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ART/PELAG/animation/squall/Pelag_Squall_Work.blend'))
scene = bpy.context.scene
scene.render.fps = 30
source = bpy.data.objects['Squall_Source_Downward']
action = bpy.data.actions['AN_Squall_Source_Downward']
source.animation_data.action = action
source.animation_data.action_slot = action.slots[0]

def smooth(t):
    t=max(0,min(1,t)); return t*t*(3-2*t)

def curve(frame, keys):
    for (a, av),(b,bv) in zip(keys,keys[1:]):
        if frame<=b:
            w=smooth((frame-a)/(b-a))
            return Vector(av).lerp(Vector(bv),w)
    return Vector(keys[-1][1])

poses=[]
for frame in range(1,29):
    # Контакт на кадре 16, возврат не обрезает отдачу корпуса.
    sf=curve(frame,[(1,(1,0,0)),(10,(12,0,0)),(16,(18,0,0)),(20,(23,0,0)),(28,(41,0,0))]).x
    scene.frame_set(int(sf),subframe=sf-int(sf))
    poses.append(({b.name:b.matrix_basis.to_quaternion().copy() for b in source.pose.bones},
                  source.pose.bones['mixamorig:Hips'].matrix.translation.copy()))
scene.frame_set(1)
feet={side:source.pose.bones['mixamorig:'+side+'Foot'].head.copy() for side in ['Left','Right']}
foot_rotations={side:source.pose.bones['mixamorig:'+side+'Foot'].matrix.to_quaternion().copy() for side in ['Left','Right']}
rig=source.copy();rig.data=source.data.copy();scene.collection.objects.link(rig)
rig.name='AnchorSlam_Export_Rig';rig.animation_data_clear();rig.animation_data_create()
rig.hide_set(False)
for b in rig.pose.bones:
    b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.location=(0,0,0);b.scale=(1,1,1)

def export(name, animated):
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
        object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=animated,
        bake_anim_use_all_actions=False,bake_anim_use_nla_strips=animated,
        bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')

scene.frame_start=scene.frame_end=1
bpy.context.view_layer.update();export('Pelag_AN_AnchorSlamBind',False)
derived=bpy.data.actions.new('AN_Pelag_AnchorSlam');rig.animation_data.action=derived

def solve(upper_name, lower_name, end_name, target, pole):
    a,b,c=[rig.pose.bones['mixamorig:'+n] for n in [upper_name,lower_name,end_name]]
    shoulder=a.head.copy(); elbow=b.head.copy(); hand=c.head.copy()
    la=(elbow-shoulder).length;lb=(hand-elbow).length
    delta=target-shoulder;distance=max(abs(la-lb)+.001,min(delta.length,(la+lb)*.985))
    axis=delta.normalized();target=shoulder+axis*distance
    bend=(pole-axis*pole.dot(axis)).normalized()
    along=(la*la-lb*lb+distance*distance)/(2*distance)
    goal=shoulder+axis*along+bend*math.sqrt(max(0,la*la-along*along))
    if end_name.endswith('Hand'):
        # Ориентация плоскости локтя не наследует roll исходного оружия:
        # короткая дуга FromTo давала оборот плеча у почти прямой руки.
        normal=axis.cross(bend).normalized()
        for bone,origin,destination in [(a,shoulder,goal),(b,goal,target)]:
            y=(destination-origin).normalized();x=normal;z=x.cross(y).normalized()
            m=Matrix((x,y,z)).transposed().to_4x4();m.translation=origin;bone.matrix=m
            bpy.context.view_layer.update()
        return
    m=(elbow-shoulder).rotation_difference(goal-shoulder).to_matrix().to_4x4() @ a.matrix;m.translation=shoulder;a.matrix=m
    bpy.context.view_layer.update()
    origin=b.head.copy();m=(c.head-origin).rotation_difference(target-origin).to_matrix().to_4x4() @ b.matrix;m.translation=origin;b.matrix=m
    bpy.context.view_layer.update()

previous_quaternions={}
for frame,(rotations,hip_pos) in enumerate(poses,1):
    scene.frame_set(frame)
    for b in rig.pose.bones:b.rotation_quaternion=rotations[b.name];b.location=(0,0,0)
    bpy.context.view_layer.update()
    hip=rig.pose.bones['mixamorig:Hips'];m=hip.matrix.copy()
    weight=curve(frame,[(1,(0,0,0)),(10,(.025,-.025,-.045)),(16,(-.015,-.045,.055)),
        (19,(-.01,-.03,.04)),(24,(.01,-.01,-.025)),(28,(0,0,0))])
    m.translation=Vector((weight.x,hip_pos.y+weight.y,.002+weight.z));hip.matrix=m
    bpy.context.view_layer.update()
    # Левая ведёт над плечом; правая подхватывает цепь ниже, не скрещивая кисти.
    left=curve(frame,[(1,(.24,.86,.20)),(5,(.32,1.15,.04)),(10,(.30,1.55,-.10)),
        (13,(.22,1.40,.36)),(16,(.20,.99,.60)),(18,(.24,1.00,.51)),
        (21,(.32,.91,.22)),(25,(.30,.88,.10)),(28,(.24,.86,.20))])
    right=curve(frame,[(1,(-.24,.95,.28)),(7,(-.08,1.15,.26)),(10,(.05,1.30,.32)),
        (16,(-.06,.96,.45)),(19,(-.06,1.02,.38)),(22,(-.10,.88,.18)),(28,(-.24,.95,.28))])
    solve('LeftArm','LeftForeArm','LeftHand',left,Vector((1,-.15,-2)))
    solve('RightArm','RightForeArm','RightHand',right,Vector((-1,-.3,-1)))
    # У Downward левая ладонь раскрыта. Цепь держится замкнутым хватом,
    # сгиб направлен внутрь ладони в системе исходной T-позы.
    for finger in ['Index','Middle','Ring','Pinky','Thumb']:
        for joint in range(1,4):
            bone=rig.pose.bones.get(f'mixamorig:LeftHand{finger}{joint}')
            if bone is None: continue
            rest=bone.bone.matrix_local.to_quaternion()
            axis=(rest@Vector((0,1,0))).cross(Vector((0,-1,0))).normalized()
            local_axis=rest.inverted()@axis
            angle=([65,85,60] if finger!='Thumb' else [20,35,30])[joint-1]
            bone.rotation_quaternion=Quaternion(local_axis,math.radians(angle))
    for side,sign in [('Left',1),('Right',-1)]:
        solve(side+'UpLeg',side+'Leg',side+'Foot',feet[side],Vector((sign*.1,0,1)))
        foot=rig.pose.bones['mixamorig:'+side+'Foot'];fm=foot_rotations[side].to_matrix().to_4x4()
        fm.translation=foot.head.copy();foot.matrix=fm
        bpy.context.view_layer.update()
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in previous_quaternions and q.dot(previous_quaternions[b.name])<0:q.negate()
        b.rotation_quaternion=q;previous_quaternions[b.name]=q.copy()
        b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
    hip.keyframe_insert('location',frame=frame,group=hip.name)

for layer in derived.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for fc in bag.fcurves:
                for p in fc.keyframe_points:
                    p.interpolation='LINEAR'
slot=rig.animation_data.action_slot
track=rig.animation_data.nla_tracks.new();strip=track.strips.new(derived.name,1,derived);strip.action_slot=slot
rig.animation_data.action=None;derived.use_fake_user=True
scene.frame_start=1;scene.frame_end=28
for frame,label in [(1,'Grip'),(10,'Left shoulder'),(16,'AnchorSlamImpact'),(19,'Take up slack'),(24,'Low return'),(28,'Release')]:
    scene.timeline_markers.new(label,frame=frame)
export('Pelag_AN_AnchorSlam',True)
WORK.mkdir(parents=True,exist_ok=True)
exec((WORK/'prepare_preview.py').read_text(encoding='utf-8'), {'__file__': str(WORK/'prepare_preview.py')})
print('ANCHOR_SLAM_EXPORTED frames=28 contact=16 duration=.9')
