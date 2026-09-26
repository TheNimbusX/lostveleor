"""Probe support-paw orientation at f18 for a wider ground patch."""
import math
import bpy
from mathutils import Quaternion, Vector

scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
obj=next(o for o in scene.objects if o.type=='MESH' and o.find_armature()==rig)
gs={g.name:g.index for g in obj.vertex_groups}
ids=[v.index for v in obj.data.vertices if any(g.group in {gs['R_foot'],gs['R_toe']} and g.weight>=.25 for g in v.groups)]

def turn(bone,axis,deg):
    rest=bone.bone.matrix_local.to_3x3()
    q=Quaternion(Vector(axis),math.radians(deg))
    bone.rotation_quaternion=bone.rotation_quaternion @ (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()

def stats():
    deps=bpy.context.evaluated_depsgraph_get()
    ev=obj.evaluated_get(deps)
    mesh=ev.to_mesh(preserve_all_data_layers=False,depsgraph=deps)
    zs=sorted((ev.matrix_world @ mesh.vertices[i].co).z for i in ids)
    ev.to_mesh_clear()
    minimum=zs[0]
    return round(minimum,3),round(zs[int(len(zs)*.1)]-minimum,3),round(zs[int(len(zs)*.5)]-minimum,3),sum(z-minimum<=.05 for z in zs)

for bone_name in ('R_foot','R_toe'):
    for axis_name,axis in (('X',(1,0,0)),('Y',(0,1,0)),('Z',(0,0,1))):
        for deg in (-45,-30,-20,-10,0,10,20,30,45):
            scene.frame_set(18)
            turn(rig.pose.bones[bone_name],axis,deg)
            bpy.context.view_layer.update()
            print('FOOT',bone_name,axis_name,deg,stats())
