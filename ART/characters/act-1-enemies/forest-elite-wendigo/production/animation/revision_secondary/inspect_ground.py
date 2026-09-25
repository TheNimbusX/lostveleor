import bpy
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.find_armature()==rig)
for clip,f in [('Death',28),('Death',44),('Death',60)]:
 rig.animation_data.action=bpy.data.actions[f'AN_ForestWendigo_{clip}']; bpy.context.scene.frame_set(f)
 obj=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()); m=obj.to_mesh()
 rows=[]
 for v in m.vertices:
  p=obj.matrix_world@v.co
  if p.z<-.03:
   weights=sorted(((mesh.vertex_groups[g.group].name,round(g.weight,2)) for g in mesh.data.vertices[v.index].groups),key=lambda g:-g[1])
   rows.append((round(p.z,3),v.index,tuple(round(x,2) for x in p),weights[:2]))
 print('GROUND',clip,f,len(rows),sorted(rows)[:25])
 obj.to_mesh_clear()
