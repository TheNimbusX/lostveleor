import bpy

rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for action, frame in (("Walk", 4), ("Walk", 7), ("Death", 19), ("Death", 45)):
    rig.animation_data.action = bpy.data.actions[f"AN_ForestWendigo_{action}"]
    bpy.context.scene.frame_set(frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eo = mesh.evaluated_get(depsgraph)
    em = eo.to_mesh()
    lowest = sorted(((eo.matrix_world @ v.co).z, v.index) for v in em.vertices)[:12]
    print(action, frame, [(round(z, 4), i, [(mesh.vertex_groups[g.group].name, round(g.weight, 2)) for g in mesh.data.vertices[i].groups]) for z, i in lowest])
    eo.to_mesh_clear()
