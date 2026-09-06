import bpy, os, math
from mathutils import Vector, Quaternion, Euler
ROOT=r'C:\Users\d.grab\Desktop\the-game'; OUT=os.path.join(ROOT,r'ART\PELAG\animation\anchor_sweep_review'); blend=os.path.join(OUT,'Pelag_AnchorSweep_Complete.blend')
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene; arm=bpy.data.objects.get('ARM_Pelag_v6'); prop=bpy.data.objects.get('CTRL_AnchorRig'); grip=bpy.data.objects.get('PROP_AnchorGrip'); hook=bpy.data.objects.get('PROP_AnchorHead'); links=[bpy.data.objects.get(f'PROP_ChainLink_{i+1:02d}') for i in range(18)]
# detach grip for correct world hand contact
grip.parent=None
hand=arm.pose.bones.get('mixamorig:LeftHand') or arm.pose.bones.get('mixamorig:RightHand')
hp={}
for f in range(1,61):
 scene.frame_set(f); bpy.context.view_layer.update(); hp[f]=arm.matrix_world @ hand.matrix.translation
# reconstruct prop trajectory and grip world transform
start=hp[1]+Vector((0.08,0,0.10)); behind=Vector((1.70,.28,.66)); targetP=Vector((1.45,.15,.52))
for f in range(1,61):
 scene.frame_set(f)
 if f<=10: p=start
 elif f<=22: p=start.lerp(behind,(f-10)/12)
 elif f<=34: p=behind
 elif f<=42: p=behind.lerp(targetP,(f-34)/8)
 elif f<=49: p=targetP
 else: p=targetP.lerp(hp[f]+Vector((.08,0,.10)),(f-49)/11)
 prop.location=p; d=p-hp[f]; prop.rotation_mode='QUATERNION'; prop.rotation_quaternion=(d.to_track_quat('Z','Y') if d.length>1e-4 else Quaternion((1,0,0,0))); prop.keyframe_insert('location',frame=f); prop.keyframe_insert('rotation_quaternion',frame=f)
 grip.location=hp[f]+Vector((.03,0,.05)); grip.rotation_mode='QUATERNION'; grip.rotation_quaternion=Quaternion((1,0,0,0)); grip.keyframe_insert('location',frame=f); grip.keyframe_insert('rotation_quaternion',frame=f)
 # chain
 if f<10: prog=0
 elif f<24: prog=(f-10)/14
 elif f<49: prog=1
 else: prog=max(0,(60-f)/11)
 a=hp[f]; b=p; bend=(a+b)*.5 + Vector((0, .22 if f<24 else .05, .22 if f<24 else .02))
 for i,o in enumerate(links):
  t=(i+0.5)/len(links); vis=prog*len(links)-i; u=min(1,max(0,vis)); q=(1-u)*0.001+u
  pp=(1-t)*(1-t)*a+2*(1-t)*t*bend+t*t*b; nxt=min(1,t+.03); pp2=(1-nxt)*(1-nxt)*a+2*(1-nxt)*nxt*bend+nxt*nxt*b; tan=pp2-pp
  o.location=pp; o.rotation_mode='QUATERNION'; base=(tan.to_track_quat('Z','Y') if tan.length>1e-4 else Quaternion((1,0,0,0)))
  o.rotation_quaternion=(base @ Euler((math.radians(90) if i%2 else 0,0,0),'XYZ').to_quaternion()).normalized(); o.scale=(q,q*.58,q); o.keyframe_insert('location',frame=f); o.keyframe_insert('rotation_quaternion',frame=f); o.keyframe_insert('scale',frame=f)
# render corrected frames
for f in [1,7,12,18,24,30,34,40,46,52,60]:
 scene.frame_set(f); scene.render.filepath=os.path.join(OUT,f'frame_{f:02d}.png'); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=blend)
# re-export
bpy.ops.object.select_all(action='DESELECT')
for o in [arm,bpy.data.objects.get('Pelag_Body'),prop,grip,hook]+links:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Pelag_AnchorSweep_Complete.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
