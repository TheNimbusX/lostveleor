from __future__ import annotations
import json
from pathlib import Path
import bpy

PROJECT = Path(r'C:\Users\d.grab\Desktop\the-game')
PATHS = [
    PROJECT / 'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_AnchorAttack.fbx',
    PROJECT / 'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_Idle.fbx',
    PROJECT / 'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_Run.fbx',
    *sorted((PROJECT / 'artifacts/pelag-animation-build/exports').glob('Pelag_MX_*.fbx')),
]
OUT = PROJECT / 'artifacts/pelag-animation-build/mesh-detail-audit.json'

def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

def audit(path):
    clear()
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
    action = arm.animation_data.action
    start, end = map(round, action.frame_range)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    depsgraph = bpy.context.evaluated_depsgraph_get()
    best = None
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for obj in meshes:
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            try:
                for vertex in mesh.vertices:
                    point = evaluated.matrix_world @ vertex.co
                    if best is None or point.z < best['xyz'][2]:
                        best = {
                            'frame': frame,
                            'object': obj.name,
                            'vertex': int(vertex.index),
                            'xyz': [float(point.x), float(point.y), float(point.z)],
                            'material_count': len(obj.material_slots),
                        }
            finally:
                evaluated.to_mesh_clear()
    return {
        'file': str(path),
        'armature': arm.name,
        'armature_world': [list(row) for row in arm.matrix_world],
        'meshes': [
            {
                'name': obj.name,
                'type': obj.type,
                'vertices': len(obj.data.vertices),
                'triangles': sum(len(poly.vertices) - 2 for poly in obj.data.polygons),
            }
            for obj in meshes
        ],
        'min': best,
    }

payload = [audit(path) for path in PATHS if path.is_file()]
OUT.write_text(json.dumps(payload, indent=2), encoding='utf-8')
for item in payload:
    print(Path(item['file']).name, item['min'])
    print('meshes:', [(mesh['name'], mesh['vertices'], mesh['triangles']) for mesh in item['meshes']])
