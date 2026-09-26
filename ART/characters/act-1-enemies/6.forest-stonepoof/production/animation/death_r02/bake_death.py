import bpy,json,math
from pathlib import Path
from mathutils import Matrix
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Death_r02.blend'))
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];names=[b.name for b in arm.data.bones if b.use_deform]
samples=[]
for i in range(961):
 f=i/16;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
 samples.append((f,{name:arm.pose.bones[name].matrix.copy() for name in names}))
arm.animation_data_clear();arm.parent=None;arm.matrix_world=Matrix.Identity(4)
for b in arm.pose.bones:
 for c in list(b.constraints):b.constraints.remove(c)
for o in list(bpy.data.objects):
 if o.name.startswith(('CTRL_','QA_')):bpy.data.objects.remove(o,do_unlink=True)
bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for b in list(arm.data.edit_bones):
 if not b.use_deform:arm.data.edit_bones.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')
act=bpy.data.actions.new('AN_Stonehoof_Death');act.use_fake_user=True;arm.animation_data_create();arm.animation_data.action=act
previous={}
for f,pose in samples:
 for name in names:
  b=arm.pose.bones[name];b.rotation_mode='QUATERNION';b.matrix=pose[name];b.scale=(1,1,1);q=b.rotation_quaternion.copy()
  if name in previous and q.dot(previous[name])<0:q.negate()
  b.rotation_quaternion=q;previous[name]=q
  b.keyframe_insert('location',frame=f,group=name);b.keyframe_insert('rotation_quaternion',frame=f,group=name);bpy.context.view_layer.update()
for layer in act.layers:
 for strip in layer.strips:
  for bag in strip.channelbags:
   for fc in bag.fcurves:
    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
errors=[];max_angle=0
for f,expected in samples:
 scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
 for name in names:
  b=arm.pose.bones[name];errors.append((b.matrix.translation-expected[name].translation).length)
  q=b.matrix.to_quaternion().normalized();eq=expected[name].to_quaternion().normalized();max_angle=max(max_angle,math.degrees(2*math.acos(min(1,abs(q.dot(eq))))))
for old in list(bpy.data.actions):
 if old!=act:bpy.data.actions.remove(old)
act.use_frame_range=True;act.frame_start=0;act.frame_end=60
arm['root_motion']='No object/carrier travel. Bone body offsets express the collapse.'
arm['clip_status']='Death candidate. Owner review pending.'
arm.animation_data.action=None
track=arm.animation_data.nla_tracks.new();track.name='Death';strip=track.strips.new(act.name,0,act);strip.action_frame_start=0;strip.action_frame_end=60;strip.extrapolation='HOLD_FORWARD'
scene.frame_set(0);bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_Death_Baked_r02.blend'))
report={'deforming_bones':len(names),'duration_seconds':2,'sample_step_frames':1/16,'maximum_baked_joint_error_m':max(errors),'maximum_baked_rotation_error_degrees':max_angle,'owner_approved':False,'constraints':0,'travel_removed':True}
(HERE/'bake_validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report));assert max(errors)<.001
