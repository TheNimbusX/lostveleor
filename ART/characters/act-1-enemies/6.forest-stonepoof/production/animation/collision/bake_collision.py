"""Bake the deform rig without preview travel, preserving editable master separately."""
import bpy,json,math
from pathlib import Path
from mathutils import Matrix
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Collision_r01.blend'))
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof']
names=[b.name for b in arm.data.bones if b.use_deform]
samples=[]
for i in range(769):
    f=i/16;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
    samples.append((f,{name:arm.pose.bones[name].matrix.copy() for name in names}))
arm.animation_data_clear();arm.parent=None;arm.matrix_world=Matrix.Identity(4)
for p in arm.pose.bones:
    for c in list(p.constraints):p.constraints.remove(c)
for o in list(bpy.data.objects):
    if o.name.startswith(('CTRL_','QA_')):bpy.data.objects.remove(o,do_unlink=True)
bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for b in list(arm.data.edit_bones):
    if not b.use_deform:arm.data.edit_bones.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')
actions={};errors=[]
for action_name,start,end in [('AN_Stonehoof_WallBrace',8,12),('AN_Stonehoof_WallImpact',12,48),('AN_Stonehoof_CollisionReview',0,48)]:
    act=bpy.data.actions.new(action_name);act.use_fake_user=True;arm.animation_data_create();arm.animation_data.action=act
    previous={}
    for source_frame,pose in samples:
        if not start<=source_frame<=end:continue
        frame=source_frame-start
        for name in names:
            b=arm.pose.bones[name];b.rotation_mode='QUATERNION';b.matrix=pose[name];b.scale=(1,1,1)
            q=b.rotation_quaternion.copy()
            if name in previous and q.dot(previous[name])<0:q.negate()
            b.rotation_quaternion=q;previous[name]=q
            b.keyframe_insert('location',frame=frame,group=name);b.keyframe_insert('rotation_quaternion',frame=frame,group=name)
            bpy.context.view_layer.update()
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'
    actions[action_name]=act
full=actions['AN_Stonehoof_CollisionReview'];arm.animation_data.action=full
prev={};max_step=0;max_angle_error=0;max_step_at=None
for f,expected in samples:
    scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
    err=0
    for name in names:
        b=arm.pose.bones[name];err=max(err,(b.matrix.translation-expected[name].translation).length)
        q=b.matrix.to_quaternion()
        if name in prev:
            step=math.degrees(2*math.acos(min(1,abs(q.normalized().dot(prev[name].normalized())))))
            if step>max_step:max_step=step;max_step_at={'bone':name,'frame':f}
        eq=expected[name].to_quaternion().normalized()
        max_angle_error=max(max_angle_error,math.degrees(2*math.acos(min(1,abs(q.normalized().dot(eq))))))
        prev[name]=q
    errors.append(err)
for old in list(bpy.data.actions):
    if old not in actions.values():bpy.data.actions.remove(old)
for name,act in actions.items():
    act.use_frame_range=True;act.frame_start=0;act.frame_end={'AN_Stonehoof_WallBrace':4,'AN_Stonehoof_WallImpact':36,'AN_Stonehoof_CollisionReview':48}[name]
arm.animation_data.action=None
track=arm.animation_data.nla_tracks.new();track.name='Review only · approach, impact and stun';strip=track.strips.new(full.name,0,full);strip.action_frame_start=0;strip.action_frame_end=48;strip.extrapolation='NOTHING'
arm['runtime_actions']='AN_Stonehoof_WallBrace (0.133333s within run), AN_Stonehoof_WallImpact (1.2s from Sim collision). AN_Stonehoof_CollisionReview includes approach for review only.'
arm['root_motion']='No object translation; preview travel removed. Body offsets only express weight transfer.'
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Stonehoof_Collision_Baked_r01.blend'))
report={'deforming_bones':len(names),'runtime_clips':{'AN_Stonehoof_WallBrace':4/30,'AN_Stonehoof_WallImpact':1.2},'sample_step_frames':.0625,'maximum_baked_joint_position_error_m':max(errors),'maximum_baked_rotation_error_degrees':max_angle_error,'maximum_sixteenth_frame_rotation_change_degrees':max_step,'maximum_step_at':max_step_at,'root_travel_removed':True,'scale_curves':False,'owner_approved':False}
(HERE/'bake_validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
assert max(errors)<.001,report
