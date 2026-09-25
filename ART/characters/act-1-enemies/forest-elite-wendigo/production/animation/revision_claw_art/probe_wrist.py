"""Inspect contact-claw clearance for alternative wrist rotations."""
import math
import bpy
from mathutils import Quaternion, Vector

scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
obj=next(o for o in scene.objects if o.type=='MESH' and o.find_armature()==rig)
group=obj.vertex_groups['R_hand'].index
ids=[v.index for v in obj.data.vertices if any(g.group==group and g.weight>=.25 for g in v.groups)]
for frame in (17,18,19):
    for degrees in (-90,-65,-45,-25,0,25,45,65,90):
        scene.frame_set(frame)
        b=rig.pose.bones['R_hand']
        rest=b.bone.matrix_local.to_3x3()
        world=Quaternion(Vector((1,0,0)),math.radians(degrees))
        b.rotation_quaternion=b.rotation_quaternion @ (rest.inverted() @ world.to_matrix() @ rest).to_quaternion()
        deps=bpy.context.evaluated_depsgraph_get()
        ev=obj.evaluated_get(deps)
        mesh=ev.to_mesh(preserve_all_data_layers=False,depsgraph=deps)
        pts=[ev.matrix_world @ mesh.vertices[i].co for i in ids]
        wrist=rig.matrix_world @ b.head
        tip=max(pts,key=lambda p:(p-wrist).length)
        print('WRIST',frame,degrees,'MINZ',round(min(p.z for p in pts),3),
              'TIP',tuple(round(v,3) for v in tip))
        ev.to_mesh_clear()
