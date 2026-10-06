"""Read-only probe: how Blender sees the Pelag FBX files (units, armature, actions, raw FBX nodes).
blender -b --factory-startup -P probe_fbx.py -- <fbx> [<fbx> ...]"""
import bpy, sys, json
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def raw_nodes(path, wanted):
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(path)
    out = {'version': version, 'global': {}, 'models': {}}
    for el in root.elems:
        if el.id == b'GlobalSettings':
            for sub in el.elems:
                if sub.id == b'Properties70':
                    for p in sub.elems:
                        name = p.props[0].decode('utf8', 'replace')
                        if name in ('UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign',
                                    'UnitScaleFactor', 'OriginalUnitScaleFactor', 'TimeMode', 'CustomFrameRate'):
                            out['global'][name] = [x if not isinstance(x, bytes) else x.decode() for x in p.props[4:]]
        if el.id == b'Objects':
            for m in el.elems:
                if m.id != b'Model':
                    continue
                name = m.props[1].split(b'\x00')[0].decode('utf8', 'replace')
                if not any(w in name for w in wanted):
                    continue
                rec = {'type': m.props[2].decode()}
                for sub in m.elems:
                    if sub.id == b'Properties70':
                        for p in sub.elems:
                            pn = p.props[0].decode()
                            if pn in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling', 'PreRotation', 'PostRotation',
                                      'RotationOrder', 'GeometricTranslation', 'GeometricRotation', 'GeometricScaling'):
                                rec[pn] = list(p.props[4:])
                out['models'][name] = rec
    return out


for path in args:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    rec = {'file': path, 'objects': [], 'actions': [(a.name, list(a.frame_range)) for a in bpy.data.actions],
           'scene_fps': bpy.context.scene.render.fps, 'frame_start': bpy.context.scene.frame_start,
           'frame_end': bpy.context.scene.frame_end}
    for o in bpy.data.objects:
        r = {'name': o.name, 'type': o.type, 'loc': [round(x, 5) for x in o.matrix_world.translation],
             'scale': [round(x, 5) for x in o.matrix_world.to_scale()],
             'rot': [round(x, 3) for x in o.matrix_world.to_euler()], 'parent': o.parent.name if o.parent else None}
        if o.type == 'ARMATURE':
            r['bones'] = len(o.data.bones)
            r['anim'] = o.animation_data.action.name if o.animation_data and o.animation_data.action else None
            for bn in ('mixamorig:Hips', 'mixamorig:Head', 'mixamorig:HeadTop_End', 'mixamorig:LeftHand', 'mixamorig:LeftHandMiddle1',
                       'mixamorig:LeftArm', 'mixamorig:RightArm', 'mixamorig:LeftToeBase', 'mixamorig:LeftFoot', 'mixamorig:Spine2'):
                pb = o.pose.bones.get(bn)
                if pb:
                    m = o.matrix_world @ pb.matrix
                    r[bn] = {'head': [round(x, 4) for x in m.translation], 'len': round(pb.bone.length * o.matrix_world.to_scale()[0], 4),
                             'y_axis': [round(x, 3) for x in m.to_3x3().col[1].normalized()]}
            zs = [(o.matrix_world @ pb.matrix).translation.z for pb in o.pose.bones]
            r['z_range'] = [round(min(zs), 4), round(max(zs), 4)]
        if o.type == 'MESH':
            ws = [o.matrix_world @ v.co for v in o.data.vertices]
            if ws:
                r['bbox_z'] = [round(min(w.z for w in ws), 4), round(max(w.z for w in ws), 4)]
        rec['objects'].append(r)
    try:
        rec['raw'] = raw_nodes(path, ['Armature', 'Hips', 'LeftHand', 'Spine2'])
    except Exception as e:  # parser layout differs between versions; the probe is diagnostic only
        rec['raw_error'] = repr(e)
    print('PROBE_JSON=' + json.dumps(rec, default=str))
