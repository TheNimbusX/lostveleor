"""Search small support-foot corrections by surface contact patch."""
import itertools
import math
import bpy
from mathutils import Quaternion, Vector

scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
obj=next(o for o in scene.objects if o.type=='MESH' and o.find_armature()==rig)
gs={g.name:g.index for g in obj.vertex_groups}
ids=[v.index for v in obj.data.vertices if any(g.group in {gs['R_foot'],gs['R_toe']} and g.weight>=.25 for g in v.groups)]
scene.frame_set(19)
bone=rig.pose.bones['R_foot']
base=bone.rotation_quaternion.copy()
rest=bone.bone.matrix_local.to_3x3()
choices=[]
for x,y,z in itertools.product(range(0,46,5),range(-20,31,10),range(-40,21,10)):
    q=base.copy()
    for axis,deg in (((1,0,0),x),((0,1,0),y),((0,0,1),z)):
        turn=Quaternion(Vector(axis),math.radians(deg))
        q=q @ (rest.inverted() @ turn.to_matrix() @ rest).to_quaternion()
    bone.rotation_quaternion=q
    deps=bpy.context.evaluated_depsgraph_get()
    ev=obj.evaluated_get(deps)
    mesh=ev.to_mesh(preserve_all_data_layers=False,depsgraph=deps)
    zs=sorted((ev.matrix_world @ mesh.vertices[i].co).z for i in ids)
    ev.to_mesh_clear()
    minimum=zs[0]
    count=sum(v-minimum<=.05 for v in zs)
    q10=zs[int(len(zs)*.1)]-minimum
    choices.append((count,q10,x,y,z,minimum))
for row in sorted(choices,key=lambda r:(-r[0],r[1],sum(abs(a) for a in r[2:5])))[:25]:
    print('BEST',row)
