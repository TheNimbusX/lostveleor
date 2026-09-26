"""Re-import the exported FBX and compare the Wendigo_Howl take with the solved joints.

blender -b --factory-startup --python check_fbx_howl.py -- <ForestWendigo.fbx>
(The importer connects 'pelvis' to 'root', which would hide its translation; it is
disconnected here so the check sees exactly what Unity plays.)"""
import bpy, sys, json
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
src = sys.argv[sys.argv.index('--') + 1]
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for b in arm.data.edit_bones: b.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
act = next(a for a in bpy.data.actions if a.name.endswith('Wendigo_Howl'))
arm.animation_data.action = act
J = json.loads((HERE / 'expected_joints.json').read_text())
names = [b['name'] for b in json.loads((HERE / 'fit_source.json').read_text())['bones']]
err = 0.0; worst = None
f0 = int(act.frame_range[0])
for i in range(0, len(J)):
    f = f0 + i / 4
    bpy.context.scene.frame_set(int(f), subframe=f % 1)
    for j, n in enumerate(names):
        e = (arm.pose.bones[n].head - Vector(J[i][j])).length
        if e > err: err, worst = e, (i / 4, n)
res = {'fbx': src, 'take': act.name, 'frame_range': list(act.frame_range), 'max_joint_error_m': err, 'worst': worst}
(HERE / 'fbx_howl_check.json').write_text(json.dumps(res, indent=1))
print('FBXCHECK', json.dumps(res))
