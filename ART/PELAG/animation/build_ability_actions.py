"""Авторинг на исходном скелете. Запускать через Blender MCP."""
import bpy, math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
root=Path(r'C:\Users\d.grab\Desktop\the-game')
rig=bpy.data.objects['Armature']; rig.animation_data_create()
scene=bpy.context.scene; scene.render.fps=30
out=root/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
S=.01

def bone(n): return rig.pose.bones['mixamorig:'+n]
def world(n): return rig.matrix_world@bone(n).head
def update(): bpy.context.view_layer.update()
def rotate(n,q):
 b=bone(n); m=rig.matrix_world@b.matrix
 b.matrix=rig.matrix_world.inverted()@Matrix.Translation(m.translation)@q.to_matrix().to_4x4()@Matrix.Translation(-m.translation)@m
 update()
def limb(u,l,tip,target,pole):
 a,b,c=world(u),world(l),world(tip); l1,l2=(b-a).length,(c-b).length
 delta=Vector(target)*S-a; d=min(max(delta.length,abs(l1-l2)+.000001),(l1+l2)*.995)
 direction=delta.normalized(); p=Vector(pole); bend=(p-direction*p.dot(direction)).normalized()
 along=(l1*l1-l2*l2+d*d)/(2*d)
 elbow=a+direction*along+bend*math.sqrt(max(0,l1*l1-along*along))
 rotate(u,(b-a).rotation_difference(elbow-a))
 rotate(l,(world(tip)-world(l)).rotation_difference(a+direction*d-world(l)))
neutral=dict(left=(.26,-.06,.99),right=(-.26,-.06,.99),lean=0,twist=0,crouch=0)
brace=dict(left=(.39,-.18,1.24),right=(-.25,-.15,1.10),lean=-6,twist=-14,crouch=.05)
flight=dict(left=(.13,-.40,1.58),right=(-.20,-.10,1.09),lean=18,twist=-8,crouch=0,lfoot=(.17,.30,.38),rfoot=(-.17,.37,.52))
land=dict(brace,lean=16,crouch=.16,left=(.28,-.30,1.13),right=(-.32,-.10,1.02))
dash=dict(flight,lean=23,left=(.24,.06,1.12),right=(-.24,-.29,1.27),lfoot=(.20,.28,.24),rfoot=(-.18,.36,.38))
actions={
'Pelag_AN_CycloneStart':[(1,neutral),(3,dict(brace,left=(.24,.01,.96),twist=16)),(7,brace)],
'Pelag_AN_CycloneLoop':[(1,brace),(8,dict(brace,twist=8,hiptwist=-5,lean=-9,crouch=.075,left=(.38,-.25,1.30),right=(-.28,.08,1.04))),(16,dict(brace,twist=24,hiptwist=6,lean=-5,crouch=.035,left=(.33,-.20,1.35))),(23,dict(brace,twist=4,hiptwist=3,lean=3,crouch=.065,left=(.42,-.08,1.25),right=(-.30,-.22,1.15))),(31,brace)],
'Pelag_AN_CycloneEnd':[(1,brace),(4,dict(brace,left=(.12,-.22,1.17),right=(-.07,-.23,1.10),twist=12)),(7,dict(neutral,left=(.23,.02,.99))),(10,neutral)],
'Pelag_AN_AnchorLeap':[(1,neutral),(4,dict(brace,left=(.19,.04,1.55),twist=22)),(7,dict(brace,left=(.10,-.43,1.44),lean=12)),(10,dict(flight,lfoot=(.19,.10,.14),rfoot=(-.19,.11,.15))),(14,flight),(19,dict(flight,lfoot=(.18,.28,.53),rfoot=(-.18,.30,.35))),(23,dict(flight,lfoot=(.20,-.16,.20),rfoot=(-.20,-.10,.23),lean=5)),(25,land),(30,brace),(35,neutral)],
'Pelag_AN_Squall':[(1,brace),(2,dash),(4,dict(dash,twist=-22,right=(-.08,-.43,1.32))),(6,dict(brace,twist=25,right=(.24,-.28,1.19))),(8,brace)]}
combat=dict(brace,left=(.30,-.10,1.10),right=(-.30,-.32,1.15),lean=2,twist=-8,crouch=.045,rightroll=-90)
# Клинок сначала выходит из кушака вперёд, затем кисть раскрывает низкую защиту.
draw=[(1,neutral),(5,dict(neutral,right=(.14,-.13,1.02),twist=12)),(8,dict(neutral,right=(.20,-.08,.97),twist=16)),(12,dict(brace,right=(.12,-.34,1.12),twist=8,rightroll=-25)),(17,dict(combat,right=(-.20,-.37,1.20))),(22,combat)]
actions['Pelag_AN_CombatIdle']=[(1,combat),(20,dict(combat,lean=1,right=(-.26,-.28,1.21))),(40,dict(combat,lean=3)),(61,combat)]
actions['Pelag_AN_SaberDraw']=draw
actions['Pelag_AN_SaberStow']=[(23-f,p) for f,p in reversed(draw)]
def pose(frame,spec):
 for b in rig.pose.bones:
  b.rotation_mode='QUATERNION'; b.rotation_quaternion=(1,0,0,0); b.location=(0,0,0); b.scale=(1,1,1)
 update()
 b=bone('Hips'); m=b.matrix.copy(); m.translation+=rig.matrix_world.inverted().to_3x3()@Vector((0,0,-spec.get('crouch',0)*S)); b.matrix=m; update()
 rotate('Hips',Quaternion(Vector((0,0,1)),math.radians(spec.get('hiptwist',0))))
 rotate('Spine',Quaternion(Vector((1,0,0)),math.radians(spec.get('lean',0))))
 rotate('Spine2',Quaternion(Vector((0,0,1)),math.radians(spec.get('twist',0))))
 for side,hand,pole in [('Left',spec['left'],(1,.5,-.3)),('Right',spec['right'],(-1,.5,-.3))]:
  limb(side+'Arm',side+'ForeArm',side+'Hand',hand,pole)
  # Пронация предплечья сохраняет положение кисти, разворачивая хват клинка.
  roll=spec.get(side.lower()+'roll',0)
  if roll: rotate(side+'ForeArm',Quaternion((world(side+'Hand')-world(side+'ForeArm')).normalized(),math.radians(roll)))
 limb('LeftUpLeg','LeftLeg','LeftFoot',spec.get('lfoot',(.21,.04,.136)),(0,-1,0))
 limb('RightUpLeg','RightLeg','RightFoot',spec.get('rfoot',(-.21,.04,.136)),(0,-1,0))
 for side in ['Left','Right']:
  for digit in ['Index','Middle','Ring','Pinky']:
   for joint in range(1,4):
    b=bone(side+'Hand'+digit+str(joint)); b.rotation_quaternion=Quaternion(Vector((1,0,0)),math.radians(35 if joint==1 else 55))
 for b in rig.pose.bones:
  b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name); b.keyframe_insert('location',frame=frame,group=b.name)
def export(name,animated=True):
 bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=animated,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=animated,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
for name,keys in actions.items():
 old=bpy.data.actions.get(name)
 if old: bpy.data.actions.remove(old)
 action=bpy.data.actions.new(name); action.use_fake_user=True; rig.animation_data.action=action
 for frame,spec in keys: pose(frame,spec)
 # Один активный NLA strip сохраняет явный контракт длительности экспорта.
 track=rig.animation_data.nla_tracks.new(); track.name=name
 strip=track.strips.new(name,1,action); strip.action_slot=rig.animation_data.action_slot
 rig.animation_data.action=None
 scene.frame_start=1; scene.frame_end=keys[-1][0]; scene.frame_set(1); export(name)
 rig.animation_data.nla_tracks.remove(track)
rig.animation_data.action=None
for b in rig.pose.bones: b.rotation_quaternion=(1,0,0,0); b.location=(0,0,0); b.scale=(1,1,1)
update(); export('Pelag_AN_Bind',False)
rig.animation_data.action=bpy.data.actions['Pelag_AN_CycloneLoop']; scene.frame_start=1; scene.frame_end=31; scene.frame_set(8)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'ART/PELAG/animation/Pelag_Ability_Actions.blend'))
result={'actions':list(actions),'bones':len(rig.data.bones),'source':bpy.data.filepath}
