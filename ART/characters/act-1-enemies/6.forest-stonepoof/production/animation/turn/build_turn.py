"""Editable 90 deg turn-in-place cycles (TurnLeft / TurnRight) for the Stonehoof.

Run: blender -b --python animation/turn/build_turn.py -- [build|all]
  build -> Stonehoof_Turn_r01.blend (editable IK master, both directions)
  all   -> build, then bake_turn.py -> Stonehoof_Turn_Baked_r01.blend
Accepted clips are only read; their sha256 is asserted unchanged at the end.
"""
import bpy, sys, json, hashlib
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import importlib
import turn_params as P
import turn_rig as R
import turn_author as A
for m in (P, R, A):
    importlib.reload(m)

MODE = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1 else 'all'
ANIM = HERE.parent
SOURCE = ANIM / 'windup_start' / 'Stonehoof_WindupStart_r01.blend'
ACCEPTED = [ANIM / 'windup_start' / 'Stonehoof_WindupStart_r01.blend',
            ANIM / 'windup_start' / 'Stonehoof_WindupStart_Baked_r01.blend',
            ANIM / 'charge_loop' / 'Stonehoof_ChargeLoop_r01.blend',
            ANIM / 'charge_loop' / 'Stonehoof_ChargeLoop_Baked_r01.blend',
            ANIM / 'brake' / 'Stonehoof_Brake_r01.blend',
            ANIM / 'brake' / 'Stonehoof_Brake_Baked_r01.blend',
            ANIM / 'collision' / 'Stonehoof_Collision_Baked_r01.blend',
            ANIM / 'death_r02' / 'Stonehoof_Death_Baked_r02.blend']
ACCEPTED = [p for p in ACCEPTED if p.exists()]
hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in ACCEPTED}
OUT_MASTER = HERE / 'Stonehoof_Turn_r01.blend'


def stance_snapshot():
    arm = bpy.data.objects['ARM_ForestStonehoof']
    return {b.name: b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}


def assign(actions):
    for obj_name, act_name in actions.items():
        o = bpy.data.objects[obj_name]
        o.animation_data_create()
        act = bpy.data.actions[act_name]
        o.animation_data.action = act
        if not o.animation_data.action_slot and len(act.slots):
            o.animation_data.action_slot = act.slots[0]


def build():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    R.goto(scene, 0)
    stance = stance_snapshot()
    ctx = R.setup()
    for act in list(bpy.data.actions):
        act.use_fake_user = False
    report = {'stage': 'turn_in_place_r01', 'owner_approved': False, 'fps': P.FPS,
              'frames': P.FRAMES, 'frame_range': [0, P.FRAMES], 'source': str(SOURCE),
              'source_sha256': hashes, 'directions': {}}
    for name, s in P.DIRECTIONS.items():
        info = A.author(ctx, scene, name, s)
        info['yaw_sign_blender_z'] = s
        info.update(A.measure(ctx, scene, s))
        R.goto(scene, 0)
        info['stance_join_f0'] = {
            'max_joint_position_m': max((ctx['arm'].pose.bones[n].matrix.translation - m.translation).length
                                        for n, m in stance.items()),
            'max_matrix_error': max(abs(ctx['arm'].pose.bones[n].matrix[i][j] - m[i][j])
                                    for n, m in stance.items() for i in range(4) for j in range(4)),
            'worst_bones': sorted(((round((ctx['arm'].pose.bones[n].matrix.translation - m.translation).length, 4), n)
                                   for n, m in stance.items()), reverse=True)[:6]}
        seam = []
        for f in (0, P.FRAMES):
            R.goto(scene, f)
            seam.append({n: ctx['arm'].pose.bones[n].matrix.copy() for n in stance})
        info['loop_seam_max_position_m'] = max((seam[0][n].translation - seam[1][n].translation).length for n in stance)
        report['directions'][name] = info
        print('TURN_AUTHORED', name, json.dumps({k: info[k] for k in ('crouch_m', 'max_ik_target_error_m', 'loop_seam_max_position_m')}))
    for old in list(bpy.data.actions):
        if not old.use_fake_user:
            bpy.data.actions.remove(old)
    R.clear_animation(ctx)
    assign(report['directions']['TurnLeft']['actions'])
    scene['turn_actions'] = json.dumps({k: v['actions'] for k, v in report['directions'].items()})
    scene.frame_start, scene.frame_end, scene.render.fps = 0, P.FRAMES, P.FPS
    for mk in list(scene.timeline_markers):
        scene.timeline_markers.remove(mk)
    for f, label in [(0, 'Stance / loop seam'), (1, 'Inside fore lifts'), (4, 'Outside fore cross-step'),
                     (7.5, 'Half turn 45 deg'), (15, '90 deg, loop')]:
        scene.timeline_markers.new(label, frame=int(f))
    arm = ctx['arm']
    arm['clip_status'] = 'Turn-in-place r01 candidate; owner approval pending.'
    arm['runtime_action'] = ('AN_Stonehoof_TurnLeft / AN_Stonehoof_TurnRight (baked). 15 frames = 90 deg; '
                             'phase = accumulated yaw / 90. Switch direction: scene["turn_actions"].')
    R.goto(scene, 0)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT_MASTER))
    (HERE / 'build.json').write_text(json.dumps(report, indent=2, default=str))
    pose_keys = {'roles': P.ROLES, 'directions': P.DIRECTIONS, 'c_clamp_deg': P.C_CLAMP,
                 'stance_offsets_deg': {r: P.stance_offset(r) for r in P.ROLES}}
    (HERE / 'pose_keys.json').write_text(json.dumps(pose_keys, indent=2))
    return report


if __name__ == '__main__':
    rep = build()
    if MODE == 'all':
        import bake_turn
        importlib.reload(bake_turn)
        bake_turn.bake()
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest() == h for p, h in hashes.items())
    print('TURN_BUILT', MODE)
