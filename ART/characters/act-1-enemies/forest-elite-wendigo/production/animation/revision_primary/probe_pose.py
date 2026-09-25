import bpy
from mathutils import Vector

rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o.find_armature() == rig)
deps = bpy.context.evaluated_depsgraph_get()
for action_name, frames in (('AN_ForestWendigo_Claw',(0,12,15,18)),('AN_ForestWendigo_Leap',(0,18,23,28,33,35))):
    rig.animation_data.action = bpy.data.actions[action_name]
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        evaluated = mesh.evaluated_get(deps)
        temp = evaluated.to_mesh()
        coords = [evaluated.matrix_world @ v.co for v in temp.vertices]
        feet = [(v.x,v.y,v.z) for v in coords if v.z < .5]
        print('POSE',action_name,frame,'minz',round(min(v.z for v in coords),3),
              'lowest',sorted(feet,key=lambda v:v[2])[:5])
        if action_name.endswith('Leap'):
            for label,pred in (('left_claws',lambda v:v.x<-.45 and v.y>.65),('right_claws',lambda v:v.x>.35 and v.y>.25)):
                region=[v.z for v in coords if pred(v)]
                print(label,round(min(region),3))
        for name in ('pelvis','L_foot','R_foot','L_hand','R_hand','CTRL_L_foot','CTRL_R_foot','CTRL_L_hand'):
            p=rig.pose.bones[name]
            loc=rig.matrix_world @ p.head
            print(name,tuple(round(x,3) for x in loc))
        evaluated.to_mesh_clear()
