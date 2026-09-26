"""Производная сцена и семь клипов. Принятые источники не перезаписываются."""
import bpy,json,math,shutil
from pathlib import Path
from mathutils import Quaternion,Vector
ROOT=Path(__file__).resolve().parent;ANIM=ROOT.parent
OUT=ANIM/'unity_package';OUT.mkdir(exist_ok=True)
UNITY=Path('C:/Users/d.grab/Desktop/the-game/razlom/Assets/Resources/Characters/Forest_Wendigo');UNITY.mkdir(parents=True,exist_ok=True)
s=bpy.context.scene;r=s.objects['ARM_ForestWendigo'];mesh=s.objects['SM_ForestWendigo_LOD0'];r.animation_data_clear();r.animation_data_create()
def load(path,prefix):
 if Path(bpy.data.filepath).resolve()==path.resolve():
  return sorted([a for a in bpy.data.actions if a.name.startswith(prefix)],key=lambda a:a.name)[-1].copy()
 with bpy.data.libraries.load(str(path),link=False) as (src,dst):
  wanted=[n for n in src.actions if n.startswith(prefix)]
  if not wanted:raise RuntimeError((prefix,wanted))
  dst.actions=[sorted(wanted)[-1]]
 return dst.actions[0]
clips={
 'Idle':load(ANIM/'ForestWendigo_Animated.blend','AN_ForestWendigo_Idle'),
 'Claw':load(ANIM/'reference_match_claw/Claw_Reference_Spline.blend','AN_ForestWendigo_Claw_Reference_Spline.002'),
 'Leap':load(ANIM/'reference_match_leap/Leap_Final.blend','AN_ForestWendigo_Leap_Final'),
 'Death':load(ROOT/'death_work/Death_Final.blend','AN_ForestWendigo_Death_Final'),
 'Walk':load(ROOT/'walk_revision_r04/Walk_Final.blend','AN_ForestWendigo_Walk_Final'),
 # Вой чащи (Howl): контакт когтей на кадре 24 из 48, см. reference_match_howl/validation.json.
 'Howl':load(ANIM/'reference_match_howl/Howl_Baked_r01.blend','AN_ForestWendigo_Howl_Baked')}
for role,a in clips.items():
 a.name='Wendigo_'+role;a.use_fake_user=True
 if role=='Idle':
  start=float(a.frame_range[0])
  for layer in a.layers:
   for strip in layer.strips:
    for bag in strip.channelbags:
     for fc in bag.fcurves:
      for k in fc.keyframe_points:
       k.co.x=(k.co.x-start)*.8;k.handle_left.x=(k.handle_left.x-start)*.8;k.handle_right.x=(k.handle_right.x-start)*.8
r.animation_data.action=clips['Idle'];s.frame_set(0);base={b.name:(b.location.copy(),b.rotation_quaternion.copy()) for b in r.pose.bones}
hit=bpy.data.actions.new('Wendigo_Hit');hit.use_fake_user=True;r.animation_data.action=hit;clips['Hit']=hit
for frame,force in [(0,0),(1.5,.65),(3,1),(5.5,.55),(8,-.12),(12,0)]:
 for b in r.pose.bones:
  b.rotation_mode='QUATERNION';b.location,b.rotation_quaternion=base[b.name];b.scale=(1,1,1)
  strength={'spine_01':-5,'spine_02':-7,'neck':4,'head':7,'L_clavicle':-3,'R_clavicle':3}.get(b.name,0)
  if strength:
   axis=b.bone.matrix_local.to_3x3().transposed()@Vector((-.707,.707,0));b.rotation_quaternion=b.rotation_quaternion@Quaternion(axis,math.radians(strength*force))
  b.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=b.name)
  if b.name=='pelvis':b.keyframe_insert(data_path='location',frame=frame,group=b.name)
for layer in hit.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for fc in bag.fcurves:
    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
# Action не сбрасывает свойства без ключей: Idle включает IK ног, и фиксация
# протекает в последующие клипы при all-actions bake. Однократного сброса мало:
# каждому FK-клипу нужны собственные ключи, а исходный IK ожидания сохраняем.
for role,a in clips.items():
 if role=='Idle':continue
 r.animation_data.action=a
 start,end=map(float,a.frame_range)
 for b in r.pose.bones:
  for c in b.constraints:
   for f in (start,end):
    c.influence=0
    c.keyframe_insert(data_path='influence',frame=f,group='Export constraint state')
# Проверяем вычисленный скелет после Idle: наличие ключей само по себе не доказывает движение.
r.animation_data.action=clips['Idle'];s.frame_set(0)
r.animation_data.action=clips['Walk'];walk_feet={n:[] for n in ('L_foot','R_foot')}
for frame in range(0,49):
 s.frame_set(frame)
 if any(c.influence>1e-6 for b in r.pose.bones for c in b.constraints):
  raise RuntimeError('Walk inherited a constraint from another Action')
 for n in walk_feet:walk_feet[n].append(list(r.pose.bones[n].head))
walk_span={n:max((Vector(a)-Vector(b)).length for a in points for b in points) for n,points in walk_feet.items()}
if min(walk_span.values())<1.2:raise RuntimeError('Export foot plants are locking the walk: '+str(walk_span))
(OUT/'walk_evaluated_skeleton.json').write_text(json.dumps({'feet':walk_feet,'span_metres':walk_span},indent=2))
# Unity использует четыре веса. Отбрасываем только малые хвосты, затем нормируем.
pruned=0;lost_max=0
for v in mesh.data.vertices:
 groups=sorted([(g.group,g.weight) for g in v.groups],key=lambda x:-x[1]);keep=groups[:4];total=sum(w for _,w in keep)
 if len(groups)>4:pruned+=1;lost_max=max(lost_max,sum(w for _,w in groups[4:]))
 if total<=0:raise RuntimeError('unweighted vertex '+str(v.index))
 for gi,_ in groups:mesh.vertex_groups[gi].remove([v.index])
 for gi,w in keep:mesh.vertex_groups[gi].add([v.index],w/total,'REPLACE')
for a in list(bpy.data.actions):
 if a not in clips.values():bpy.data.actions.remove(a)
for role,a in clips.items():a.name='Wendigo_'+role
for b in r.pose.bones:
 for c in b.constraints:c.influence=0
for im in bpy.data.images:
 if im.name.startswith('Color_'):
  im.filepath_raw=str(UNITY/'ForestWendigo_BaseColor.png');im.file_format='PNG'
  # Принятая текстура уже в Unity: пересохранение из упакованных данных меняет пиксели (26.09).
  if not (UNITY/'ForestWendigo_BaseColor.png').exists():im.save()
r.animation_data.action=clips['Idle'];s.frame_set(0);s.render.fps=24;s.frame_start=0;s.frame_end=96
guide=bpy.data.objects.get('FacingGuide')
if guide is None:guide=bpy.data.objects.new('FacingGuide',None);s.collection.objects.link(guide)
guide.parent=r;guide.location=(.36,.35,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ForestWendigo_Production.blend'))
bpy.ops.object.select_all(action='DESELECT');r.select_set(True);mesh.select_set(True);guide.select_set(True);bpy.context.view_layer.objects.active=r
bpy.ops.export_scene.fbx(filepath=str(UNITY/'ForestWendigo.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},
 add_leaf_bones=False,use_armature_deform_only=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
 bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,
 bake_anim_step=.25,bake_anim_simplify_factor=0,mesh_smooth_type='FACE',use_mesh_modifiers=True,path_mode='AUTO')
manifest={'triangles':sum(len(p.vertices)-2 for p in mesh.data.polygons),'fps':24,'clips':{k:list(a.frame_range) for k,a in clips.items()},'max_weights':4,'pruned_vertices':pruned,'maximum_pruned_weight':lost_max,'source':'reference-matched; Claw r02 immutable; Howl r01 added 2026-09-26','contact_frames':{'Howl':24},'root_motion':False}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));result=manifest
