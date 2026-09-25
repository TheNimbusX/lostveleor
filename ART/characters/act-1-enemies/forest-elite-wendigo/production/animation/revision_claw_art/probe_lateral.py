"""Probe the strike elbow's IK pole and possible lateral contact targets."""
import itertools
import math
import bpy
from mathutils import Vector

scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
obj=next(o for o in scene.objects if o.type=='MESH' and o.find_armature()==rig)
hand_group=obj.vertex_groups['R_hand'].index
hand_ids=[v.index for v in obj.data.vertices if any(g.group==hand_group and g.weight>=.25 for g in v.groups)]
scene.frame_set(18)
base_target=(rig.matrix_world @ rig.pose.bones['CTRL_R_hand'].head).copy()
base_pole=(rig.matrix_world @ rig.pose.bones['CTRL_R_elbow'].head).copy()
print('BASE',tuple(base_target),tuple(base_pole))

def move(name,xyz):
    bone=rig.pose.bones[name]
    rest=bone.bone.matrix_local.to_3x3()
    bone.location=rest.inverted() @ Vector(xyz)

for px,py in itertools.product((-.7,-.4,0,.4,.7),(-.5,0,.5)):
    scene.frame_set(18)
    move('CTRL_R_elbow',(px,py,0))
    bpy.context.view_layer.update()
    shoulder=rig.matrix_world @ rig.pose.bones['R_arm_upper'].head
    elbow=rig.matrix_world @ rig.pose.bones['R_arm_lower'].head
    wrist=rig.matrix_world @ rig.pose.bones['R_hand'].head
    target=rig.matrix_world @ rig.pose.bones['CTRL_R_hand'].head
    print('POLE',px,py,'ELBOW',tuple(round(v,3) for v in elbow),
          'SCREEN',round(elbow.x-elbow.y,3),'WRIST',round((wrist-target).length,4))

for x,y in ((-.20,.47),(-.15,.46),(-.12,.46),(.10,.35),(.12,.21),(.15,.2)):
    scene.frame_set(18)
    target=Vector((x,y,base_target.z))
    rest=rig.matrix_world @ rig.pose.bones['CTRL_R_hand'].bone.head_local
    move('CTRL_R_hand',target-rest)
    bpy.context.view_layer.update()
    shoulder=rig.matrix_world @ rig.pose.bones['R_arm_upper'].head
    elbow=rig.matrix_world @ rig.pose.bones['R_arm_lower'].head
    wrist=rig.matrix_world @ rig.pose.bones['R_hand'].head
    deps=bpy.context.evaluated_depsgraph_get()
    ev=obj.evaluated_get(deps)
    m=ev.to_mesh(preserve_all_data_layers=False,depsgraph=deps)
    min_z=min((ev.matrix_world @ m.vertices[i].co).z for i in hand_ids)
    ev.to_mesh_clear()
    print('TARGET',x,y,'REACH',round((target-shoulder).length,3),
          'ERROR',round((wrist-target).length,3),'ELBOW',tuple(round(v,3) for v in elbow),
          'WRIST',tuple(round(v,3) for v in wrist),'HAND_MIN_Z',round(min_z,3))
