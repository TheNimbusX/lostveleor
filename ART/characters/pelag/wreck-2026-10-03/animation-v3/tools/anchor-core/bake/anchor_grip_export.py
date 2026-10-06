"""Step A of the anchor bake (DESIGN 3.1): grip track of a Pelag clip in Unity root axes, no Unity needed.

blender -b --factory-startup -P anchor_grip_export.py -- --clip <clip.fbx> --bind <bind.fbx> [--rig <Pelag_v6_MixamoRig.fbx>]
        [--hip-limit .95] [--hand Left|Right] [--socket grip_socket.json] [--sub 8] [--out <dir>]

What it does (read-only on the repo):
1. imports the v6 rig, the Blender bind reference and the clip FBX into one empty scene;
2. repeats RazlomPelagAuthoredClips.Build frame by frame (body-basis alignment, world rotation delta transfer,
   segment-direction correction toward the Spine/Neck/first child, hip offset x hipScale, hip envelope check)
   in Blender world space - every formula there is a conjugation, so it is covariant under the FBX->Unity mirror;
3. samples the built clip the way Unity plays it: per-bone LOCAL quaternions, linear tangents (component lerp
   + normalise, EnsureQuaternionContinuity), hips position lerp, FK on the v6 rest offsets - `sub` samples per frame;
4. writes grip (socket point + rotation of the holding hand), support hand, body bones for capsules, Spine2 pose,
   all in the Unity ROOT space of the game body (x right, y up, z forward, metres at WoleScale 1.82).
Mapping Blender world -> Unity root: p_U = k * (-x, z, -y), bone frame R_U = M R_B S (S = diag(-1,1,1), the
importer's X mirror of every local frame), k = 100 * globalScale(meta) * bodyScale; checked against the capture log.
"""
import bpy, sys, os, json, math, argparse, hashlib, re
from mathutils import Matrix, Vector, Quaternion

REPO = os.environ.get('RAZLOM_REPO') or os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
CHAR = os.path.join(REPO, 'razlom', 'Assets', 'Resources', 'Characters')
NUL = bytes([0])
M = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
S = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, 1)))
BODY_BONES = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'HeadTop_End',
              'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1', 'LeftHandIndex1',
              'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1',
              'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End',
              'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End']


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--clip', required=True, help='clip FBX path, or a name under Pelag_v5/Mixamo')
    ap.add_argument('--bind', required=True, help='Blender bind reference FBX path or name (Build bindReference)')
    ap.add_argument('--rig', default=os.path.join(CHAR, 'Pelag_v6', 'Runtime', 'Pelag_v6_MixamoRig.fbx'))
    ap.add_argument('--hip-limit', type=float, default=.4, help='Build maxHipOffsetBodyLengths for this clip')
    ap.add_argument('--hand', default='Left', choices=['Left', 'Right'])
    ap.add_argument('--socket', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), 'grip_socket.json'))
    ap.add_argument('--socket-key', default='grip')
    ap.add_argument('--sub', type=int, default=8)
    ap.add_argument('--body-scale', type=float, default=1.82, help='ArenaView.WoleScale')
    ap.add_argument('--bone-unit', default='unity', choices=['unity', 'node'],
                    help='metres of one Unity bone-local unit: unity = WoleScale (lossyScale), node = FBX node metre')
    ap.add_argument('--dump-rig', action='store_true', help='add Unity rest local positions (for check_vs_anim.py)')
    ap.add_argument('--hand-child', default='', help='finger order Unity uses for the hand aim, e.g. Index,Middle')
    ap.add_argument('--ground-from-anim', default='', help='Unity .anim whose constant hip shift (post-Build grounding) to apply')
    ap.add_argument('--tag', default='', help='suffix of the output name')
    ap.add_argument('--out', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out'))
    a = ap.parse_args(argv)
    a.sort_by_name = meta_flag(a.rig, 'sortHierarchyByName', 1) == 1
    for key in ('clip', 'bind'):
        v = getattr(a, key)
        if not v.lower().endswith('.fbx'):
            setattr(a, key, os.path.join(CHAR, 'Pelag_v5', 'Mixamo', v + '.fbx'))
    return a


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def meta_flag(fbx, key, default):
    try:
        m = re.search(key + r':\s*([0-9]+)', open(fbx + '.meta', encoding='utf8').read())
        return int(m.group(1)) if m else default
    except OSError:
        return default


def meta_global_scale(fbx):
    try:
        text = open(fbx + '.meta', encoding='utf8').read()
        m = re.search(r'globalScale:\s*([0-9.eE+-]+)', text)
        return float(m.group(1)) if m else 1.0
    except OSError:
        return 1.0


def import_armature(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arms = [o for o in new if o.type == 'ARMATURE']
    if len(arms) != 1:
        raise RuntimeError('expected one armature in ' + path)
    return arms[0]


def snapshot(arm):
    """World position and normalised rotation of every mixamorig bone at the current frame."""
    bpy.context.view_layer.update()
    out = {}
    for pb in arm.pose.bones:
        if not pb.name.startswith('mixamorig:'):
            continue
        m = arm.matrix_world @ pb.matrix
        r = m.to_3x3()
        r.normalize()
        out[pb.name] = (m.translation.copy(), r)
    return out


def body_basis(b):
    up = (b['mixamorig:Head'][0] - b['mixamorig:Hips'][0]).normalized()
    right = (b['mixamorig:LeftArm'][0] - b['mixamorig:RightArm'][0]).normalized()
    fwd = right.cross(up).normalized()
    x = up.cross(fwd).normalized()          # Unity LookRotation(forward, up)
    y = fwd.cross(x)
    return Matrix((x, y, fwd)).transposed()


def preorder(arm):
    order, parent, children = [], {}, {}
    def walk(bone):
        order.append(bone.name)
        kids = [c.name for c in bone.children if c.name.startswith('mixamorig:')]
        children[bone.name] = kids
        for c in bone.children:
            if c.name.startswith('mixamorig:'):
                parent[c.name] = bone.name
                walk(c)
    walk(arm.data.bones['mixamorig:Hips'])
    return order, parent, children


def child_rank(a, name):
    """Unity's child order decides which finger the hand is aimed at; --hand-child picks the first one."""
    for i, finger in enumerate(a.hand_child.split(',')):
        if finger and finger in name:
            return i
    return 99


def from_to(a, b):
    return a.rotation_difference(b).to_matrix() if a.length > 1e-12 and b.length > 1e-12 else Matrix.Identity(3)


def build_frames(a):
    scene = bpy.context.scene
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    rig = import_armature(a.rig)
    rig_start = int(rig.animation_data.action.frame_range[0]) if rig.animation_data and rig.animation_data.action else 1
    scene.frame_set(rig_start)
    target = snapshot(rig)                      # Unity prefab pose = first key of the rig take
    bone_unit = rig.matrix_world.to_scale().x   # one bone-local unit (node metre) in Blender world units
    bind_arm = import_armature(a.bind)
    scene.frame_set(1)
    bind = snapshot(bind_arm)
    clip_arm = import_armature(a.clip)
    act = clip_arm.animation_data.action
    start, end = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    order, parent, children = preorder(rig)
    missing = [n for n in order if n not in bind]
    if missing:
        raise RuntimeError('bind lacks bones: %s' % missing[:5])
    A = body_basis(target) @ body_basis(bind).transposed()
    hip, head = 'mixamorig:Hips', 'mixamorig:Head'
    hip_scale = (target[hip][0] - target[head][0]).length / max(1e-9, (bind[hip][0] - bind[head][0]).length)
    hip_rest = target[hip][0].copy()
    limit = a.hip_limit * (bind[hip][0] - bind[head][0]).length * hip_scale
    local = {n: target[parent[n]][1].transposed() @ (target[n][0] - target[parent[n]][0]) for n in order if n in parent}
    frames, worst, worst_bone, hip_peak = [], 0.0, '', 0.0
    for f in range(0, end - start + 1):
        scene.frame_set(start + f)
        src = snapshot(clip_arm)
        hips_pos = hip_rest + A @ (src[hip][0] - bind[hip][0]) * hip_scale
        hip_peak = max(hip_peak, (hips_pos - hip_rest).length / max(1e-9, limit / a.hip_limit))
        if (hips_pos - hip_rest).length > limit:
            raise RuntimeError('Pelag hip offset exceeds authored crouch envelope (Build would throw) at frame %d' % f)
        rot, pos = {}, {hip: hips_pos}
        for n in order:
            if n in parent:
                pos[n] = pos[parent[n]] + rot[parent[n]] @ local[n]
            r = A @ src[n][1] @ bind[n][1].transposed() @ A.transposed() @ target[n][1]
            kids = children[n]
            kids = sorted(kids) if a.sort_by_name else kids         # Unity child order (sortHierarchyByName)
            kids = sorted(kids, key=lambda c: child_rank(a, c))
            child = next((c for c in kids if 'Spine' in c or 'Neck' in c), kids[0] if kids else None)
            if child is not None:
                wanted = A @ (src[child][0] - src[n][0])
                r = from_to(r @ local[child], wanted) @ r
            rot[n] = r
        for n in order:                          # Build's diagnostic: segment direction error vs source
            if n in parent:
                child_pos = pos[parent[n]] + rot[parent[n]] @ local[n]
                exp = A @ (src[n][0] - src[parent[n]][0])
                err = math.degrees((child_pos - pos[parent[n]]).angle(exp, 0.0))
                if err > worst:
                    worst, worst_bone = err, n
        frames.append((pos, rot))
    return dict(frames=frames, order=order, parent=parent, local=local, bone_unit=bone_unit,
                rig_start=rig_start, clip_range=[start, end], worst=worst, worst_bone=worst_bone,
                hip_scale=hip_scale, hip_peak=hip_peak, target_rest=target)


def unity_sampler(built):
    """Local quaternions per key exactly as Build records them (+EnsureQuaternionContinuity)."""
    order, parent = built['order'], built['parent']
    keys = []
    for pos, rot in built['frames']:
        q = {}
        for n in order:
            r = rot[n] if n not in parent else rot[parent[n]].transposed() @ rot[n]
            q[n] = r.to_quaternion()
        keys.append((pos['mixamorig:Hips'].copy(), q))
    for i in range(1, len(keys)):
        for n in order:
            if keys[i][1][n].dot(keys[i - 1][1][n]) < 0:
                keys[i][1][n].negate()
    return keys


def fk(built, hips, qs):
    order, parent, local = built['order'], built['parent'], built['local']
    pos, rot = {}, {}
    for n in order:
        r = qs[n].to_matrix()
        if n in parent:
            pos[n] = pos[parent[n]] + rot[parent[n]] @ local[n]
            rot[n] = rot[parent[n]] @ r
        else:
            pos[n], rot[n] = hips, r
    return pos, rot


def sample(built, keys, frame):
    """Unity curve evaluation of the built clip: linear tangents per component, then normalise."""
    i = min(int(math.floor(frame)), len(keys) - 2)
    u = frame - i
    h0, q0 = keys[i]
    h1, q1 = keys[i + 1]
    qs = {}
    for n in q0:
        a, b = q0[n], q1[n]
        q = Quaternion((a.w + (b.w - a.w) * u, a.x + (b.x - a.x) * u, a.y + (b.y - a.y) * u, a.z + (b.z - a.z) * u))
        q.normalize()
        qs[n] = q
    return fk(built, h0.lerp(h1, u), qs)


def hips_y_from_anim(path):
    """Hips localPosition.y keys of a Unity .anim (m_PositionCurves), model units."""
    out, inside = [], False
    for line in open(path, encoding='utf8'):
        if line.startswith('  m_PositionCurves'):
            inside = True
            continue
        if inside and line.startswith('  m_') :
            break
        if inside and 'value:' in line:
            m = re.search(r'y:\s*(-?[0-9.eE+-]+)', line)
            out.append(float(m.group(1)))
    return out


def rel(path):
    return os.path.relpath(path, REPO).replace(os.sep, '/')


def to_unity(k, p):
    v = M @ p
    return [round(k * v.x, 6), round(k * v.y, 6), round(k * v.z, 6)]


def rot_unity(r):
    q = (M @ r @ S).to_quaternion()
    return [round(q.x, 7), round(q.y, 7), round(q.z, 7), round(q.w, 7)]


def socket_point(built, pos, rot, bone, cfg, k):
    """grip_socket.json v2: 'position' in METRES in the hand's Unity axes (the game divides by lossyScale);
    v1: 'offset' in Unity bone-local units. Unity local -> FBX/Blender local is the X mirror S."""
    if 'position' in cfg:
        o = S @ Vector(cfg['position']) * (1.0 / k)          # metres -> Blender units
    else:
        o = S @ Vector(cfg['offset']) * built['socket_unit']
    return pos[bone] + rot[bone] @ o


def raw_lcl_translation(path):
    """FBX Lcl Translation of every mixamorig node (node units) - what Unity turns into localPosition."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(path)
    out = {}
    for el in root.elems:
        if el.id != b'Objects':
            continue
        for m in el.elems:
            if m.id != b'Model':
                continue
            name = m.props[1].split(NUL)[0].decode('utf8', 'replace')
            if not name.startswith('mixamorig:'):
                continue
            for sub in m.elems:
                if sub.id == b'Properties70':
                    for prop in sub.elems:
                        if prop.props[0] == b'Lcl Translation':
                            out[name] = [float(x) for x in prop.props[4:7]]
            out.setdefault(name, [0.0, 0.0, 0.0])
    return out


def unity_rig(built, gs, rig_path):
    """Unity rest localPosition per bone (S mirror, globalScale baked) for the .anim FK check."""
    raw = raw_lcl_translation(rig_path)
    rest, parent, unit = built['target_rest'], built['parent'], built['bone_unit']
    out = {}
    for n in built['order']:
        if n not in parent:
            continue
        p0, r0 = rest[parent[n]]
        blender_local = r0.transposed() @ (rest[n][0] - p0) / unit
        out[n] = {'parent': parent[n],
                  'localPos': [-raw[n][0] * gs, raw[n][1] * gs, raw[n][2] * gs] if n in raw else None,
                  'localPosFromBlender': [-blender_local.x * gs, blender_local.y * gs, blender_local.z * gs]}
    return out


def main():
    a = parse_args()
    socket_all = json.load(open(a.socket, encoding='utf8'))
    # v2 (one contract with the rig): the grip is the root object; support/abordage are nested.
    grip_cfg = socket_all if (a.socket_key == 'grip' and 'position' in socket_all) else socket_all[a.socket_key]
    grip_cfg = {key: grip_cfg[key] for key in ('bone', 'position', 'offset') if key in grip_cfg}
    hand_bone = 'mixamorig:%sHand' % a.hand
    if grip_cfg['bone'] != hand_bone:
        grip_cfg = dict(grip_cfg, bone=hand_bone)
    support_bone = 'mixamorig:%sHand' % ('Right' if a.hand == 'Left' else 'Left')
    support_cfg = socket_all.get('support', {'offset': [0, .045, .012]})
    built = build_frames(a)
    gs = meta_global_scale(a.rig)
    k = (1.0 / built['bone_unit']) * gs * a.body_scale  # Blender units -> metres (hips .anim value checks it)
    # Unity bakes globalScale into translations and keeps bone scale 1, so one Unity bone-local unit is
    # 1/globalScale node units (lossyScale = WoleScale, 1.82 m); 'node' keeps the older reading (0.99 m).
    built['socket_unit'] = built['bone_unit'] / gs if a.bone_unit == 'unity' else built['bone_unit']
    keys = unity_sampler(built)
    n_frames = len(keys) - 1
    rest = built['target_rest']
    height = max(p.z for p, _ in rest.values()) * k
    samples = []
    for i in range(n_frames * a.sub + 1):
        fr = i / a.sub
        pos, rot = sample(built, keys, fr)
        g = socket_point(built, pos, rot, hand_bone, grip_cfg, k)
        sp = socket_point(built, pos, rot, support_bone, support_cfg, k)
        samples.append({
            't': round(fr / 30.0, 6), 'frame': round(fr, 4),
            'grip': to_unity(k, g), 'gripQ': rot_unity(rot[hand_bone]),
            'support': to_unity(k, sp), 'supportQ': rot_unity(rot[support_bone]),
            'spine2Q': rot_unity(rot['mixamorig:Spine2']),
            'bones': [to_unity(k, pos['mixamorig:' + b]) for b in BODY_BONES],
        })
    ground_shift = 0.0
    if a.ground_from_anim:
        # Post-Build grounding (e.g. RazlomPelagV5AnimatorBuilder.GroundAbordage2Clips): one constant hip shift per
        # clip family. Read it from the Unity clip instead of re-measuring the support frames.
        anim_y = hips_y_from_anim(a.ground_from_anim)
        mine = [smp['bones'][0][1] / a.body_scale for smp in samples[::a.sub]]
        diffs = sorted(u - m for u, m in zip(anim_y, mine))
        ground_shift = diffs[len(diffs) // 2] * a.body_scale
        for smp in samples:
            for key in ('grip', 'support'):
                smp[key][1] = round(smp[key][1] + ground_shift, 6)
            for b in smp['bones']:
                b[1] = round(b[1] + ground_shift, 6)
    name = os.path.splitext(os.path.basename(a.clip))[0]
    out = {
        'version': 1, 'clip': name, 'hand': a.hand, 'fps': 30, 'sub': a.sub, 'frames': n_frames,
        'source': {'fbx': rel(a.clip), 'sha256': sha256(a.clip),
                   'bind': rel(a.bind), 'bindSha256': sha256(a.bind),
                   'rig': rel(a.rig), 'rigSha256': sha256(a.rig),
                   'socket': grip_cfg, 'supportSocket': support_cfg},
        'units': {'axes': 'Unity root space: x right, y up, z forward (body faces +z), metres',
                  'blenderToRoot': k, 'globalScale': gs, 'bodyScale': a.body_scale,
                  'boneUnitMetres': round(built['socket_unit'] * k, 6), 'boneUnitMode': a.bone_unit,
                  'restHeight': round(height, 4)},
        'build': {'groundShiftMetres': round(ground_shift, 5), 'hipLimit': a.hip_limit, 'hipPeakBodyLengths': round(built['hip_peak'], 4),
                  'hipScale': built['hip_scale'], 'worstSegmentErrorDeg': round(built['worst'], 4),
                  'worstSegmentBone': built['worst_bone'], 'clipFrames': built['clip_range'],
                  'rigFirstKey': built['rig_start']},
        'boneNames': BODY_BONES, 'samples': samples,
        'unityRig': unity_rig(built, gs, a.rig) if a.dump_rig else None,
    }
    os.makedirs(a.out, exist_ok=True)
    path = os.path.join(a.out, name + ('' if a.hand == 'Left' else '_' + a.hand) + a.tag + '.grip.json')
    with open(path, 'w', encoding='utf8') as f:
        json.dump(out, f, separators=(',', ':'))
    print('GRIP_EXPORT ok %s frames=%d samples=%d k=%.5f boneUnit=%.4fm height=%.3fm worstSeg=%.3fdeg@%s hipPeak=%.3f'
          % (path, n_frames, len(samples), k, built['socket_unit'] * k, height, built['worst'], built['worst_bone'],
             built['hip_peak']))


if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        import traceback
        traceback.print_exc()
        print('GRIP_EXPORT failed: %r' % e)
        sys.exit(1)
