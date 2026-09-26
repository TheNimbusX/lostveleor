import bpy,json
from pathlib import Path
from mathutils import Vector,Quaternion
HERE=Path(__file__).resolve().parent
s=bpy.context.scene;r=s.objects['ARM_ForestWendigo']
d=json.loads((HERE/'pose_solutions.json').read_text());samples=json.loads((HERE/'polished_samples.json').read_text())
a=bpy.data.actions.new('AN_ForestWendigo_Leap_Reference_Polished');a.use_fake_user=True;r.animation_data.action=a
previous={}
for sample,x in enumerate(samples):
 f=sample/4
 s.frame_set(int(f),subframe=f%1)
 for b in r.pose.bones:
  b.location=(0,0,0);b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.scale=(1,1,1)
 r.pose.bones['pelvis'].location=r.data.bones['pelvis'].matrix_local.to_3x3().transposed()@Vector(x[:3])
 for i,n in enumerate(d['names'][1:]):
  v=Vector(x[3+i*3:6+i*3]);q=Quaternion(v.normalized(),v.length) if v.length>1e-8 else Quaternion()
  if n in previous and q.dot(previous[n])<0:q.negate()
  previous[n]=q.copy();r.pose.bones[n].rotation_quaternion=q;r.pose.bones[n].keyframe_insert(data_path='rotation_quaternion',frame=f,group=n)
 r.pose.bones['pelvis'].keyframe_insert(data_path='location',frame=f,group='pelvis')
for layer in a.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for fc in bag.fcurves:
    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
s.frame_set(0);s.render.filepath=str(HERE/'frames/pose_')
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Leap_Reference_Spline.blend'))
