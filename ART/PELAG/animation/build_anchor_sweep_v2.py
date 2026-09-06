import bpy, os, math
from mathutils import Vector
ROOT=r'C:\Users\d.grab\Desktop\the-game'
rig=os.path.join(ROOT,r'razlom\Assets\Resources\Characters\Pelag_v5\Mixamo\Pelag_MX_AnchorSweep.fbx')
anchor_path=os.path.join(ROOT,r'ART\PELAG\anchor+chain+weapon+3d+model.fbx')
out_blend=os.path.join(ROOT,r'ART\PELAG\animation\Pelag_AnchorSweep_Hook_v2.blend')
out_fbx=os.path.join(ROOT,r'ART\PELAG\animation\Pelag_AnchorSweep_Hook_v2.fbx')
out_png=os.path.join(ROOT,r'ART\PELAG\animation\anchor_sweep_preview_v2.png')

bpy.ops.wm.read_homefile(use_empty=True,use_factory_startup=True)
bpy.ops.import_scene.fbx(filepath=rig,automatic_bone_orientation=False)
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE'); body=next(o for o in bpy.context.scene.objects if o.type=='MESH')
arm.name='ARM_Pelag_AnchorSweep'; body.name='Pelag_Body'
scene=bpy.context.scene; scene.render.fps=30; scene.frame_start=1; scene.frame_end=17
# retain and rename source verified body action
arm.animation_data.action.name='Pelag_AnchorSweep_Body_Source'
# Imported anchor
bpy.ops.import_scene.fbx(filepath=anchor_path,automatic_bone_orientation=False)
anchor=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o!=body); anchor.name='PROP_Anchor_Hook'
anchor.scale=(0.58,0.58,0.58); anchor.rotation_mode='XYZ'
# Anchor control with world-space keys, aligned to hand at first frames.
ctrl=bpy.data.objects.new('CTRL_AnchorSweep_Hook',None); scene.collection.objects.link(ctrl); anchor.parent=ctrl; anchor.location=(0,0,0)
# Hand position in world from actual source action.
hand_bone=arm.pose.bones.get('mixamorig:RightHand')
def hand_pos(frame):
 scene.frame_set(frame); bpy.context.view_layer.update(); return arm.matrix_world @ hand_bone.matrix @ Vector((0.0,0.0,0.12))
# Start attached, then throw to target behind enemies and pull toward player.
hand_frames={f:hand_pos(f) for f in range(1,18)}
traj={1:hand_frames[1],3:hand_frames[3],5:hand_frames[5],7:hand_frames[7],9:Vector((0.00,0.38,0.72)),11:Vector((0.55,0.38,1.05)),13:Vector((1.10,0.34,0.70)),15:Vector((0.78,0.30,0.48)),17:hand_frames[17]}
for f,p in traj.items():
 ctrl.location=p; ctrl.rotation_euler=(math.radians(-18),math.radians(25+f*9),math.radians(-22+f*2)); ctrl.keyframe_insert('location',frame=f); ctrl.keyframe_insert('rotation_euler',frame=f)
# Continuous chain curve, baked each frame from hand to hook. Renderable tube.
cu=bpy.data.curves.new('CURVE_AnchorChain','CURVE'); cu.dimensions='3D'; cu.resolution_u=1; cu.bevel_depth=0.022; cu.bevel_resolution=3
sp=cu.splines.new('POLY'); N=14; sp.points.add(N-1)
chain=bpy.data.objects.new('PROP_AnchorChain',cu); scene.collection.objects.link(chain)
mat=bpy.data.materials.new('MAT_Chain'); mat.diffuse_color=(0.06,0.10,0.12,1); mat.metallic=0.7; mat.roughness=0.3; cu.materials.append(mat)
for f in range(1,18):
 scene.frame_set(f); bpy.context.view_layer.update(); h=hand_frames[f]; e=ctrl.matrix_world.translation
 for i,pt in enumerate(sp.points):
  t=i/(N-1); p=h.lerp(e,t); p.z -= 0.10*math.sin(math.pi*t) if f<9 else 0.025*math.sin(math.pi*t); pt.co=(p.x,p.y,p.z,1); pt.keyframe_insert('co',frame=f)
# Add simple target to establish direction.
bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,radius=0.22,location=(1.10,0.34,0.22)); target=bpy.context.object; target.name='REVIEW_Target'; tm=bpy.data.materials.new('MAT_Target'); tm.diffuse_color=(0.55,0.05,0.04,1); target.data.materials.append(tm)
# Ground and camera close enough to read the body.
bpy.ops.mesh.primitive_plane_add(size=8,location=(0,0,0)); ground=bpy.context.object; gm=bpy.data.materials.new('MAT_Ground'); gm.diffuse_color=(0.12,0.18,0.17,1); ground.data.materials.append(gm)
bpy.ops.object.camera_add(location=(1.65,-2.65,1.30)); cam=bpy.context.object; cam.name='CAM_AnchorSweep'; scene.camera=cam; cam.data.lens=58; cam.rotation_euler=((Vector((0,0,0.52))-cam.location).to_track_quat('-Z','Y')).to_euler()
# Workbench gives reliable textured-free silhouette preview.
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.studio_light='paint.sl'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True; scene.display.shading.cavity_type='WORLD'; scene.display.shading.curvature_ridge_factor=1.5; scene.display.shading.curvature_valley_factor=1.2
scene.render.resolution_x=900; scene.render.resolution_y=700; scene.render.resolution_percentage=100; scene.render.filepath=out_png
scene.frame_set(9)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
# Convert chain curve to mesh for FBX export while preserving animation.
for o in scene.objects: o.select_set(False)
for o in [arm,body,anchor,ctrl,chain,target]: o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=out_fbx,use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
bpy.ops.render.render(write_still=True)
print('SAVED',out_blend,out_fbx,out_png)

