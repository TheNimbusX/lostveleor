import bpy, json, math
from pathlib import Path
from mathutils import Vector, Quaternion

HERE=Path(__file__).resolve().parent
d=json.loads((HERE/'pose_solutions.json').read_text())
s=bpy.context.scene;r=s.objects['ARM_ForestWendigo']
r.animation_data_clear();r.animation_data_create()
a=bpy.data.actions.new('AN_ForestWendigo_Leap_Reference_Blocking');a.use_fake_user=True;r.animation_data.action=a
previous_q={}
for frame,data in d['frames'].items():
 x=data['pose'];frame=int(frame);s.frame_set(frame)
 for b in r.pose.bones:
  b.location=(0,0,0);b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.scale=(1,1,1)
  for c in b.constraints:c.influence=0
 r.pose.bones['pelvis'].location=r.data.bones['pelvis'].matrix_local.to_3x3().transposed() @ Vector(x[:3])
 for j,n in enumerate(d['names'][1:]):
  v=Vector(x[3+j*3:6+j*3]);q=Quaternion(v.normalized(),v.length) if v.length>1e-7 else Quaternion()
  if n in previous_q and q.dot(previous_q[n])<0:q.negate()
  previous_q[n]=q.copy()
  r.pose.bones[n].rotation_quaternion=q
 for b in r.pose.bones:
  if b.name.startswith('CTRL'):continue
  b.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=b.name)
 r.pose.bones['pelvis'].keyframe_insert(data_path='location',frame=frame,group='pelvis')
for layer in a.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for f in bag.fcurves:
    for k in f.keyframe_points:k.interpolation='CONSTANT'
az,el,scale,cx,cy=d['camera'];c=s.camera;t=Vector((0,0,1.55));c.location=t+Vector((math.sin(az)*math.cos(el),math.cos(az)*math.cos(el),math.sin(el)))*5.5;c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='PERSP';c.data.lens=36*5.5/scale;c.data.sensor_width=36;c.data.shift_x=.5-cx/960;c.data.shift_y=cy/960-.5
s.render.fps=24;s.frame_start=0;s.frame_end=96
for m in list(s.timeline_markers):s.timeline_markers.remove(m)
for name,fr in [('STANCE',0),('HANDS_DOWN',15),('COIL',24),('LOAD',42),('PUSH',45),('AIRBORNE',48),('APEX',54),('LAND_HANDS',60),('ABSORB',66),('RECOVERY',87)]:s.timeline_markers.new(name,frame=fr)
movie=HERE.parents[2]/'review'/'higgsfield-refs'/'leap.mp4'
if movie.exists():
 c.data.show_background_images=True
 bg=c.data.background_images.new();bg.source='MOVIE_CLIP';bg.clip=bpy.data.movieclips.load(str(movie));bg.alpha=.4
# The source render camera and reference video remain editable in this scene.
s.world.node_tree.nodes.get('Background').inputs[0].default_value=(.4,.4,.4,1)
s.world.node_tree.nodes.get('Background').inputs[1].default_value=.4
s.objects['KEY'].data.energy=650;s.objects['KEY'].data.size=.8
s.objects['ReferenceFloor'].scale=(100,100,100);c.data.clip_end=1000
mat=s.objects['ReferenceFloor'].data.materials[0];mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.36,.36,.36,1);bsdf.inputs['Roughness'].default_value=1
s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Leap_Reference_Blocking.blend'))
s.render.resolution_x=720;s.render.resolution_y=720
out=HERE/'blocking';out.mkdir(exist_ok=True)
for frame in (() if globals().get('SKIP_STILLS') else (0,12,18,24,42,45,48,51,54,57,60,63,69,87,96)):
 s.frame_set(frame);s.render.filepath=str(out/f'pose_{frame:03d}.png');bpy.ops.render.render(write_still=True)

smooth=a.copy();smooth.name='AN_ForestWendigo_Leap_Reference_Spline';smooth.use_fake_user=True
r.animation_data.action=smooth
for layer in smooth.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for f in bag.fcurves:
    for k in f.keyframe_points:
     k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
s.frame_set(0);s.render.resolution_x=720;s.render.resolution_y=720
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Leap_Reference_Spline.blend'))
