import bpy, math, os
from mathutils import Vector

ROOT=r'C:\Users\d.grab\Desktop\the-game'
rig_path=os.path.join(ROOT,r'razlom\Assets\Resources\Characters\Pelag_v5\Runtime\Pelag_v5_MixamoRig.fbx')
anchor_path=os.path.join(ROOT,r'ART\PELAG\anchor+chain+weapon+3d+model.fbx')
out_blend=os.path.join(ROOT,r'ART\PELAG\animation\Pelag_AnchorSweep_Hook_v1.blend')
out_fbx=os.path.join(ROOT,r'ART\PELAG\animation\Pelag_AnchorSweep_Hook_v1.fbx')

bpy.ops.wm.read_homefile(use_empty=True,use_factory_startup=True)
bpy.ops.import_scene.fbx(filepath=rig_path, automatic_bone_orientation=False)
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
arm.name='ARM_Pelag_AnchorSweep'
mesh.name='Pelag_Body'
# Apply the FBX 0.01 armature scale so world dimensions are in meters.
a=bpy.data.actions.new('Pelag_AnchorSweep_Hook_v1')
arm.animation_data_create()
arm.animation_data.action=a
scene=bpy.context.scene
scene.render.fps=30
scene.frame_start=1; scene.frame_end=54
scene.frame_set(1)

# Helpers
pose=arm.pose
def pb(name): return pose.bones.get('mixamorig:'+name)
def key(name, frame, loc=None, rot=None):
    b=pb(name)
    if not b: return
    b.rotation_mode='XYZ'
    if loc is not None: b.location=loc
    if rot is not None: b.rotation_euler=rot
    b.keyframe_insert('location',frame=frame)
    b.keyframe_insert('rotation_euler',frame=frame)

def rot(name, frame, xyz): key(name,frame,rot=xyz)
# Clear inherited pose transforms and key a neutral starting pose.
for b in pose.bones:
    b.location=(0,0,0); b.rotation_mode='XYZ'; b.rotation_euler=(0,0,0); b.scale=(1,1,1)
    b.keyframe_insert('location',frame=1); b.keyframe_insert('rotation_euler',frame=1)

# Deliberate 1.8s action: anticipation, release, tension, return.
# Pose language: torso coils opposite hand, hook arm draws back then snaps forward.
poses={
  1: {'Spine':(0.02,-0.10,0.08),'Spine1':(0.04,-0.12,0.10),'Spine2':(0.05,-0.16,0.12),'RightShoulder':(0.0,0.0,-0.25),'RightArm':(0.35,0.15,-0.85),'RightForeArm':(0.0,0.25,-1.05),'RightHand':(0.0,0.0,-0.20),'LeftArm':(-0.10,0.0,0.18)},
  10:{'Spine':(0.02,-0.04,-0.06),'Spine1':(0.03,-0.06,-0.08),'Spine2':(0.04,-0.08,-0.10),'RightShoulder':(0.0,0.0,0.34),'RightArm':(-0.30,0.15,0.85),'RightForeArm':(0.0,-0.25,0.85),'RightHand':(0.0,0.0,0.35),'LeftArm':(-0.08,0.0,-0.20)},
  16:{'Spine':(0.0,0.03,0.02),'Spine1':(0.0,0.02,0.02),'Spine2':(0.0,0.04,0.04),'RightShoulder':(0.0,0.0,-0.15),'RightArm':(-0.85,0.0,-1.25),'RightForeArm':(0.0,0.0,-1.10),'RightHand':(0.0,0.0,-0.10),'LeftArm':(0.0,0.0,0.10)},
  22:{'Spine':(0.0,0.0,0.0),'Spine1':(0.0,0.0,0.0),'Spine2':(0.0,0.0,0.0),'RightShoulder':(0.0,0.0,0.0),'RightArm':(-0.55,0.0,-0.20),'RightForeArm':(0.0,0.0,-0.65),'RightHand':(0.0,0.0,0.0),'LeftArm':(0.0,0.0,0.0)},
  30:{'Spine':(0.0,0.0,0.0),'Spine1':(0.0,0.0,0.0),'Spine2':(0.0,0.0,0.0),'RightShoulder':(0.0,0.0,0.18),'RightArm':(-0.10,0.0,0.55),'RightForeArm':(0.0,0.0,0.25),'RightHand':(0.0,0.0,0.0),'LeftArm':(0.0,0.0,0.0)},
  38:{'Spine':(0.02,-0.04,0.05),'Spine1':(0.02,-0.05,0.06),'Spine2':(0.02,-0.06,0.07),'RightShoulder':(0.0,0.0,0.10),'RightArm':(-0.30,0.0,0.30),'RightForeArm':(0.0,0.0,0.55),'RightHand':(0.0,0.0,0.10),'LeftArm':(0.0,0.0,0.0)},
  48:{'Spine':(0.0,0.0,0.0),'Spine1':(0.0,0.0,0.0),'Spine2':(0.0,0.0,0.0),'RightShoulder':(0.0,0.0,-0.06),'RightArm':(-0.15,0.0,-0.10),'RightForeArm':(0.0,0.0,-0.15),'RightHand':(0.0,0.0,0.0),'LeftArm':(0.0,0.0,0.0)},
  54:{'Spine':(0.0,0.0,0.0),'Spine1':(0.0,0.0,0.0),'Spine2':(0.0,0.0,0.0),'RightShoulder':(0.0,0.0,-0.04),'RightArm':(-0.12,0.0,-0.08),'RightForeArm':(0.0,0.0,-0.12),'RightHand':(0.0,0.0,0.0),'LeftArm':(0.0,0.0,0.0)},
}
for f,vals in poses.items():
    for n,r in vals.items(): rot(n,f,r)
# Add subtle foot/hips weight shift to sell the pull.
for f,x in [(1,-0.03),(10,-0.06),(16,0.07),(22,0.04),(30,-0.03),(38,0.02),(48,0.0),(54,0.0)]: key('Hips',f,loc=(x,0,0))

# Find hand world transform for prop placement at key frames.
def hand_world(frame):
    scene.frame_set(frame); bpy.context.view_layer.update()
    b=pb('RightHand')
    return arm.matrix_world @ b.matrix @ Vector((0.0,0.0,0.13))

# Import hook/anchor as a separate animated prop, scaled to game size.
bpy.ops.import_scene.fbx(filepath=anchor_path, automatic_bone_orientation=False)
anchor=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o!=mesh)
anchor.name='PROP_Anchor_Hook'
anchor.scale=(0.72,0.72,0.72)
# FBX prop axis: rotate to read as a thrown hook in the camera.
anchor.rotation_mode='XYZ'
# Parent to a non-deforming control root for animation.
root=bpy.data.objects.new('CTRL_AnchorSweep',None); bpy.context.collection.objects.link(root)
anchor.parent=root
# hook trajectory: hand -> high arc -> behind target -> taut pull line.
traj={1:(-0.30,0.80,0.45),8:(-0.55,1.05,0.70),16:(-0.10,1.28,1.15),22:(0.55,1.15,1.35),30:(1.35,0.95,1.05),38:(1.75,0.70,0.55),46:(1.15,0.72,0.20),54:(0.60,0.78,0.35)}
for f,p in traj.items():
    root.location=p; root.rotation_euler=(0.0,0.0, math.radians(-25 + (f-1)*3.0)); root.keyframe_insert('location',frame=f); root.keyframe_insert('rotation_euler',frame=f)
# Default interpolation in Blender 5.2 layered actions is kept for this preview.\r\n
# Visible chain: 8 small cylinders between hand and hook; baked transforms per frame.
chain=[]
mat=bpy.data.materials.new('MAT_Chain'); mat.diffuse_color=(0.07,0.10,0.11,1.0); mat.metallic=0.65; mat.roughness=0.32
for i in range(8):
    bpy.ops.mesh.primitive_torus_add(major_radius=0.055, minor_radius=0.014, major_segments=12, minor_segments=6, location=(0,0,0))
    c=bpy.context.object; c.name=f'PROP_ChainLink_{i+1:02d}'; c.data.materials.append(mat); c.parent=None; chain.append(c)
# animate each link as a straight, slightly sagging chain from hand to hook.
for f in range(1,55):
    scene.frame_set(f); bpy.context.view_layer.update()
    h=hand_world(f)
    end=root.matrix_world.translation + Vector((0,0,0.15))
    for i,c in enumerate(chain):
        t=(i+1)/9.0
        p=h.lerp(end,t); p.z -= 0.20*math.sin(math.pi*t) * (0.35 if f<18 else 0.12)
        c.location=p
        direction=end-h
        c.rotation_mode='QUATERNION'
        if direction.length>1e-5: c.rotation_quaternion=direction.to_track_quat('Z','Y')
        c.scale=(1,1,1)
        c.keyframe_insert('location',frame=f); c.keyframe_insert('rotation_quaternion',frame=f)
# Make chain links visually alternating orientation.
for i,c in enumerate(chain): c.rotation_mode='XYZ'; c.rotation_euler[1]=math.radians(90 if i%2 else 0)

# Ground, camera, lights for review render.
bpy.ops.mesh.primitive_plane_add(size=12, location=(0,0,0)); ground=bpy.context.object; ground.name='REVIEW_Ground'
gmat=bpy.data.materials.new('MAT_Ground'); gmat.diffuse_color=(0.16,0.22,0.20,1); ground.data.materials.append(gmat)
bpy.ops.object.camera_add(location=(1.9,-3.2,1.75)); cam=bpy.context.object; cam.name='CAM_AnchorSweep'; scene.camera=cam
# point camera at upper body center
q=(Vector((0,0,0.58))-cam.location).to_track_quat('-Z','Y'); cam.rotation_euler=q.to_euler(); cam.data.lens=52
bpy.ops.object.light_add(type='AREA', location=(-3,-4,5)); keyL=bpy.context.object; keyL.data.energy=850; keyL.data.shape='DISK'; keyL.data.size=4
keyL.rotation_euler=((Vector((0,0,1))-keyL.location).to_track_quat('-Z','Y')).to_euler()
bpy.ops.object.light_add(type='AREA', location=(3,1,2.5)); fill=bpy.context.object; fill.data.energy=400; fill.data.size=3; fill.rotation_euler=((Vector((0,0,1))-fill.location).to_track_quat('-Z','Y')).to_euler()
scene.render.engine='BLENDER_EEVEE'; scene.render.resolution_x=720; scene.render.resolution_y=720; scene.render.resolution_percentage=100
scene.render.filepath=os.path.join(ROOT,r'ART\PELAG\animation\anchor_sweep_preview.png')
scene.world = bpy.data.worlds.new('World') if scene.world is None else scene.world
scene.world.color=(0.025,0.035,0.04)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
# Export selected animated objects + armature + mesh + chain and ground.
for o in bpy.context.scene.objects: o.select_set(False)
for o in [arm,mesh,anchor,root]+chain: o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'ARMATURE','MESH','EMPTY'}, bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y', apply_unit_scale=True)
bpy.ops.render.render(write_still=True)
print('SAVED',out_blend,out_fbx,scene.render.filepath)







