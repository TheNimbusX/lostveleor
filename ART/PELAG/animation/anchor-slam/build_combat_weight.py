"""Постановка тяжёлого боя на текущем риге. Поступательное движение остаётся в Sim."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

ROOT=Path('C:/Users/d.grab/Desktop/the-game')
WORK=ROOT/'ART/PELAG/animation/anchor-slam'
OUT=ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
scene=bpy.data.scenes.get('Pelag_Weighted_Combat')
if scene is None:
    with bpy.data.libraries.load(str(ROOT/'ART/PELAG/animation/squall/Pelag_Squall_Work.blend'),link=False) as (src,dst):
        dst.scenes=['Pelag Squall Authoring']
    scene=dst.scenes[0];scene.name='Pelag_Weighted_Combat'
bpy.context.window.scene=scene
scene.render.fps=30
preview=next(o for o in scene.objects if o.type=='ARMATURE' and 'Preview_Rig' in o.name)
ready=bpy.data.actions['AN_Pelag_Squall_Finish']
preview.animation_data_create();preview.animation_data.action=ready;preview.animation_data.action_slot=ready.slots[0]
scene.frame_set(15)
base={b.name:(b.rotation_quaternion.copy(),b.location.copy()) for b in preview.pose.bones}
feet={s:preview.pose.bones['mixamorig:'+s+'Foot'].head.copy() for s in ('Left','Right')}
foot_q={s:preview.pose.bones['mixamorig:'+s+'Foot'].matrix.to_quaternion().copy() for s in feet}
hip_base=preview.pose.bones['mixamorig:Hips'].head.copy()
rig=preview.copy();rig.data=preview.data.copy();scene.collection.objects.link(rig)
rig.name='Weighted_Combat_Export';rig.animation_data_clear();rig.animation_data_create()
rig.hide_set(False)
for b in rig.pose.bones:
    b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.location=(0,0,0);b.scale=(1,1,1)

def export(name,animated):
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'ARMATURE'},
        add_leaf_bones=False,bake_anim=animated,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=animated,
        bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')

scene.frame_start=scene.frame_end=1;bpy.context.view_layer.update()
for bind in ('MobilityBind','AnchorSlamBind','AnchorLeapBind'):export('Pelag_AN_'+bind,False)

def curve(frame,keys):
    for (a,av),(b,bv) in zip(keys,keys[1:]):
        if frame<=b:
            u=max(0,min(1,(frame-a)/(b-a)));u=u*u*(3-2*u)
            return Vector(av).lerp(Vector(bv),u)
    return Vector(keys[-1][1])

def world_rotate(name,pitch=0,yaw=0,roll=0):
    bone=rig.pose.bones['mixamorig:'+name];m=bone.matrix.copy();p=m.translation.copy()
    q=Quaternion((0,1,0),math.radians(yaw)) @ Quaternion((1,0,0),math.radians(pitch)) @ Quaternion((0,0,1),math.radians(roll))
    m=q.to_matrix().to_4x4()@m;m.translation=p;bone.matrix=m;bpy.context.view_layer.update()

def limb(side,leg,target,pole):
    names=[side+n for n in (('UpLeg','Leg','Foot') if leg else ('Arm','ForeArm','Hand'))]
    a,b,c=[rig.pose.bones['mixamorig:'+n] for n in names]
    start=a.head.copy();mid=b.head.copy();end=c.head.copy()
    la=(mid-start).length;lb=(end-mid).length
    delta=target-start;d=max(abs(la-lb)+.0001,min(delta.length,(la+lb)*.975))
    axis=delta.normalized();goal=start+axis*d
    bend=(pole-axis*pole.dot(axis)).normalized();along=(la*la-lb*lb+d*d)/(2*d)
    elbow=start+axis*along+bend*math.sqrt(max(0,la*la-along*along))
    m=(mid-start).rotation_difference(elbow-start).to_matrix().to_4x4()@a.matrix
    m.translation=start;a.matrix=m;bpy.context.view_layer.update()
    origin=b.head.copy();m=(c.head-origin).rotation_difference(goal-origin).to_matrix().to_4x4()@b.matrix
    m.translation=origin;b.matrix=m;bpy.context.view_layer.update()
    if leg:
        m=foot_q[side].to_matrix().to_4x4();m.translation=c.head.copy();c.matrix=m;bpy.context.view_layer.update()

# Одна поза задаёт таз, дугу корпуса, кисти и опору. Удар — короткий участок между развёрнутыми позами.
# frame: (смещение таза xyz, наклон/поворот/крен таза, наклон/поворот/крен корпуса, левая кисть, правая кисть)
S=(0,0,0); L=(.24,.86,.28);R=(-.28,.99,.24)
def pose(f,h=S,hip=S,body=S,left=L,right=R):return (f,h,hip,body,left,right)
clips={
 'AnchorSlam':(28,16,[
  pose(1),pose(5,(.03,-.05,-.04),(-5,-8,2),(-8,-12,4),(.40,1.02,.06),(-.06,1.00,.29)),
  pose(10,(.06,-.065,-.16),(-10,-20,4),(-16,-26,5),(.36,1.55,-.12),(.02,1.29,.05)),
  pose(13,(.025,-.045,-.11),(-7,-17,3),(-12,-21,3),(.27,1.61,.04),(-.02,1.36,.23)),
  pose(16,(-.035,-.18,.20),(12,15,-3),(27,20,-4),(.13,.80,.72),(-.12,.79,.54)),
  pose(18,(-.04,-.19,.21),(14,18,-4),(28,22,-4),(.12,.76,.65),(-.15,.74,.50)),
  pose(22,(.035,-.015,-.06),(-6,-8,3),(-14,-16,2),(.31,1.10,.18),(-.08,1.01,.16)),
  pose(25,(.02,-.02,-.025),(-2,-5,1),(-4,-6,1),(.30,.83,.06),(-.19,.96,.20)),pose(28)]),
 'WreckA':(16,7,[
  pose(1),pose(4,(.05,-.075,-.12),(-4,-23,6),(-9,-32,7),(.55,1.13,-.12),(.15,1.10,.19)),
  pose(7,(-.05,-.12,.16),(9,23,-6),(15,32,-9),(-.24,.94,.68),(-.39,.87,.50)),
  pose(9,(-.07,-.13,.16),(11,28,-7),(18,39,-10),(-.43,.85,.37),(-.48,.82,.15)),
  pose(12,(.015,-.06,-.035),(-3,-4,1),(-5,-10,2),(.31,.92,.15),(-.09,.89,.19)),pose(16)]),
 'WreckB':(16,7,[
  pose(1),pose(4,(-.045,-.08,-.08),(-4,24,-6),(-8,35,-7),(-.35,1.16,.08),(-.53,1.10,-.10)),
  pose(7,(.05,-.12,.14),(8,-24,6),(15,-34,9),(.39,.97,.60),(.05,.90,.56)),
  pose(9,(.07,-.13,.12),(11,-30,7),(19,-40,10),(.56,.85,.24),(.24,.89,.22)),
  pose(12,(0,-.07,-.015),(-2,5,0),(-5,9,0),(.26,1.05,.22),(-.09,1.03,.24)),pose(16)]),
 'WreckFinish':(19,7,[
  pose(1),pose(4,(.01,.075,-.12),(-12,-13,2),(-19,-18,3),(.24,1.65,.09),(-.11,1.48,.16)),
  pose(7,(-.02,-.18,.18),(16,6,-2),(32,10,-3),(.13,.65,.64),(-.16,.67,.48)),
  pose(10,(-.025,-.17,.19),(15,8,-2),(27,12,-3),(.16,.63,.52),(-.18,.67,.41)),
  pose(14,(.015,-.025,-.07),(-6,-5,1),(-12,-7,2),(.31,.98,.13),(-.10,.96,.15)),pose(19)]),
 'FireFlask':(19,7,[
  pose(1),pose(4,(.055,-.035,-.10),(-3,-14,2),(-8,-24,4),(.42,1.25,-.25),(-.32,.98,.16)),
  pose(7,(-.045,-.065,.15),(8,13,-4),(17,23,-5),(.19,1.35,.73),(-.29,.97,.13)),
  pose(10,(-.05,-.08,.16),(10,19,-5),(20,31,-6),(-.05,1.08,.60),(-.27,1.02,.24)),
  pose(14,(-.01,-.03,.03),(2,6,-1),(4,8,-2),(.18,.88,.31),R),pose(19)]),
 'Skewer':(10,10,[
  pose(1,(0,-.045,-.025),(0,-8,0),(7,-17,-4),(.36,1.01,-.09),(-.26,.96,.27)),
  pose(3,(-.025,-.15,.15),(11,13,-4),(24,20,-6),(.45,1.03,-.40),(-.13,1.00,.76)),
  pose(7,(-.025,-.12,.19),(10,14,-4),(24,18,-6),(.40,1.00,-.42),(-.12,.96,.81)),
  pose(10,(-.01,-.07,.06),(4,4,-1),(10,9,-2),(.28,.90,.04),(-.19,.97,.54))]),
 'Backblast':(9,3,[
  pose(1,(.02,-.16,.10),(12,-10,4),(23,-16,5),(.10,.40,.41),(-.30,1.03,.31)),
  pose(3,(.01,-.08,.04),(-8,-4,2),(-17,-7,3),(.22,.63,.32),(-.30,1.08,.35)),
  pose(5,(.0,.06,-.06),(-14,5,-2),(-20,8,-3),(.41,1.13,.16),(-.35,1.19,.34)),
  pose(7,(.0,-.13,-.035),(9,3,0),(18,4,0),(.27,.87,.22),(-.28,1.06,.36)),pose(9)]),
 'AnchorLeap':(46,33,[
  pose(1),pose(5,(.035,-.07,-.09),(-5,-19,4),(-11,-27,6),(.44,1.38,-.19),(.04,1.16,.13)),
  pose(7,(-.025,-.055,.12),(7,14,-3),(17,22,-5),(.19,1.27,.71),(-.12,1.15,.56)),
  pose(12,(-.035,-.12,.13),(10,17,-3),(22,21,-5),(.13,1.03,.68),(-.13,.96,.53)),
  pose(16,(.0,-.13,-.055),(-8,-2,0),(-16,-6,2),(.24,1.08,.31),(-.11,1.03,.32)),
  pose(21,(-.01,.02,.05),(9,1,1),(20,3,2),(.14,1.16,.64),(-.14,1.08,.59)),
  pose(29,(-.01,-.015,.08),(10,-9,0),(23,-14,0),(.15,1.14,.64),(-.32,1.08,.12)),
  pose(31,(-.02,-.06,.11),(12,-3,-2),(25,-2,-4),(.17,1.09,.61),(-.26,1.10,.32)),
  pose(33,(-.04,-.15,.17),(13,18,-3),(28,27,-5),(.22,1.02,.50),(-.06,1.13,.83)),
  pose(35,(-.04,-.14,.18),(12,20,-3),(25,29,-4),(.22,.98,.44),(-.08,1.10,.80)),
  pose(38,(.015,-.065,-.025),(-2,5,0),(-5,9,0),(.29,.94,.14),(-.20,.97,.34)),pose(46)])
}

def apply_pose(kind,frame,keys):
    for b in rig.pose.bones:b.rotation_quaternion,b.location=base[b.name]
    bpy.context.view_layer.update()
    values=[curve(frame,[(p[0],p[j]) for p in keys]) for j in range(1,6)]
    h,hip,body,left,right=values
    bone=rig.pose.bones['mixamorig:Hips'];m=bone.matrix.copy();m.translation=hip_base+h;bone.matrix=m;bpy.context.view_layer.update()
    world_rotate('Hips',*hip)
    for name,weight in [('Spine',.3),('Spine1',.4),('Spine2',.3)]:world_rotate(name,*(body*weight))
    world_rotate('Neck',-body.x*.5,-body.y*.35,-body.z*.4)
    limb('Left',False,left,Vector((1,-.4,-.65)));limb('Right',False,right,Vector((-1,-.4,-.65)))
    for side in feet:
        target=feet[side].copy();lift=0
        if kind.startswith('Anchor') or kind.startswith('Wreck'):
            brace=curve(frame,[(1,(0,0,0)),(4,(1,0,0)),(clips[kind][0]-5,(1,0,0)),(clips[kind][0],(0,0,0))]).x
            target.x+=(.12 if side=='Left' else -.12)*brace
        if kind=='Backblast':lift=curve(frame,[(1,(0,0,0)),(3,(.14,0,0)),(5,(.38,0,0)),(7,(0,0,0)),(9,(0,0,0))]).x;target.z-=lift*.35
        if kind=='AnchorLeap':lift=curve(frame,[(1,(0,0,0)),(16,(0,0,0)),(21,(.30,0,0)),(29,(.17,0,0)),(33,(0,0,0)),(46,(0,0,0))]).x;target.z-=lift*.5
        if kind=='Skewer':
            stride=math.sin(math.pi*(frame-1)/9)*.24;target.z+=stride if side=='Left' else -stride;lift=.06*math.sin(math.pi*(frame-1)/9)
        target.y+=lift
        limb(side,True,target,Vector((.12 if side=='Left' else -.12,0,1)))
    # Хват закрыт на обеих руках якоря, раскрытие бутылки следует выпуску.
    sides=('Left','Right') if kind.startswith('Wreck') or kind.startswith('Anchor') else ('Left',)
    for side in sides:
        for finger in ('Index','Middle','Ring','Pinky','Thumb'):
            for joint in range(1,4):
                b=rig.pose.bones.get(f'mixamorig:{side}Hand{finger}{joint}')
                if not b:continue
                rest=b.bone.matrix_local.to_quaternion();axis=(rest@Vector((0,1,0))).cross(Vector((0,-1,0))).normalized()
                angle=([60,80,55] if finger!='Thumb' else [15,30,25])[joint-1]
                if kind in ('FireFlask','Backblast'):angle*=1-min(1,max(0,(frame-(6 if kind=='FireFlask' else 2))/2))
                b.rotation_quaternion=Quaternion(rest.inverted()@axis,math.radians(angle))

actions={};blocking={}
for kind,(count,hit,keys) in clips.items():
    for is_block in (True,False):
        action=bpy.data.actions.new('AN_Pelag_'+kind+('_Blocking_Weight' if is_block else '_Weight'));action.use_fake_user=True
        rig.animation_data.action=action;previous={}
        frames=[p[0] for p in keys] if is_block else range(1,count+1)
        for frame in frames:
            scene.frame_set(frame);apply_pose(kind,frame,keys)
            for b in rig.pose.bones:
                q=b.rotation_quaternion.copy()
                if b.name in previous and q.dot(previous[b.name])<0:q.negate()
                b.rotation_quaternion=q;previous[b.name]=q.copy();b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
            rig.pose.bones['mixamorig:Hips'].keyframe_insert('location',frame=frame,group='mixamorig:Hips')
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for p in fc.keyframe_points:
                            p.interpolation='CONSTANT' if is_block else 'BEZIER';p.handle_left_type=p.handle_right_type='AUTO_CLAMPED'
        (blocking if is_block else actions)[kind]=action
    slot=rig.animation_data.action_slot
    track=rig.animation_data.nla_tracks.new();strip=track.strips.new(action.name,1,action);strip.action_slot=slot
    rig.animation_data.action=None;scene.frame_start=1;scene.frame_end=count
    export('Pelag_AN_'+kind,True);rig.animation_data.nla_tracks.remove(track)

preview.animation_data.action=actions['AnchorSlam'];preview.animation_data.action_slot=actions['AnchorSlam'].slots[0]
for o in scene.objects:
    visible=o==preview or (o.type=='MESH' and any(m.type=='ARMATURE' and m.object==preview for m in o.modifiers)) or o.type in ('CAMERA','LIGHT')
    o.hide_set(not visible);o.hide_render=not visible
rig.hide_set(True);rig.hide_render=True
scene.frame_start=1;scene.frame_end=28;scene.frame_set(13)
for img in bpy.data.images:
    if img.source=='FILE' and img.has_data and not img.packed_file:
        try:img.pack()
        except RuntimeError:pass
bpy.data.libraries.write(str(WORK/'Pelag_Weighted_Combat_Work.blend'),{scene,*actions.values(),*blocking.values()},fake_user=True,compress=True)
(WORK/'weighted-contracts.json').write_text(json.dumps({k:{'frames':v[0],'contact':v[1]} for k,v in clips.items()},indent=2),encoding='utf-8')
result={'actions':{k:a.name for k,a in actions.items()},'scene':scene.name,'rig':preview.name,'rootMotion':'in-place; hips carry weight only'}
