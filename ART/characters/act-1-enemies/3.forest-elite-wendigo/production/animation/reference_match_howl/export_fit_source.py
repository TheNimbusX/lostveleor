"""Howl: snapshot of the production rig for the reference fit (read-only).

Run headless on unity_package/ForestWendigo_Production.blend. Writes
fit_source.json (rest bones, rest vertices, the exported 4-weight skin, triangles)
and stance_samples.json (evaluated Idle / Claw start poses in the fit's
parameterisation: pelvis world offset + bone-local rotation vectors).
The production file is never saved.
"""
import bpy, json
from pathlib import Path
from mathutils import Matrix

HERE = Path(__file__).resolve().parent
s = bpy.context.scene
r = s.objects['ARM_ForestWendigo']
mesh = s.objects['SM_ForestWendigo_LOD0']
deform = [b for b in r.data.bones if b.use_deform]
names = [b.name for b in deform]
bones = [{'name': b.name, 'parent': b.parent.name if b.parent else None,
          'matrix': [list(row) for row in b.matrix_local]} for b in deform]
groups = {g.index: g.name for g in mesh.vertex_groups}
verts, weights = [], []
for v in mesh.data.vertices:
    verts.append(list(v.co))
    weights.append([[groups[g.group], g.weight] for g in v.groups if g.weight > 0 and groups[g.group] in names])
mesh.data.calc_loop_triangles()
tris = [list(t.vertices) for t in mesh.data.loop_triangles]
(HERE / 'fit_source.json').write_text(json.dumps({'source': bpy.data.filepath, 'bones': bones, 'vertices': verts,
                                                  'weights': weights, 'triangles': tris}))


def pose_vector():
    rest = {b.name: b.matrix_local for b in deform}
    pose = {pb.name: pb.matrix for pb in r.pose.bones}
    x = []
    for b in deform[1:]:
        p = b.parent.name
        basis = (rest[p].inverted() @ rest[b.name]).inverted() @ (pose[p].inverted() @ pose[b.name])
        if b.name == 'pelvis':
            t = rest['pelvis'].to_3x3() @ basis.to_translation()
            x = list(t) + x
        q = basis.to_quaternion()
        axis, angle = q.to_axis_angle()
        x.extend(list(axis * angle))
    return x


out = {}
for name, frames in [('Wendigo_Idle', range(0, 61, 4)), ('Wendigo_Claw', [0, 96]), ('Wendigo_Leap', [0, 96]), ('Wendigo_Walk', [0])]:
    r.animation_data.action = bpy.data.actions[name]
    for f in frames:
        s.frame_set(f)
        out[f'{name}@{f}'] = pose_vector()
(HERE / 'stance_samples.json').write_text(json.dumps({'names': names, 'poses': out}, indent=1))
print('EXPORTED', len(verts), 'verts', len(tris), 'tris', names)
