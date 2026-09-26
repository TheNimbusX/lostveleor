"""Howl (Вой чащи) - whole pipeline in order. python run_pipeline.py [--from STEP] [--no-render] [--no-package]

Reference-match method as for Claw/Leap: reference breakdown -> key poses -> spline -> validation.
  0 extract     howl.mp4 -> reference/ref_###.png (97 frames, 24 fps), reference_sheet.jpg
  1 source      export_fit_source.py (Blender): rig/skin/Idle stance from ForestWendigo_Production.blend
  2 camera      fit_camera.py: reference camera from howl.mp4 frame 0 vs the Idle pose
  3 contact     solve_contact_keys.py: slam/hold key poses (claws on their ground points)
  4 spline      build_howl.py: keys -> quarter-frame samples (IK feet/claws, CoM curve, overlap)
  5 validate    validate_howl.py (numpy)
  6 scenes      apply_howl.py (Blender): Howl_r01.blend (keys) + Howl_Baked_r01.blend (export clip)
  7 review      render_review.py (Blender): checks + game/side/reference-camera frames
  8 package     build_unity_package.py (Blender, 7 clips) -> razlom/.../Forest_Wendigo/ForestWendigo.fbx,
                check_fbx_howl.py (re-import), finalize_validation.py, package_review.py
Key poses live in pose_keys.py (readable controls) + contact_solutions.json (solved).
"""
import subprocess, sys, argparse
from pathlib import Path
HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
REVIEW = HERE.parents[1] / 'review' / 'animation_howl'
FBX = Path('C:/Users/d.grab/Desktop/the-game/razlom/Assets/Resources/Characters/Forest_Wendigo/ForestWendigo.fbx')
PROD = ANIM / 'unity_package' / 'ForestWendigo_Production.blend'


def py(*a): subprocess.run([sys.executable, *a], cwd=HERE, check=True)
def bl(blend, script, *a, factory=False):
    cmd = [BLENDER, '-b'] + (['--factory-startup'] if factory else []) + ([str(blend)] if blend else []) + ['--python', str(script)]
    if a: cmd += ['--'] + list(a)
    subprocess.run(cmd, cwd=HERE, check=True)


def extract():
    import imageio_ffmpeg
    (HERE / 'reference').mkdir(exist_ok=True)
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-v', 'error', '-i', str(HERE.parents[2] / 'review/higgsfield-refs/howl.mp4'),
                    '-vsync', '0', '-start_number', '1', '-y', str(HERE / 'reference' / 'ref_%03d.png')], check=True)


STEPS = [
    ('extract', extract),
    ('source', lambda: bl(PROD, HERE / 'export_fit_source.py')),
    ('camera', lambda: py('fit_camera.py')),
    ('contact', lambda: py('solve_contact_keys.py')),
    ('spline', lambda: py('build_howl.py')),
    ('validate', lambda: py('validate_howl.py')),
    ('scenes', lambda: bl(PROD, HERE / 'apply_howl.py')),
    ('review', lambda: bl(HERE / 'Howl_Baked_r01.blend', HERE / 'render_review.py', '--out', str(REVIEW))),
    ('package', lambda: (bl(PROD, ANIM / 'reference_match_remaining' / 'build_unity_package.py'),
                         bl(None, HERE / 'check_fbx_howl.py', str(FBX), factory=True),
                         py('finalize_validation.py'), py('package_review.py'))),
]

if __name__ == '__main__':
    ap = argparse.ArgumentParser(); ap.add_argument('--from', dest='start', default='extract')
    ap.add_argument('--only', default='')
    a = ap.parse_args()
    names = [n for n, _ in STEPS]
    run = [a.only] if a.only else names[names.index(a.start):]
    for n, fn in STEPS:
        if n in run:
            print('==', n, flush=True); fn()
