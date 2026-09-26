"""Редактируемая постановка отдельного клипа; не меняет исходник принятого Claw."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector,Quaternion
ROOT=Path(__file__).resolve().parent
kind=globals().get('CLIP','Death');out=ROOT/(kind.lower()+'_work');out.mkdir(exist_ok=True)
d=json.loads((out/'pose_solutions.json').read_text());s=bpy.context.scene;r=s.objects['ARM_ForestWendigo']
a=bpy.data.actions.new('AN_ForestWendigo_'+kind+'_Reference');a.use_fake_user=True;r.animation_data.action=a
previous={}
for frame,data in d['frames'].items():
 x=data['pose'];f=float(frame);s.frame_set(int(f),subframe=f%1)
 for b in r.pose.bones:
  b.location=(0,0,0);b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.scale=(1,1,1)
  for c in b.constraints:c.influence=0
 r.pose.bones['pelvis'].location=r.data.bones['pelvis'].matrix_local.to_3x3().transposed()@Vector(x[:3])
 for j,n in enumerate(d['names'][1:]):
  v=Vector(x[3+j*3:6+j*3]);q=Quaternion(v.normalized(),v.length) if v.length>1e-7 else Quaternion()
  if n in previous and q.dot(previous[n])<0:q.negate()
  previous[n]=q.copy();r.pose.bones[n].rotation_quaternion=q;r.pose.bones[n].keyframe_insert(data_path='rotation_quaternion',frame=f,group=n)
 r.pose.bones['pelvis'].keyframe_insert(data_path='location',frame=f,group='pelvis')
for layer in a.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for fc in bag.fcurves:
    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
s.frame_start=0;s.frame_end=int(max(map(float,d['frames'])));s.render.fps=24
s.render.resolution_x=720;s.render.resolution_y=720
s.frame_set(0);s.render.filepath=str(out/'frames/pose_')
bpy.ops.wm.save_as_mainfile(filepath=str(out/(kind+'_Reference.blend')))
# Снимаем четвертькадровые позы из того же интерполированного клипа для проверки опор.
samples=[]
for k in range(s.frame_end*4+1):
 f=k/4;s.frame_set(int(f),subframe=f%1);p=r.data.bones['pelvis'].matrix_local.to_3x3()@r.pose.bones['pelvis'].location;x=list(p)
 for n in d['names'][1:]:
  q=r.pose.bones[n].rotation_quaternion;axis,ang=q.to_axis_angle();x.extend(axis*ang)
 samples.append(x)
(out/'animation_samples.json').write_text(json.dumps(samples))
s.frame_set(0)
result={'file':bpy.data.filepath,'frames':s.frame_end,'action':a.name}
