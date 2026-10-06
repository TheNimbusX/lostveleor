"""Exporter check against the clip UNITY built (DESIGN 3.5 without opening Unity).

python check_vs_anim.py <clip.grip.json exported with --dump-rig> <Pelag_v5/<clip>.anim> [--body 1.82]
FK of the .anim (m_RotationCurves = local quaternions, m_PositionCurves = Hips) on the v6 rest local positions
(raw FBX Lcl Translation, X mirror, globalScale baked; Armature at the origin, identity, scale 1), x WoleScale.
Compares, per key frame, every exported body bone and the grip socket (TransformPoint of grip_socket offset).
Pass: bones <= 5 mm, socket <= 5 mm (DESIGN 3.5 asks <= 5 mm / 1 deg against the Unity dump).
"""
import json, math, re, sys, argparse

ap = argparse.ArgumentParser()
ap.add_argument('grip'); ap.add_argument('anim')
ap.add_argument('--body', type=float, default=1.82)
ap.add_argument('--local-from', default='localPos', choices=['localPos', 'localPosFromBlender'])
ap.add_argument('--out', default='')
a = ap.parse_args()
grip = json.load(open(a.grip, encoding='utf8'))
rig = grip.get('unityRig')
if not rig:
    sys.exit('grip json has no unityRig - export with --dump-rig')


def qmul(p, q):
    px, py, pz, pw = p; qx, qy, qz, qw = q
    return (pw * qx + px * qw + py * qz - pz * qy, pw * qy - px * qz + py * qw + pz * qx,
            pw * qz + px * qy - py * qx + pz * qw, pw * qw - px * qx - py * qy - pz * qz)


def qrot(q, v):
    x, y, z, w = q
    tx, ty, tz = 2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])
    return (v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx))


def parse_anim(path):
    section, curves, cur = None, {'rot': {}, 'pos': {}}, []
    kv = re.compile(r'(\w+):\s*(-?[0-9.eE+-]+)')
    for line in open(path, encoding='utf8'):
        s = line.rstrip('\n')
        if s.startswith('  m_') and s.endswith(':') or s.startswith('  m_') and ': ' in s and not s.startswith('   '):
            name = s.strip().split(':')[0]
            section = {'m_RotationCurves': 'rot', 'm_PositionCurves': 'pos'}.get(name)
            cur = []
            continue
        if section is None:
            continue
        t = s.strip()
        if t.startswith('time:'):
            cur.append([float(t.split(':')[1]), None])
        elif t.startswith('value:') and cur and cur[-1][1] is None:
            d = dict(kv.findall(t))
            cur[-1][1] = tuple(float(d[c]) for c in ('xyzw' if section == 'rot' else 'xyz'))
        elif t.startswith('path:'):
            curves[section][t.split(':', 1)[1].strip()] = cur
            cur = []
    return curves


curves = parse_anim(a.anim)
by_name = {}
for path, keys in curves['rot'].items():
    by_name[path.split('/')[-1]] = keys
hip_path = [p for p in curves['pos'] if p.endswith('mixamorig:Hips')][0]
hips_keys = curves['pos'][hip_path]
names = grip['boneNames']
sub = grip['sub']
order = ['mixamorig:Hips'] + [n for n in rig]          # rig is written in pre-order


def at_time(keys, t):
    """Curve value at t: exact key if present, else linear between neighbours (linear tangents)."""
    for i, (kt, v) in enumerate(keys):
        if abs(kt - t) < 1e-4:
            return v
        if kt > t:
            pt, pv = keys[i - 1]
            u = (t - pt) / (kt - pt)
            w = [pv[c] + (v[c] - pv[c]) * u for c in range(len(v))]
            if len(w) == 4:
                n = math.sqrt(sum(x * x for x in w)); w = [x / n for x in w]
            return tuple(w)
    return keys[-1][1]


def fk(frame):
    pos, rot = {}, {}
    t = frame / 30.0
    for n in order:
        q = at_time(by_name[n], t)
        if n == 'mixamorig:Hips':
            pos[n], rot[n] = at_time(hips_keys, t), q
        else:
            par = rig[n]['parent']
            lp = rig[n][a.local_from]
            off = qrot(rot[par], lp)
            pos[n] = tuple(pos[par][c] + off[c] for c in range(3))
            rot[n] = qmul(rot[par], q)
    return pos, rot


socket = grip['source']['socket']
hand = socket['bone']
# grip_socket.json v2 gives metres in the hand axes; FK here is in body units (x a.body = metres)
offset = [c / a.body for c in socket['position']] if 'position' in socket else socket['offset']
worst_angle = 0.0
rows, worst_bone, worst_socket = [], 0.0, 0.0
for f in range(grip['frames'] + 1):
    pos, rot = fk(f)
    s = grip['samples'][f * sub]
    errs = {}
    for i, n in enumerate(names):
        u = [pos['mixamorig:' + n][c] * a.body for c in range(3)]
        errs[n] = math.dist(u, s['bones'][i])
    o = qrot(rot[hand], offset)
    g = [(pos[hand][c] + o[c]) * a.body for c in range(3)]
    se = math.dist(g, s['grip'])
    # rotation of the hand: angle between Unity world rotation and the exported gripQ
    q1, q2 = rot[hand], s['gripQ']
    ang = math.degrees(2 * math.acos(min(1.0, abs(sum(q1[c] * q2[c] for c in range(4))))))
    worst_bone = max(worst_bone, max(errs.values())); worst_socket = max(worst_socket, se); worst_angle = max(worst_angle, ang)
    rows.append({'frame': f, 'maxBone': round(max(errs.values()), 5), 'worst': max(errs, key=errs.get),
                 'socket': round(se, 5), 'handAngleDeg': round(ang, 3)})
    print('frame %2d bones max %.4f m (%s) socket %.4f m hand %.2f deg' % (f, max(errs.values()), max(errs, key=errs.get), se, ang))
ok = worst_bone <= .005 and worst_socket <= .005 and worst_angle <= 1.0   # DESIGN 3.5: 5 mm and 1 deg
print('ANIM_CHECK %s clip=%s worstBone=%.4f worstSocket=%.4f worstHandAngle=%.2fdeg' % ('PASS' if ok else 'FAIL', grip['clip'], worst_bone, worst_socket, worst_angle))
if a.out:
    json.dump({'pass': ok, 'worstBone': worst_bone, 'worstSocket': worst_socket, 'worstHandAngleDeg': worst_angle, 'frames': rows},
              open(a.out, 'w', encoding='utf8'), indent=1)
