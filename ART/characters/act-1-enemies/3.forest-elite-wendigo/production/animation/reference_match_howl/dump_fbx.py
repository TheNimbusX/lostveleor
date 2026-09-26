"""Dump every take of a Wendigo FBX as sampled bone matrices (headless, factory scene).

blender -b --factory-startup --python dump_fbx.py -- <in.fbx> <out.json>
Used to prove the re-exported package leaves the six accepted clips unchanged.
"""
import bpy, sys, json
args = sys.argv[sys.argv.index('--') + 1:]
src, dst = args
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
out = {'actions': {}, 'mesh': {'verts': len(mesh.data.vertices), 'polys': len(mesh.data.polygons)}}
arm.animation_data_create()
for a in bpy.data.actions:
    arm.animation_data.action = a
    f0, f1 = map(int, a.frame_range)
    frames = {}
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        frames[f] = {b.name: [round(v, 6) for row in b.matrix for v in row] for b in arm.pose.bones}
    # raw curves too: the importer marks 'pelvis' connected, so pose matrices hide its
    # translation; the keyed values are what Unity reads
    curves = {}
    for layer in a.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    curves[f'{fc.data_path}[{fc.array_index}]'] = [[round(k.co.x, 4), round(k.co.y, 6)] for k in fc.keyframe_points]
    out['actions'][a.name] = {'range': [f0, f1], 'frames': frames, 'curves': curves}
json.dump(out, open(dst, 'w'))
print('DUMPED', list(out['actions']))
