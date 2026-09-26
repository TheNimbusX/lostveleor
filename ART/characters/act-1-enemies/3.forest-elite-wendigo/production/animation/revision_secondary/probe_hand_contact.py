import bpy, math
from mathutils import Vector,Quaternion
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.find_armature()==rig)
rig.animation_data.action=bpy.data.actions['AN_ForestWendigo_Death']
rest={b.name:b.bone.matrix_local.copy() for b in rig.pose.bones}
groups={}
for s in ('L','R'):
 gid=mesh.vertex_groups[f'{s}_hand'].index
 groups[s]=[v.index for v in mesh.data.vertices if sum(g.weight for g in v.groups if g.group==gid)>=.45]
for s in ('L','R'):
 print('HAND',s)
 for dz in (0,-.1,-.2,-.3):
  for angle in (-80,-60,-40,-20,0):
   bpy.context.scene.frame_set(60)
   bone=rig.pose.bones[f'{s}_hand']
   axes=rest[bone.name].to_3x3()
   rot=(axes.inverted()@Quaternion(Vector((1,0,0)),math.radians(angle)).to_matrix()@axes).to_quaternion()
   bone.rotation_quaternion=bone.rotation_quaternion@rot
   control=rig.pose.bones[f'CTRL_{s}_hand']
   control.location+=rest[control.name].to_3x3().inverted()@Vector((0,0,dz))
   bpy.context.view_layer.update()
   obj=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=obj.to_mesh()
   mins=min((obj.matrix_world@m.vertices[i].co).z for i in groups[s]);obj.to_mesh_clear()
   print('VAR',s,dz,angle,round(mins,3),round(bone.head.z,3),round(control.head.z,3))
