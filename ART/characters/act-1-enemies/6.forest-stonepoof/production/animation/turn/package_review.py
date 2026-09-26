from pathlib import Path
import json, subprocess, hashlib, ast, shutil
from PIL import Image, ImageDraw
import imageio_ffmpeg
HERE = Path(__file__).resolve().parent; PROD = HERE.parents[1]; OUT = PROD / 'review' / 'animation_turn'
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
tree = ast.parse((HERE.parent / 'charge_loop' / 'package_review.py').read_text(encoding='utf8'))
for node in tree.body:
    if isinstance(node, ast.FunctionDef) and node.name == 'encode':
        exec(compile(ast.Module(body=[node], type_ignores=[]), 'encode', 'exec'))
N = 60
for clip in ('TurnLeft', 'TurnRight'):
    for view in ('game', 'side'):
        frames = [OUT / f'{clip}_{view}_frames' / f'{i:04d}.png' for i in range(N)]
        assert all(p.exists() for p in frames), (clip, view)
        for codec in ('webm', 'mp4'):
            encode(frames, OUT / f'{clip}_{view}.{codec}', codec)
        sheet = Image.new('RGB', (1440, 540), '#202823'); draw = ImageDraw.Draw(sheet)
        for i, f in enumerate([0, 2, 4, 6, 8, 10, 12, 14]):
            im = Image.open(frames[f]).convert('RGB'); im.thumbnail((360, 240))
            x = i % 4 * 360; y = i // 4 * 270; sheet.paste(im, (x, y))
            draw.text((x + 8, y + 247), f'Frame {f} / 15 | {f/30:.2f}s | yaw {f*6} deg', fill='white')
        sheet.save(OUT / f'{clip}_{view}_keys.jpg', quality=92)
shutil.copy2(PROD / 'references' / 'turn.mp4', OUT / 'reference_turn.mp4')
sheet = Image.new('RGB', (1440, 540), '#202823'); draw = ImageDraw.Draw(sheet)
for i in range(8):
    t = 0.2 + i * 0.5; tmp = OUT / '_ref.jpg'
    subprocess.run([ffmpeg, '-v', 'error', '-y', '-ss', str(t), '-i', str(PROD / 'references' / 'turn.mp4'),
                    '-frames:v', '1', '-q:v', '3', str(tmp)], check=True)
    im = Image.open(tmp).convert('RGB'); im.thumbnail((360, 240))
    x = i % 4 * 360; y = i // 4 * 270; sheet.paste(im, (x, y)); draw.text((x + 8, y + 247), f'Reference {t:.1f}s', fill='white')
tmp.unlink(); sheet.save(OUT / 'reference_keys.jpg', quality=92)
shutil.copy2(PROD / 'review' / 'animation_death_r02' / 'review.css', OUT / 'review.css')
v = json.loads((HERE / 'validation.json').read_text(encoding='utf8'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = {
    'stage': 'turn_in_place_owner_review', 'owner_review_pending': True, 'owner_clip_approved': False,
    'claude_status': 'accepted provisionally by Claude: all numeric checks pass; owner judges motion',
    'clips': {'TurnLeft': 'AN_Stonehoof_TurnLeft', 'TurnRight': 'AN_Stonehoof_TurnRight'},
    'fbx_takes': ['ARM_ForestStonehoof|Stonehoof_TurnLeft', 'ARM_ForestStonehoof|Stonehoof_TurnRight'],
    'fps': 30, 'frames': [0, 15], 'cycle_deg': 90, 'duration_seconds': 0.5,
    'game_contract': 'sim 6 deg/tick at 30 ticks/s; view phase = accumulated yaw / 90, repeats per 90 deg',
    'preview': 'editable scene with preview carrier yaw; 4 cycles = 360 deg = 60 frames; cameras: game 48 deg pitch ortho, side ortho',
    'reference': {'file': 'reference_turn.mp4', 'source': '../../references/turn.mp4',
                  'use': 'style only: head leads, front hooves cross-step, hind pivot, chest weight shift; its drift and ~180 deg amount ignored'},
    'editable_master': '../../animation/turn/Stonehoof_Turn_r01.blend',
    'derived_clip': '../../animation/turn/Stonehoof_Turn_Baked_r01.blend',
    'master_sha256': sha(HERE / 'Stonehoof_Turn_r01.blend'),
    'baked_sha256': sha(HERE / 'Stonehoof_Turn_Baked_r01.blend'),
    'unity_fbx': 'razlom/Assets/Resources/Characters/Forest_Stonehoof/ForestStonehoof.fbx (12 takes; 10 previous takes byte-identical in curves)',
    'validation': v,
}
(OUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf8')
print('TURN_REVIEW_PACKAGED')
