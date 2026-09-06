import bpy, os, math
from mathutils import Vector, Quaternion, Euler
ROOT=r'C:\Users\d.grab\Desktop\the-game'; rig=os.path.join(ROOT,r'razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_AnchorSweep.fbx'); anchor_path=os.path.join(ROOT,r'ART\PELAG\anchor+chain+weapon+3d+model.fbx'); outdir=os.path.join(ROOT,r'ART\PELAG\animation'); os.makedirs(outdir,exist_ok=True)
outblend=os.path.join(outdir,'Pelag_AnchorSweep_Hook_v3.blend'); outfbx=os.path.join(outdir,'Pelag_AnchorSweep_Hook_v3.fbx')
# clean
bpy.ops.wm.read_homefile(use_empty=True,use_factory_startup=True); bpy.ops.import_scene.fbx(filepath=rig,automatic_bone_orientation=False)
scene=bpy.context.scene; scene.render.fps=30; scene.frame_start=1; scene.frame_end=60
arm=next(o for o in scene.objects if o.type=='ARMATURE'); body=next(o for o in scene.objects if o.type=='MESH'); arm.name='ARM_Pelag_AnchorSweep'; body.name='Pelag_Body'
# capture source local pose
src_action=arm.animation_data.action; bones=list(arm.pose.bones); samples=[]
for sf in range(2,18):
 scene.frame_set(sf); bpy.context.view_layer.update(); row={}
 for b in bones:
  row[b.name]=(b.location.copy(), b.rotation_quaternion.copy(), b.scale.copy(), b.rotation_mode)
 samples.append(row)
# new action
arm.animation_data.action=None; act=bpy.data.actions.new('Pelag_AnchorSweep_Hook_v3'); arm.animation_data.action=act
# helpers
custom_frames={1:(0,0,0),8:(0,0,0),16:(0.06,-0.04,0.12),22:(0.10,-0.08,0.18),28:(0.0,0.0,0.0),36:(-0.08,0.04,-0.10),46:(-0.04,0.02,-0.04),60:(0,0,0)}
def tweak(name, f, pose):
 b=pose.get(name)
 if not b:return
 if name in ('mixamorig:Spine','mixamorig:Spine1','mixamorig:Spine2'):
  x,y,z=custom_frames[min(custom_frames,key=lambda k:abs(k-f))]; b.rotation_mode='XYZ'; e=b.rotation_euler; b.rotation_euler=(e.x+x,e.y+y,e.z+z)
# resample source motion smoothly to 60 frames
for f in range(1,61):
 t=(f-1)/59.0; q=t*15.0; i=min(14,int(math.floor(q))); u=q-i; a=samples[i]; z=samples[min(i+1,15)]
 for b in bones:
  loc=a[b.name][0].lerp(z[b.name][0],u); rot=a[b.name][1].slerp(z[b.name][1],u); b.location=loc; b.rotation_mode='QUATERNION'; b.rotation_quaternion=rot; b.scale=a[b.name][2].lerp(z[b.name][2],u)
 # exaggerate the pull arc with stable torso and arm accents
 for n in ('mixamorig:Spine','mixamorig:Spine1','mixamorig:Spine2'): tweak(n,f,arm.pose.bones)
 # add release and hard-pull accents
 if f in (16,22,28,36,46,60):
  for b in bones:
   b.keyframe_insert('location',frame=f); b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('scale',frame=f)
# ensure every frame has keys for smooth interpolation
for f in range(1,61):
 for b in bones:
  if not b.bone.select: pass
  b.keyframe_insert('location',frame=f); b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('scale',frame=f)
# hook
bpy.ops.import_scene.fbx(filepath=anchor_path,automatic_bone_orientation=False)
hook=next(o for o in scene.objects if o.type=='MESH' and o!=body); hook.name='PROP_Anchor_Hook'; hook.scale=(0.50,0.50,0.50)
hook.data.materials.clear(); hm=bpy.data.materials.new('MAT_Hook'); hm.diffuse_color=(0.035,0.05,0.065,1); hm.metallic=0.8; hm.roughness=0.25; hook.data.materials.append(hm)
ctrl=bpy.data.objects.new('CTRL_AnchorSweep_Hook',None); scene.collection.objects.link(ctrl); hook.parent=ctrl; hook.location=(0,0,0)
hand=arm.pose.bones['mixamorig:RightHand']
def handpos(f): scene.frame_set(f); bpy.context.view_layer.update(); return arm.matrix_world @ hand.matrix @ Vector((0,0,0.14))
handpos_cache={f:handpos(f) for f in range(1,61)}
traj={1:handpos_cache[1],8:handpos_cache[8],12:Vector((0.00,0.36,0.70)),18:Vector((0.32,0.40,0.94)),24:Vector((0.78,0.38,0.88)),30:Vector((1.10,0.36,0.62)),34:Vector((1.10,0.36,0.62)),38:Vector((1.10,0.36,0.62)),46:Vector((0.74,0.34,0.45)),54:Vector((0.36,0.32,0.35)),60:handpos_cache[60]}
for f,p in traj.items(): ctrl.location=p; ctrl.rotation_mode='XYZ'; ctrl.rotation_euler=(math.radians(-15),math.radians(15+f*8),math.radians(-22+f)); ctrl.keyframe_insert('location',frame=f); ctrl.keyframe_insert('rotation_euler',frame=f)
# chain links: actual torus geometry, visible and growing by scale
links=[]; cm=bpy.data.materials.new('MAT_Chain'); cm.diffuse_color=(0.03,0.06,0.075,1); cm.metallic=0.75; cm.roughness=0.3
for i in range(12):
 bpy.ops.mesh.primitive_torus_add(major_radius=0.045,minor_radius=0.012,major_segments=12,minor_segments=6,location=(0,0,0))
 o=bpy.context.object; o.name=f'PROP_ChainLink_{i+1:02d}'; o.data.materials.append(cm); links.append(o)
# bake each link transform every frame, scale from 0 to 1 as chain extends
for f in range(1,61):
 scene.frame_set(f); bpy.context.view_layer.update(); h=handpos_cache[f]; e=ctrl.matrix_world.translation
 # release frame 10, impact/tension 34, retract after 40
 if f<=10: progress=max(0.0,(f-2)/8.0); end=h.lerp(Vector((0.18,0.38,0.70)),progress)
 elif f<=34: end=ctrl.matrix_world.translation
 else: end=ctrl.matrix_world.translation
 for i,o in enumerate(links):
  t=(i+1)/12.0; p=h.lerp(end,t); sag=(0.08 if f<22 else 0.018)*math.sin(math.pi*t); p.z-=sag
  o.location=p; d=end-h; o.rotation_mode='QUATERNION'; o.rotation_quaternion=d.to_track_quat('Z','Y') if d.length>1e-5 else Quaternion.Identity(); o.rotation_mode='XYZ'; o.rotation_euler.rotate_axis('Z',math.radians(90 if i%2 else 0))
  count=round(max(0,min(12,(f-3)/1.7))) if f<18 else 12
  s=1.0 if i<count else 0.0
  o.scale=(s,s,s); o.keyframe_insert('location',frame=f); o.keyframe_insert('rotation_euler',frame=f); o.keyframe_insert('scale',frame=f)
# review target and ground
bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=0.22,location=(1.10,0.36,0.22)); target=bpy.context.object; target.name='REVIEW_Target'; tm=bpy.data.materials.new('MAT_Target'); tm.diffuse_color=(0.68,0.04,0.03,1); target.data.materials.append(tm)
bpy.ops.mesh.primitive_plane_add(size=7,location=(0,0,0)); floor=bpy.context.object; floor.name='REVIEW_Ground'; fm=bpy.data.materials.new('MAT_Ground'); fm.diffuse_color=(0.12,0.19,0.18,1); floor.data.materials.append(fm)
bpy.ops.object.camera_add(location=(1.58,-2.65,1.12)); cam=bpy.context.object; cam.name='CAM_AnchorSweep'; scene.camera=cam; cam.data.lens=58; cam.rotation_euler=((Vector((0.40,0.25,0.55))-cam.location).to_track_quat('-Z','Y')).to_euler()
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.studio_light='paint.sl'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True; scene.render.resolution_x=900; scene.render.resolution_y=700; scene.render.resolution_percentage=100
# save and export
scene.frame_set(34); bpy.ops.wm.save_as_mainfile(filepath=outblend)
for o in scene.objects:o.select_set(False)
for o in [arm,body,hook,ctrl]+links:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=outfbx,use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
# render key frames
for f in [1,8,12,18,24,30,34,40,46,54,60]:
 scene.frame_set(f); scene.render.filepath=os.path.join(outdir,f'anchor_sweep_v3_f{f:02d}.png'); bpy.ops.render.render(write_still=True)
print('SAVED',outblend,outfbx)
