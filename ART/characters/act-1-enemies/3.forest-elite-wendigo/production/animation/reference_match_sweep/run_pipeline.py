"""Круг когтей (Sweep) — весь конвейер по порядку. python run_pipeline.py [--from ШАГ] [--only ШАГ]

Реф: review/mobs-v2-concepts-2026-09-29/refs/wendigo-sweep-360.mp4 (Higgsfield e5ee213d,
владелец одобрил 29.09), лист 6 кадров/с — wendigo-sweep-360_sheet.jpg рядом.
Кадр клипа = тик Sim (30 в секунду): замах 21, удар на кадре 21, восстановление 18, всего 39.

  0 extract   ролик -> reference/ref_###.png (97 кадров, 24 fps) и лист 6 fps
  1 samples   build_sweep.py: каналы sweep_keys.py -> final_samples.json, expected_joints.json,
              claw_paths.json (пути когтей для VFX), validation_numpy.json
  2 scenes    apply_sweep.py (Blender): Sweep_r01.blend (каждый кадр) + Sweep_Baked_r01.blend
  3 review    render_review.py (Blender): пересечения частей тела, сверка с решением, кадры
              game/front/top -> production/review/animation_sweep/, листы sheet.py
  4 package   reference_match_remaining/build_unity_package.py (восемь клипов) в staging-FBX,
              check_fbx_sweep.py, compare_fbx_takes.py (старые дубли без изменений),
              finalize_validation.py, копия FBX в razlom/.../Forest_Wendigo/
Модель в Unity собирает ForestWendigoBuilder (меню «Разлом/Лесной вендиго/Собрать представление»).
"""
import argparse, shutil, subprocess, sys, tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
REFS = HERE.parents[3] / 'review' / 'mobs-v2-concepts-2026-09-29' / 'refs'
REVIEW = HERE.parents[1] / 'review' / 'animation_sweep'
VIDEO = REFS / 'wendigo-sweep-360.mp4'
UNITY = Path('C:/Users/d.grab/Desktop/the-game/razlom/Assets/Resources/Characters/Forest_Wendigo/ForestWendigo.fbx')
PROD = ANIM / 'unity_package' / 'ForestWendigo_Production.blend'
STAGE = Path(tempfile.gettempdir()) / 'wendigo_sweep_package'


def py(*a): subprocess.run([sys.executable, *map(str, a)], cwd=HERE, check=True)


def bl(blend, script, *a, factory=False):
    cmd = [BLENDER, '-b'] + (['--factory-startup'] if factory else []) + ([str(blend)] if blend else []) + ['--python', str(script)]
    if a: cmd += ['--'] + [str(v) for v in a]
    subprocess.run(cmd, cwd=HERE, check=True)


def extract():
    import imageio_ffmpeg
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    (HERE / 'reference').mkdir(exist_ok=True)
    subprocess.run([ff, '-v', 'error', '-i', str(VIDEO), '-vsync', '0', '-start_number', '0', '-y',
                    str(HERE / 'reference' / 'ref_%03d.png')], check=True)
    subprocess.run([ff, '-v', 'error', '-i', str(VIDEO), '-vf', 'fps=6,scale=320:320,tile=6x4:padding=4:color=black',
                    '-frames:v', '1', '-y', str(REFS / 'wendigo-sweep-360_sheet.jpg')], check=True)


def review():
    bl(HERE / 'Sweep_Baked_r01.blend', HERE / 'render_review.py', '--out', REVIEW)
    for v in ('game', 'front', 'top'):
        py('sheet.py', REVIEW / f'{v}_frames', REVIEW / f'{v}_sheet.jpg', 8, 220)


def package():
    STAGE.mkdir(exist_ok=True)
    old, new = STAGE / 'ForestWendigo_old.fbx', STAGE / 'ForestWendigo_new.fbx'
    shutil.copy2(UNITY, old)
    bl(PROD, ANIM / 'reference_match_remaining' / 'build_unity_package.py', '--fbx', new)
    bl(None, HERE / 'check_fbx_sweep.py', new, factory=True)
    py('compare_fbx_takes.py', old, new)
    py('finalize_validation.py')
    shutil.copy2(new, UNITY)


STEPS = [
    ('extract', extract),
    ('samples', lambda: py('build_sweep.py')),
    ('scenes', lambda: bl(PROD, HERE / 'apply_sweep.py')),
    ('review', review),
    ('package', package),
]

if __name__ == '__main__':
    ap = argparse.ArgumentParser(); ap.add_argument('--from', dest='start', default='extract'); ap.add_argument('--only', default='')
    a = ap.parse_args()
    names = [n for n, _ in STEPS]
    run = [a.only] if a.only else names[names.index(a.start):]
    for n, fn in STEPS:
        if n in run:
            print('==', n, flush=True); fn()
