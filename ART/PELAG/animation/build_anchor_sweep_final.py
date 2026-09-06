import bpy, os, math
from mathutils import Vector
ROOT=r'C:\Users\d.grab\Desktop\the-game'
rig=os.path.join(ROOT,r'razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_AnchorSweep.fbx')
anchor_path=os.path.join(ROOT,r'ART\PELAG\anchor+chain+weapon+3d+model.fbx')
out_dir=os.path.join(ROOT,r'ART\PELAG\animation'); os.makedirs(out_dir,exist_ok=True)
out_blend=os.path.join(out_dir,'Pelag_AnchorSweep_Hook_FINAL.blend'); out_fbx=os.path.join(out_dir,'Pelag_AnchorSweep_Hook_FINAL.fbx')

bpy.ops.wm.read_homefile(use_empty=True,use_factory_startup=True)
bpy.ops.import_scene.fbx(filepath=rig,automatic_bone_orientation=False)
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE'); body=next(o for o in bpy.context.scene.objects if o.type=='MESH')
arm.name='ARM_Pelag_AnchorSweep'; body.name='Pelag_Body'
scene=bpy.context.scene; scene.render.fps=30; scene.frame_start=1; scene.frame_end=60
# Make the source body read as the player in workbench preview.
body_mat=bpy.data.materials.new('MAT_Pelag_Coral'); body_mat.diffuse_color=(0.95,0.18,0.28,1); body.data.materials.clear(); body.data.materials.append(body_mat)
# Anchor prop
bpy.ops.import_scene.fbx(filepath=anchor_path,automatic_bone_orientation=False)
anchor=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o!=body); anchor.name='PROP_Anchor_Hook'; anchor.scale=(0.52,0.52,0.52)
hook_mat=bpy.data.materials.new('MAT_Anchor_Dark'); hook_mat.diffuse_color=(0.07,0.10,0.12,1); hook_mat.metallic=0.75; hook_mat.roughness=0.28; anchor.data.materials.clear(); anchor.data.materials.append(hook_mat)
ctrl=bpy.data.objects.new('CTRL_AnchorSweep_Hook',None); scene.collection.objects.link(ctrl); anchor.parent=ctrl; anchor.location=(0,0,0)
hand=arm.pose.bones['mixamorig:RightHand']
def hp(f): scene.frame_set(f); bpy.context.view_layer.update(); return arm.matrix_world @ hand.matrix @ Vector((0,0,0.14))
hand_pos={f:hp(f) for f in range(1,18)}
# Explicit stages: hand carry, release, arc, hard tension, retract.
traj={1:hand_pos[1],2:hand_pos[2],4:hand_pos[4],6:Vector((0.02,0.36,0.70)),8:Vector((0.38,0.40,0.92)),10:Vector((0.92,0.38,0.76)),11:Vector((1.12,0.36,0.58)),12:Vector((1.12,0.36,0.58)),13:Vector((1.12,0.36,0.58)),15:Vector((0.72,0.34,0.46)),17:hand_pos[17]}
for f,p in traj.items():
 ctrl.location=p; ctrl.rotation_mode='XYZ'; ctrl.rotation_euler=(math.radians(-12),math.radians(18+f*14),math.radians(-20+f*2)); ctrl.keyframe_insert('location',frame=f); ctrl.keyframe_insert('rotation_euler',frame=f)
# Curve chain, baked per frame from real hand to hook.
cu=bpy.data.curves.new('CURVE_AnchorChain','CURVE'); cu.dimensions='3D'; cu.resolution_u=1; cu.bevel_depth=0.018; cu.bevel_resolution=3
sp=cu.splines.new('POLY'); N=18; sp.points.add(N-1)
chain=bpy.data.objects.new('PROP_AnchorChain',cu); scene.collection.objects.link(chain); cu.materials.append(hook_mat)
for f in range(1,18):
 scene.frame_set(f); bpy.context.view_layer.update(); h=hand_pos[f]; e=ctrl.matrix_world.translation
 for i,pnt in enumerate(sp.points):
  t=i/(N-1); p=h.lerp(e,t); sag=(0.11 if f<10 else 0.018)*math.sin(math.pi*t); p.z-=sag; pnt.co=(p.x,p.y,p.z,1); pnt.keyframe_insert('co',frame=f)
# Target and floor
bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=0.23,location=(1.12,0.36,0.23)); target=bpy.context.object; target.name='REVIEW_Target'; target.data.materials.append(bpy.data.materials.new('MAT_Target')); target.data.materials[0].diffuse_color=(0.65,0.04,0.03,1)
bpy.ops.mesh.primitive_plane_add(size=7,location=(0,0,0)); floor=bpy.context.object; floor.name='REVIEW_Ground'; fm=bpy.data.materials.new('MAT_Ground'); fm.diffuse_color=(0.13,0.19,0.18,1); floor.data.materials.append(fm)
# Camera and lights
bpy.ops.object.camera_add(location=(1.52,-2.55,1.12)); cam=bpy.context.object; cam.name='CAM_AnchorSweep'; scene.camera=cam; cam.data.lens=58; cam.rotation_euler=((Vector((0.38,0.25,0.55))-cam.location).to_track_quat('-Z','Y')).to_euler()
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.studio_light='paint.sl'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True; scene.display.shading.cavity_type='WORLD'; scene.display.shading.curvature_ridge_factor=1.6; scene.display.shading.curvature_valley_factor=1.2
scene.render.resolution_x=900; scene.render.resolution_y=700; scene.render.resolution_percentage=100
# Save at the tension frame
scene.frame_set(12); bpy.ops.wm.save_as_mainfile(filepath=out_blend)
# Export animated package
for o in scene.objects: o.select_set(False)
for o in [arm,body,anchor,ctrl,chain]: o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=out_fbx,use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
# Render keyframe contact sheet frames.
from PIL import Image, ImageDraw
imgs=[]
for f in [1,4,8,11,12,15,17]:
 scene.frame_set(f); scene.render.filepath=os.path.join(out_dir,f'_anchor_sweep_{f:02d}.png'); bpy.ops.render.render(write_still=True); imgs.append(Image.open(scene.render.filepath).convert('RGB').resize((450,350)))
sheet=Image.new('RGB',(1350,1050),(20,25,25)); d=ImageDraw.Draw(sheet)
for i,im in enumerate(imgs):
 x=(i%3)*450; y=(i//3)*350; sheet.paste(im,(x,y)); d.text((x+12,y+12),f'F{[1,4,8,11,12,15,17][i]}',fill=(255,255,255))
sheet.save(os.path.join(out_dir,'Pelag_AnchorSweep_Hook_FINAL_contactsheet.png'))
print('FINAL',out_blend,out_fbx,os.path.join(out_dir,'Pelag_AnchorSweep_Hook_FINAL_contactsheet.png'))

