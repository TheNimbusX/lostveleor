"""Prove the re-exported package keeps the accepted takes: python compare_fbx_takes.py <old.fbx> <new.fbx>
Dumps both through dump_fbx.py (Blender re-import) and compares every animation curve key.
Writes fbx_package_check.json."""
import json, sys, subprocess, hashlib, tempfile
from pathlib import Path
import numpy as np
HERE = Path(__file__).resolve().parent
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
old, new = sys.argv[1:3]
tmp = Path(tempfile.mkdtemp())
dumps = []
for i, f in enumerate((old, new)):
    out = tmp / f'fbx_{i}.json'
    subprocess.run([BLENDER, '-b', '--factory-startup', '--python', str(HERE / 'dump_fbx.py'), '--', f, str(out)], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    dumps.append(json.loads(out.read_text()))
o, n = dumps
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
res = {'method': 'Both FBX files re-imported in Blender 5.2 (factory settings); every animation curve key of each take compared.',
       'previous_fbx_sha256': sha(old), 'new_fbx_sha256': sha(new), 'takes': {}}
for a, v in o['actions'].items():
    w = n['actions'][a]; mx = 0.0
    for c, keys in v['curves'].items():
        A = np.array(keys); B = np.array(w['curves'][c]); mx = max(mx, float(np.abs(A - B).max()) if A.shape == B.shape else 1e9)
    res['takes'][a.split('|')[-1]] = {'curves': len(v['curves']), 'max_key_difference': mx, 'unchanged': mx == 0.0}
res['new_takes'] = [a.split('|')[-1] for a in n['actions'] if a not in o['actions']]
res['mesh'] = {'old': o['mesh'], 'new': n['mesh']}
(HERE / 'fbx_package_check.json').write_text(json.dumps(res, indent=1))
print(json.dumps({k: v['unchanged'] for k, v in res['takes'].items()}), res['new_takes'])
