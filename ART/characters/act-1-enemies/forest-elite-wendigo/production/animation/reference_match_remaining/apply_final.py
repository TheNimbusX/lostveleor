import bpy,json
from pathlib import Path
from mathutils import Vector,Quaternion
ROOT=Path(__file__).resolve().parent
kind=globals().get('CLIP','Death');out=ROOT/(kind.lower()+'_work') if kind!='Leap' else ROOT.parent/'reference_match_leap'
d=json.loads((ROOT.parent/'reference_match_leap'/'pose_solutions.json').read_text());samples=json.loads((out/'final_samples.json').read_text())
s=bpy.context.scene;r=s.objects['ARM_ForestWendigo'];r.animation_data_create();a=bpy.data.actions.new('AN_ForestWendigo_'+kind+'_Final');a.use_fake_user=True;r.animation_data.action=a
previous={}
for k,x in enumerate(samples):
 f=k/4;s.frame_set(int(f),subframe=f%1)
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
    for k in fc.keyframe_points:k.interpolation='LINEAR'
s.frame_start=0;s.frame_end=(len(samples)-1)//4;s.render.fps=24;s.frame_set(0)
s.render.filepath=str(out/'final_frames/pose_');s.render.resolution_x=s.render.resolution_y=720
bpy.ops.wm.save_as_mainfile(filepath=str(out/(kind+'_Final.blend')))
result={'clip':kind,'frames':len(samples),'path':bpy.data.filepath}
