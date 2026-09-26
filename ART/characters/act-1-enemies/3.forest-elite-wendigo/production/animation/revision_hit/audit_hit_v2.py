"""Measure paw contacts and per-frame motion on the isolated hit action."""

import json
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in scene.objects if o.type == 'MESH' and o.find_armature() == rig)
rig.animation_data.action = bpy.data.actions['AN_ForestWendigo_Hit']
indices = {}
for side in ('L', 'R'):
    groups = {mesh.vertex_groups[f'{side}_foot'].index, mesh.vertex_groups[f'{side}_toe'].index}
    indices[side] = [v.index for v in mesh.data.vertices
                     if sum(g.weight for g in v.groups if g.group in groups) >= 0.6]
rows = []
for frame in range(15):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    dep = bpy.context.evaluated_depsgraph_get()
    obj = mesh.evaluated_get(dep)
    evalmesh = obj.to_mesh()
    row = {'frame': frame}
    for side in ('L', 'R'):
        row[f'{side}_paw_min_z'] = round(min((obj.matrix_world @ evalmesh.vertices[i].co).z
                                        for i in indices[side]), 4)
    row['root_xy'] = [round(rig.pose.bones['root'].location.x, 5),
                      round(rig.pose.bones['root'].location.y, 5)]
    rows.append(row)
    obj.to_mesh_clear()
report = {'paw_contacts': rows, 'triangles': sum(len(p.vertices)-2 for p in mesh.data.polygons)}
(HERE/'hit_v2_audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
