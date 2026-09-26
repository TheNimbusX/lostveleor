"""Find a readable wrist fan that retains the low contact clearance."""
import math
import bpy
from mathutils import Quaternion, Vector

scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
obj=next(o for o in scene.objects if o.type=='MESH' and o.find_armature()==rig)
group=obj.vertex_groups['R_hand'].index
ids=[v.index for v in obj.data.vertices if any(g.group==group and g.weight>=.25 for g in v.groups)]
for frame in (15,17,18,19,22):
    for deg in (-75,-60,-45,-30,-15,0,15,30,45,60):
        scene.frame_set(frame)
        b=rig.pose.bones['R_hand']
        rest=b.bone.matrix_local.to_3x3()
        turn=Quaternion(Vector((0,0,1)), math.radians(deg))
        b.rotation_quaternion=b.rotation_quaternion @ (rest.inverted() @ turn.to_matrix() @ rest).to_quaternion()
        deps=bpy.context.evaluated_depsgraph_get()
        ev=obj.evaluated_get(deps)
        mesh=ev.to_mesh(preserve_all_data_layers=False,depsgraph=deps)
        pts=[ev.matrix_world @ mesh.vertices[i].co for i in ids]
        wrist=rig.matrix_world @ b.head
        tip=max(pts,key=lambda p:(p-wrist).length)
        min_z=min(p.z for p in pts)
        print('YAW',frame,deg,'TIP',tuple(round(v,3) for v in tip),
              'TIP_SCREEN',round(tip.x-tip.y,3),'MINZ',round(min_z,3))
        ev.to_mesh_clear()
