import bpy,json,math,sys
from pathlib import Path
from mathutils import Matrix,Vector
BASE=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model');OUT=BASE/'revision-02'
bpy.ops.wm.open_mainfile(filepath=str(BASE/'Pelag_Model_Candidate.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['ARM_Pelag_v6_Original65'];body=bpy.data.objects['SM_Pelag_v6_Unchanged'];hero=bpy.data.collections['COL_Pelag_Candidate']
saber=bpy.data.objects['SM_Pelag_saber_Candidate'];anchor=bpy.data.objects['SM_Pelag_anchor_Candidate'];eye=bpy.data.objects['SM_Pelag_AnchorEye'];grip=bpy.data.objects['SM_Pelag_grip_Candidate'];template=bpy.data.objects['SM_Pelag_link_Candidate']
data=json.loads((OUT/'unity_mount_samples.json').read_text());rows={r['label']:r for r in data['rows']}
def m(a):return Matrix([a[i:i+4] for i in range(0,16,4)])
def serial(m):return [list(r) for r in m]
# Unity left-handed Y-up -> original Blender right-handed Z-up. Review unit
# normalization exactly undoes Unity's importer/global body scale, not weapon fit.
C=Matrix(((-1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
C=Matrix.Scale(1/(.5433*1.82),4)@C
Ubind={x['name']:Matrix.Scale(100,4)@m(x['matrix']) for x in rows['idle']['body']['bindBones']}
Bbind={p.name:rig.matrix_world@p.bone.matrix_local for p in rig.pose.bones}
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
connected_names=[b.name for b in rig.data.edit_bones if b.use_connect]
for b in rig.data.edit_bones:b.use_connect=False
bpy.ops.object.mode_set(mode='OBJECT')
print('BIND_DIFF',json.dumps(sorted([(n,(Bbind[n].translation-(C@u).translation).length,list(Bbind[n].translation),list((C@u).translation)) for n,u in Ubind.items()],key=lambda x:-x[1])[:12]))
def pose(label):
 row=rows[label]
 for p in sorted(rig.pose.bones,key=lambda p:len(p.parent_recursive)):
  world=C@m(row['bones'][p.name])@Ubind[p.name].inverted()@C.inverted()@Bbind[p.name]
  p.matrix=rig.matrix_world.inverted()@world
  bpy.context.view_layer.update()
 return row
def remove_parent(o):
 bpy.context.view_layer.update();wm=o.matrix_world.copy();o.parent=None;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_world=wm;bpy.context.view_layer.update()
for o in list(hero.objects):
 if o.name.startswith('SM_Pelag_CarryLink'):bpy.data.objects.remove(o,do_unlink=True)
for o in [saber,anchor,eye,grip]:remove_parent(o)
row=pose('idle')
# Geometry had native FBX transforms baked in the geometry-only optimization.
# Unity imports saber vertices with X reflected; preserve that explicit frame.
saber.matrix_world=C@m(row['sword']['meshes'][0]['matrix'])@Matrix.Diagonal(Vector((-1,1,1,1)))
bpy.context.view_layer.update()
# Bone parenting's implicit tail offset is cancelled explicitly by inverse of
# the actual evaluated parent frame. World equals boneHEAD_world * localMount.
def parent_head(o,bone):
 bpy.context.view_layer.update();wanted=o.matrix_world.copy()
 o.parent=rig;o.parent_type='BONE';o.parent_bone=bone;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_basis=Matrix.Identity(4);bpy.context.view_layer.update()
 parentframe=o.matrix_world.copy();o.matrix_basis=parentframe.inverted()@wanted;bpy.context.view_layer.update()
 return serial((rig.matrix_world@rig.pose.bones[bone].matrix).inverted()@o.matrix_world)
saber_local=parent_head(saber,'mixamorig:RightHand')
# Quick render exact grip first. Anchor remains hidden until seating reauthored.
anchor.hide_render=True;eye.hide_render=True;grip.hide_render=True
scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.cycles.samples=16
def render(name,loc,target,ortho):
 cam=scene.camera;cam.location=loc;cam.data.ortho_scale=ortho;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
hand=rig.matrix_world@rig.pose.bones['mixamorig:RightHand'].head
print('EXACT_GRIP',hand[:],serial(saber.matrix_world))
if '--audit-only' in sys.argv:raise SystemExit()
render('grip_front_preview',hand+Vector((.12,-3,.10)),hand,.58)
render('grip_side_preview',hand+Vector((-3,.15,.05)),hand,.58)
render('whole_preview',(3,-4,4.2),(0,0,.95),2.25)
(OUT/'mount_diagnosis.json').write_text(json.dumps({'unity_source':data['source'],'coordinate_conversion':serial(C),'saber_head_local':saber_local,'bone_world_max_translation_error_m':max(((rig.matrix_world@p.matrix).translation-(C@m(row['bones'][p.name])).translation).length for p in rig.pose.bones),'source_note':'World bone matrices from existing imported .anim with KnifeIdle_Grounded initialization; native FBX bind inverse used. No visual hand placement.'},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Mount_Intermediate.blend'))
