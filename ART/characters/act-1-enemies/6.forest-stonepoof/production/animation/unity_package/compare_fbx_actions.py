"""Compare the takes of two ForestStonehoof.fbx exports curve by curve.

Run: blender -b --python compare_fbx_actions.py -- OLD.fbx NEW.fbx OUT.json
Imports each FBX into an empty scene and records every action's frame range
and every fcurve's keys; reports added/removed takes and max key difference.
"""
import bpy, sys, json
from pathlib import Path


def fcurves(act):
    out = []
    for lay in act.layers:
        for st in lay.strips:
            for bag in st.channelbags:
                out.extend(bag.fcurves)
    return out


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    res = {}
    for a in bpy.data.actions:
        curves = {}
        for fc in fcurves(a):
            curves[(fc.data_path, fc.array_index)] = [tuple(k.co) for k in fc.keyframe_points]
        res[a.name] = {'range': list(a.frame_range), 'curves': curves}
    return res


argv = sys.argv[sys.argv.index('--') + 1:]
old, new = load(argv[0]), load(argv[1])
report = {'old_takes': sorted(old), 'new_takes': sorted(new),
          'added': sorted(set(new) - set(old)), 'removed': sorted(set(old) - set(new)), 'common': {}}
for name in sorted(set(old) & set(new)):
    o, n = old[name], new[name]
    diff, frame_diff, key_count = 0.0, 0.0, 0
    missing = [str(k) for k in set(o['curves']) ^ set(n['curves'])]
    for k in set(o['curves']) & set(n['curves']):
        a, b = o['curves'][k], n['curves'][k]
        if len(a) != len(b):
            missing.append(str(k) + ' key count %d vs %d' % (len(a), len(b)))
            continue
        key_count += len(a)
        for (fa, va), (fb, vb) in zip(a, b):
            frame_diff = max(frame_diff, abs(fa - fb))
            diff = max(diff, abs(va - vb))
    report['common'][name] = {'range_old': o['range'], 'range_new': n['range'], 'curves': len(n['curves']),
                              'keys': key_count, 'max_frame_diff': frame_diff, 'max_value_diff': diff,
                              'curve_mismatches': missing,
                              'identical': not missing and diff == 0 and frame_diff == 0 and o['range'] == n['range']}
report['added_detail'] = {a: {'range': new[a]['range'], 'curves': len(new[a]['curves'])} for a in report['added']}
Path(argv[2]).write_text(json.dumps(report, indent=2))
print('COMPARE', json.dumps({k: v for k, v in report.items() if k != 'common'}), flush=True)
for k, v in report['common'].items():
    print('TAKE', k, v['identical'], v['range_new'], v['curves'], v['keys'], v['max_value_diff'], v['curve_mismatches'][:3], flush=True)
