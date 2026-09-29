"""Взмах клыками Камнекопыта (Stonehoof_Tusk) на редактируемом IK-риге.

Запуск (из production/animation):
  blender -b --factory-startup --python tusk/build_tusk.py -- [build|all]
  build -> Stonehoof_Tusk_r01.blend (редактируемый мастер) + build.json
  all   -> build, затем bake_tusk.bake() -> Stonehoof_Tusk_Baked_r01.blend
Принятые клипы только читаются; их sha256 проверяется в конце.
Помощники рига — turn/turn_rig.py (только чтение).
"""
import bpy, sys, json, hashlib, math, importlib
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
sys.path.insert(0, str(HERE))
sys.path.insert(1, str(ANIM / 'turn'))
import tusk_params as P
import turn_rig as R
for m in (P, R):
    importlib.reload(m)

MODE = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1 else 'all'
SOURCE = ANIM / 'windup_start' / 'Stonehoof_WindupStart_r01.blend'
ACCEPTED = [ANIM / 'windup_start' / 'Stonehoof_WindupStart_r01.blend',
            ANIM / 'windup_start' / 'Stonehoof_WindupStart_Baked_r01.blend',
            ANIM / 'charge_loop' / 'Stonehoof_ChargeLoop_Baked_r01.blend',
            ANIM / 'brake' / 'Stonehoof_Brake_Baked_r01.blend',
            ANIM / 'collision' / 'Stonehoof_Collision_Baked_r01.blend',
            ANIM / 'death_r02' / 'Stonehoof_Death_Baked_r02.blend',
            ANIM / 'turn' / 'Stonehoof_Turn_Baked_r01.blend']
ACCEPTED = [p for p in ACCEPTED if p.exists()]
HASHES = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in ACCEPTED}
OUT_MASTER = HERE / 'Stonehoof_Tusk_r01.blend'


def reach_deficit(ctx, tag):
    """На сколько метров опустить тело, чтобы цель IK этой ноги была в досягаемости с запасом."""
    arm, leg = ctx['arm'], ctx['legs'][tag]
    hip = arm.matrix_world @ arm.pose.bones['MCH_Upper_' + tag].head
    tgt = leg['foot'].matrix_world.translation
    reach = leg['length'] - P.REACH_MARGIN
    dh = (hip.xy - tgt.xy).length
    v = hip.z - tgt.z
    if dh >= reach:
        return v
    return max(0.0, v - math.sqrt(reach * reach - dh * dh))


def samples():
    n = int(round(P.FRAMES / P.SAMPLE_STEP))
    return [i * P.SAMPLE_STEP for i in range(n + 1)]


def author(ctx, scene):
    R.clear_animation(ctx)
    arm, legs = ctx['arm'], ctx['legs']
    # Копыта стоят весь клип: ключи покоя в начале и в конце.
    for tag in legs:
        for f in (0, P.FRAMES):
            R.hoof_key(ctx, tag, f, 0.0, 0.0, 0.0)
        R.set_interp(legs[tag]['foot'])
    for f, key in P.KEYS:
        R.spine_pose(ctx, f, key['angles'], key['offset'])
    R.set_interp(arm)
    for b in arm.pose.bones:
        for c in b.constraints:
            c.influence = 1
            c.keyframe_insert('influence', frame=0)
            c.keyframe_insert('influence', frame=P.FRAMES)
    # Присяд: ноги в покое почти прямые, выпад вперёд тянет задние. Где цель
    # IK вне досягаемости, тело опускается ровно настолько (сглажено).
    fs = samples()
    need = []
    for f in fs:
        R.goto(scene, f)
        need.append(max(reach_deficit(ctx, t) for t in legs))
    crouch = [0.0] * len(fs)
    if max(need) > 0:
        n = len(fs)
        wide = [max(need[max(0, i - 6):i + 7]) for i in range(n)]
        smooth = [sum(wide[max(0, i - 6):i + 7]) / len(wide[max(0, i - 6):i + 7]) for i in range(n)]
        crouch = [max(a, b) for a, b in zip(smooth, need)]
        crouch[0] = crouch[-1] = 0.0
        rest_q = ctx['rest']['body'].to_quaternion()
        base = []
        for f in fs:
            R.goto(scene, f)
            base.append(rest_q @ arm.pose.bones['body'].location)
        pb = arm.pose.bones['body']
        for f, off, c in zip(fs, base, crouch):
            pb.location = rest_q.inverted() @ Vector((off.x, off.y, off.z - c))
            pb.keyframe_insert('location', frame=f, group='body')
        R.set_interp(arm, paths={'pose.bones["body"].location'})
    names = {}
    for o in [arm] + [l['foot'] for l in legs.values()]:
        suffix = 'Rig' if o == arm else o.name
        act = o.animation_data.action
        act.name = 'AN_Stonehoof_Tusk_' + suffix
        act.use_fake_user = True
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, P.FRAMES
        names[o.name] = act.name
    return {'actions': names, 'crouch_m': {'max': max(crouch)}, 'raw_reach_deficit_max_m': max(need)}


def tusk_tips(ctx):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ctx['mesh'].evaluated_get(dg)
    me = ev.to_mesh()
    mw = ctx['mesh'].matrix_world
    out = {k: (mw @ me.vertices[i].co).copy() for k, i in P.TUSK_TIPS.items()}
    ground = min((mw @ v.co).z for v in me.vertices)
    ev.to_mesh_clear()
    return out, ground


def measure(ctx, scene, stance):
    """IK, проскальзывание копыт, земля, стыки со стойкой, путь клыков."""
    arm, legs = ctx['arm'], ctx['legs']
    ik = 0.0
    slip = {t: 0.0 for t in legs}
    anchor = {}
    for i in range(P.FRAMES * 8 + 1):
        f = i / 8
        R.goto(scene, f)
        for t, leg in legs.items():
            ank = arm.matrix_world @ arm.pose.bones['leg_' + t + '_bot2'].head
            tgt = leg['foot'].matrix_world.translation
            ik = max(ik, (ank - tgt).length)
            anchor.setdefault(t, ank.copy())
            slip[t] = max(slip[t], (ank - anchor[t]).length)
    tips, ground, yaw = [], 100.0, []
    rest_dir = ctx['rest']['head0'].to_quaternion() @ Vector((0, 1, 0))
    rest_yaw = math.degrees(math.atan2(rest_dir.x, -rest_dir.y))
    for f in range(P.FRAMES + 1):
        R.goto(scene, f)
        t, g = tusk_tips(ctx)
        tips.append(t)
        ground = min(ground, g)
        # Рыскание морды (кость head0 в плоскости XY) от покоя, + — влево.
        d = arm.pose.bones['head0'].tail - arm.pose.bones['head0'].head
        yaw.append(math.degrees(math.atan2(d.x, -d.y)) - rest_yaw)
    # Кадр, где морда проходит линию «вперёд» (0°) слева направо кабана, с подкадром.
    crossing = None
    for f in range(1, len(yaw)):
        if yaw[f - 1] < 0 <= yaw[f]:
            crossing = f - 1 + (-yaw[f - 1]) / (yaw[f] - yaw[f - 1])
            break
    lead = [p['left'] for p in tips]
    speed = [0.0] + [(lead[f] - lead[f - 1]).length * P.FPS for f in range(1, len(lead))]
    fastest = max(range(len(speed)), key=lambda f: speed[f])
    apex = max(range(len(lead)), key=lambda f: lead[f].z)
    lowest = min(range(len(lead)), key=lambda f: lead[f].z)
    joins = {}
    for f in (0, P.FRAMES):
        R.goto(scene, f)
        joins[f] = max((arm.pose.bones[n].matrix.translation - m.translation).length for n, m in stance.items())
    R.goto(scene, 0)
    return {
        'max_ik_target_error_m': ik,
        'max_ankle_world_slip_m': slip,
        'minimum_skin_z_m': ground,
        'stance_join_m': {'f0': joins[0], 'f%d' % P.FRAMES: joins[P.FRAMES]},
        'leading_tusk': {
            'vertex': P.TUSK_TIPS['left'],
            'positions': [[round(c, 4) for c in p] for p in lead],
            'speed_m_s': [round(s, 3) for s in speed],
            'fastest_frame': fastest, 'apex_frame': apex, 'lowest_frame': lowest,
            'contact_frame': P.CONTACT,
            'contact_speed_share': speed[P.CONTACT] / max(speed),
            'contact_rise_m': lead[P.CONTACT].z - lead[lowest].z,
        },
        'snout_yaw_deg': [round(y, 2) for y in yaw],
        'snout_crosses_forward_frame': crossing,
    }


def build():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    R.goto(scene, 0)
    arm = bpy.data.objects['ARM_ForestStonehoof']
    stance = {b.name: b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
    ctx = R.setup()
    for act in list(bpy.data.actions):
        act.use_fake_user = False
    info = author(ctx, scene)
    info.update(measure(ctx, scene, stance))
    for old in list(bpy.data.actions):
        if not old.use_fake_user:
            bpy.data.actions.remove(old)
    scene.frame_start, scene.frame_end, scene.render.fps = 0, P.FRAMES, P.FPS
    for mk in list(scene.timeline_markers):
        scene.timeline_markers.remove(mk)
    for f, label in [(0, 'Стойка'), (4, 'Откат'), (11, 'Сжатие'), (P.CONTACT, 'КОНТАКТ (тик удара)'),
                     (17, 'Вершина'), (P.FRAMES, 'Стойка / Idle')]:
        scene.timeline_markers.new(label, frame=f)
    scene['tusk_actions'] = json.dumps(info['actions'])
    arm['clip_status'] = 'Stonehoof_Tusk r01: кандидат, ждёт просмотра владельцем.'
    arm['runtime_action'] = 'AN_Stonehoof_Tusk (baked): 26 кадров, контакт на 14, фаза = (tick - StartTick) / 26.'
    R.goto(scene, 0)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT_MASTER))
    report = {'stage': 'tusk_swipe_r01', 'owner_approved': False, 'fps': P.FPS, 'frames': P.FRAMES,
              'frame_range': [0, P.FRAMES], 'contact_frame': P.CONTACT,
              'sim_contract': 'Simulation.Stonehoof.cs: StonehoofTuskWindupTicks=14, StonehoofTuskRecoveryTicks=12',
              'reference': 'review/mobs-v2-concepts-2026-09-29/refs/stonehoof-tusk-swipe.mp4 (1.6-3.3 s)',
              'source': str(SOURCE), 'source_sha256': HASHES,
              'keys': [[f, k] for f, k in P.KEYS], **info}
    (HERE / 'build.json').write_text(json.dumps(report, indent=2, default=str), encoding='utf8')
    lt = info['leading_tusk']
    print('TUSK_AUTHORED', json.dumps({
        'ik': info['max_ik_target_error_m'], 'slip': max(info['max_ankle_world_slip_m'].values()),
        'ground': info['minimum_skin_z_m'], 'joins': info['stance_join_m'], 'crouch': info['crouch_m'],
        'fastest': lt['fastest_frame'], 'apex': lt['apex_frame'], 'lowest': lt['lowest_frame'],
        'contact_share': lt['contact_speed_share'], 'speed': lt['speed_m_s'],
        'crossing': info['snout_crosses_forward_frame'], 'yaw': info['snout_yaw_deg']}), flush=True)
    return report


if __name__ == '__main__':
    build()
    if MODE == 'all':
        import bake_tusk
        importlib.reload(bake_tusk)
        bake_tusk.bake()
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest() == h for p, h in HASHES.items()), 'принятые клипы изменились'
    print('TUSK_BUILT', MODE, flush=True)
