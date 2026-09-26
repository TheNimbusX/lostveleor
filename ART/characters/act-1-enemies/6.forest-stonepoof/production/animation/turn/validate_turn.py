"""Validate Stonehoof_Turn_Baked_r01.blend (TurnLeft / TurnRight).

Run: blender -b --python validate_turn.py
Same approach as brake/validate_brake.py and charge_loop/validate_loop.py:
sole vertices = hoof-weighted verts within 8 mm of the sole. The baked clip has
no root yaw, so the game yaw (+-90 deg * f/15 about the origin) is applied back
on the armature object and the clip is played for two cycles (0..30) so the
contact spanning the loop seam is measured as one plant.
"""
import bpy, json, math, hashlib, sys
from pathlib import Path
from mathutils import Vector, Matrix
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import turn_validate_src as src
FRAMES = 15
TAGS = ('front_left', 'front_right', 'hind_left', 'hind_right')
build = json.loads((HERE / 'build.json').read_text())
times = [i / 8 for i in range(FRAMES * 8 + 1)]
source_checks, source_samples = src.check_source(times)

bpy.ops.wm.open_mainfile(filepath=str(HERE / 'Stonehoof_Turn_Baked_r01.blend'))
scene = bpy.context.scene
arm = bpy.data.objects['ARM_ForestStonehoof']
mesh = bpy.data.objects['SM_ForestStonehoof_LOD0']
names = [b.name for b in arm.data.bones if b.use_deform]
soles = {}
for t in TAGS:
    idx = mesh.vertex_groups['leg_' + t + '_bot2'].index
    ids = [v.index for v in mesh.data.vertices if any(g.group == idx and g.weight > .99 for g in v.groups)]
    z = min(mesh.data.vertices[i].co.z for i in ids)
    soles[t] = [i for i in ids if mesh.data.vertices[i].co.z < z + .008]


def planted_intervals(events):
    """Planted spans over two cycles [0, 30]; lift/plant pairs from build.json.
    turn_author.py records each event at the first 1/4-frame key whose state
    flipped, so a 'lift' at f means the hoof is airborne at f: last planted key
    is f - 0.25. A 'plant' at f is the first planted key."""
    ev = sorted([(f + k * FRAMES, e) for k in (0, 1) for f, e in events])
    spans, start = [], 0.0
    for f, e in ev:
        if e == 'lift':
            spans.append((start, f - .25))
        else:
            start = f
    spans.append((start, 2 * FRAMES))
    return spans


report = {'clips': {}}
for direction, info in build['directions'].items():
    act = bpy.data.actions['AN_Stonehoof_' + direction]
    arm.animation_data_create()
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    sign = info['yaw_sign_blender_z']
    contacts = {t: sorted(set(float(f) for f, _ in c['events'])) for t, c in info['contacts'].items()}
    extra = sorted(set(f + k * FRAMES for c in contacts.values() for f in c for k in (0, 1)))
    ts = sorted(set([i / 8 for i in range(2 * FRAMES * 8 + 1)] + extra))
    rows, length_err, minz, seam, base_err = [], (0, None), (9, None), {}, (0, None)
    for f in ts:
        local = f % FRAMES if f < 2 * FRAMES else FRAMES
        if f >= FRAMES and local == 0:
            local = FRAMES if f == FRAMES else 0.0
        src.goto(scene, local)
        arm.matrix_world = Matrix.Rotation(math.radians(sign * 90 * f / FRAMES), 4, 'Z')
        bpy.context.view_layer.update()
        ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        m = ev.to_mesh()
        z = min((ev.matrix_world @ v.co).z for v in m.vertices)
        if z < minz[0]:
            minz = (z, f)
        row = {'frame': f, 'legs': {}}
        for t in TAGS:
            pts = [ev.matrix_world @ m.vertices[i].co for i in soles[t]]
            row['legs'][t] = {'sole': sum(pts, Vector()) / len(pts), 'min_z': min(p.z for p in pts)}
        ev.to_mesh_clear()
        if f <= FRAMES:
            for b in arm.pose.bones:
                if not b.bone.use_deform:
                    continue
                e = abs((b.tail - b.head).length / b.bone.length - 1)
                if e > length_err[0]:
                    length_err = (e, f, b.name)
                ref = source_samples[direction].get(f)
                if ref:
                    h, tl = ref[b.name]
                    d = max((b.head - h).length, (b.tail - tl).length)
                    if d > base_err[0]:
                        base_err = (d, f, b.name)
            if f in (0.0, float(FRAMES)):
                seam[f] = {b.name: b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
        rows.append(row)
    slip, sole_z, spans_out, profile = {}, {}, {}, {}
    for t in TAGS:
        spans = planted_intervals(info['contacts'][t]['events'])
        spans_out[t] = spans
        worst, zr = (0, None), [9, -9]
        for a, b in spans:
            part = [r for r in rows if a - 1e-6 <= r['frame'] <= b + 1e-6]
            o = part[0]['legs'][t]['sole']
            for r in part:
                d = (r['legs'][t]['sole'] - o).length
                if d > worst[0]:
                    worst = (d, [a, b], r['frame'])
                zr = [min(zr[0], r['legs'][t]['min_z']), max(zr[1], r['legs'][t]['min_z'])]
        slip[t], sole_z[t] = worst, zr
        a, b = worst[1]
        part = [r for r in rows if a - 1e-6 <= r['frame'] <= b + 1e-6]
        profile[t] = [(r['frame'], round((r['legs'][t]['sole'] - part[0]['legs'][t]['sole']).length * 1000, 2), round(r['legs'][t]['min_z'] * 1000, 1)) for r in part if (r['frame'] * 4) % 1 == 0]
    s0, s1 = seam[0.0], seam[float(FRAMES)]
    report['clips'][direction] = {
        'action': act.name, 'frame_range': list(act.frame_range), 'yaw_sign_blender_z': sign,
        'planted_spans_two_cycles': spans_out,
        'max_planted_sole_slip_world_m': slip,
        'planted_sole_min_z_range_m': sole_z,
        'minimum_mesh_z_m': minz,
        'bone_length_error': length_err,
        'loop_seam_position_m': max((s0[n].translation - s1[n].translation).length for n in s0),
        'loop_seam_matrix_error': max(abs(s0[n][i][j] - s1[n][i][j]) for n in s0 for i in range(4) for j in range(4)),
        'baked_vs_source_joint_m': base_err,
        'source': source_checks[direction],
        'worst_span_profile_mm': profile,
    }
sha = {p: hashlib.sha256(Path(p).read_bytes()).hexdigest() == h for p, h in build['source_sha256'].items()}
c = report['clips'].values()
report['pass'] = {
    'hoof_slip_lt_2mm': all(v[0] < .002 for x in c for v in x['max_planted_sole_slip_world_m'].values()),
    'bone_length_lt_1e-4': all(x['bone_length_error'][0] < 1e-4 for x in c),
    'loop_seam_lt_1e-5m': all(x['loop_seam_position_m'] < 1e-5 for x in c),
    'constraint_influences_explicit': all(x['source']['constraint_influences_explicit'] for x in c),
    'ik_target_lt_1mm': all(x['source']['max_ik_target_error_m'][0] < .001 for x in c),
    'ik_reach_slack_ge_0': all(v[0] >= 0 for x in c for v in x['source']['min_ik_reach_slack_m'].values()),
    'baked_vs_source_lt_1mm': all(x['baked_vs_source_joint_m'][0] < .001 for x in c),
    'accepted_sources_unchanged': all(sha.values()),
}
report['all_pass'] = all(report['pass'].values())
report['owner_approved'] = False
out = json.dumps(report, indent=2, default=lambda v: list(v))
(HERE / 'validation.json').write_text(out)
print('VALIDATION', json.dumps(report['pass']), 'ALL', report['all_pass'], flush=True)
for d, x in report['clips'].items():
    print('CLIP', d, json.dumps({k: v for k, v in x.items() if k not in ('planted_spans_two_cycles', 'source', 'worst_span_profile_mm')}, default=lambda v: list(v)), flush=True)
