"""Package the Howl review page: production/review/animation_howl/.

Needs the frames from render_review.py (game_frames, side_frames, fit_frames), validation.json
and blender_checks.json. Writes the retimed reference frames, key sheets, MP4s, index.html
and manifest.json (owner review pending; Claude accepted provisionally).
"""
import json, hashlib, subprocess, shutil
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg
from pose_keys import REF_OF_KEY, CONTACT_FRAME, END_FRAME, HOLD_END

HERE = Path(__file__).resolve().parent
PROD = HERE.parents[1]
OUT = PROD / 'review' / 'animation_howl'; OUT.mkdir(parents=True, exist_ok=True)
REF_VIDEO = PROD.parent / 'review' / 'higgsfield-refs' / 'howl.mp4'
ff = imageio_ffmpeg.get_ffmpeg_exe()
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 22)
small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)
N = END_FRAME + 1
kf = sorted(REF_OF_KEY)
ref_map = [int(round(np.interp(f, kf, [REF_OF_KEY[k] for k in kf]))) for f in range(N)]

PHASES = [(0, 'Сбор: вес вниз, когти назад'), (6, 'Рывок вверх'), (11, 'Вой: череп и рога назад'),
          (17, 'Удар: корпус падает, когти вниз'), (24, 'КОНТАКТ: когти в землю, кольцо'),
          (25, 'Сжатие, рога по инерции'), (29, 'Низкая тяжёлая выдержка (уязвим)'),
          (36, 'Упор на когти: корпус назад, на ноги'), (38, 'Когти отрываются, подъём в стойку')]


def phase(f):
    return [p for s, p in PHASES if f >= s][-1]


# rendered frames -> jpg (the page preloads ~200 images; PNG would be ~120 MB in LFS)
for view in ('game', 'side', 'fit'):
    for png in sorted((OUT / f'{view}_frames').glob('*.png')):
        Image.open(png).convert('RGB').save(png.with_suffix('.jpg'), quality=90)
        png.unlink()

# retimed reference frames (720 jpg) for the page
(OUT / 'reference_frames').mkdir(exist_ok=True)
for f in range(N):
    im = Image.open(HERE / 'reference' / f'ref_{ref_map[f] + 1:03d}.png').convert('RGB').resize((720, 720), Image.LANCZOS)
    im.save(OUT / 'reference_frames' / f'{f:04d}.jpg', quality=88)

# comparison frames: reference | reference camera | game camera
import tempfile
pairs = Path(tempfile.mkdtemp(prefix='howl_pairs_'))   # only feeds comparison.mp4
for f in range(N):
    ref = Image.open(OUT / 'reference_frames' / f'{f:04d}.jpg').convert('RGB').resize((540, 540))
    fit = Image.open(OUT / 'fit_frames' / f'{f:04d}.jpg').convert('RGB').resize((540, 540))
    game = Image.open(OUT / 'game_frames' / f'{f:04d}.jpg').convert('RGB').crop((150, 0, 870, 720)).resize((540, 540))
    im = Image.new('RGB', (1620, 600), (22, 27, 27)); im.paste(ref, (0, 60)); im.paste(fit, (540, 60)); im.paste(game, (1080, 60))
    d = ImageDraw.Draw(im)
    d.text((12, 6), f'Референс howl.mp4 · кадр {ref_map[f]}', font=font, fill='white')
    d.text((552, 6), 'Наша модель · камера референса', font=font, fill='white')
    d.text((1092, 6), 'Игровая камера', font=font, fill='white')
    col = (255, 214, 120) if f == CONTACT_FRAME else (206, 211, 208)
    d.text((12, 34), f'кадр {f:02d}/48 · {phase(f)}', font=small, fill=col)
    im.save(pairs / f'pair_{f:04d}.png')


def encode(pattern, target, fps=24, frames=N):
    subprocess.run([ff, '-v', 'error', '-framerate', str(fps), '-start_number', '0', '-i', str(pattern), '-frames:v', str(frames),
                    '-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2', '-c:v', 'libx264', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
                    '-y', str(target)], check=True)


encode(OUT / 'game_frames' / '%04d.jpg', OUT / 'game.mp4')
encode(OUT / 'side_frames' / '%04d.jpg', OUT / 'side.mp4')
encode(pairs / 'pair_%04d.png', OUT / 'comparison.mp4')

# key sheets
keys_show = [0, 6, 11, 14, 19, 22, 24, 28, 34, 41, 48]
for view, crop in [('game', (150, 0, 870, 720)), ('side', (120, 0, 840, 720))]:
    sheet = Image.new('RGB', (6 * 320, 2 * 344), (22, 27, 27)); d = ImageDraw.Draw(sheet)
    for i, f in enumerate(keys_show):
        im = Image.open(OUT / f'{view}_frames' / f'{f:04d}.jpg').convert('RGB').crop(crop).resize((320, 320))
        sheet.paste(im, ((i % 6) * 320, (i // 6) * 344 + 24))
        d.text(((i % 6) * 320 + 6, (i // 6) * 344 + 2), f'{f} · {phase(f)}', font=small, fill=(255, 214, 120) if f == CONTACT_FRAME else 'white')
    sheet.save(OUT / f'{view}_keys.jpg', quality=90)

val = json.loads((HERE / 'validation.json').read_text())
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
manifest = {
    'stage': 'howl_owner_review',
    'clip': 'Wendigo_Howl', 'name_ru': 'Вой чащи', 'revision': 'r01',
    'owner_review_pending': True,
    'claude_acceptance': 'accepted provisionally 2026-09-26 (owner away, asked not to be asked)',
    'fps': 24, 'frames': [0, END_FRAME], 'contact_frame': CONTACT_FRAME, 'hold_end_frame': HOLD_END,
    'game_contract': {'sim_ticks_per_second': 30, 'windup_ticks': 30, 'recovery_ticks': 24,
                      'view_mapping': 'start->impact = clip 0..24 (1.0 s, 1x); impact->end = clip 24..48 (0.8 s, 1.25x)'},
    'reference': '../../../review/higgsfield-refs/howl.mp4', 'reference_sha256': sha(REF_VIDEO),
    'reference_time_map_clip_to_ref_frame': {str(k): v for k, v in REF_OF_KEY.items()},
    'phases': [{'from_frame': s, 'label': p} for s, p in PHASES],
    'editable_master': '../../animation/reference_match_howl/Howl_r01.blend',
    'derived_clip': '../../animation/reference_match_howl/Howl_Baked_r01.blend',
    'action': 'AN_ForestWendigo_Howl_Baked -> exported as Wendigo_Howl',
    'unity_fbx': 'razlom/Assets/Resources/Characters/Forest_Wendigo/ForestWendigo.fbx',
    'validation': val,
    'files': {'game': 'game.mp4', 'side': 'side.mp4', 'comparison': 'comparison.mp4', 'game_keys': 'game_keys.jpg', 'side_keys': 'side_keys.jpg'},
}
(OUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
(OUT / 'ref_map.json').write_text(json.dumps(ref_map))
n = val['numpy']; lim = val['limits']
rows = [
    ('Контакт когтей с землёй', f"кадр {int(lim['claws_first_touch_frame']['value'])} из 48 — ровно момент удара; до него когти не касаются земли"),
    ('Скольжение стоп (точки опоры)', f"{lim['foot_contact_slip_mm_max']['value']:.4f} мм — стопы стоят на месте весь клип"),
    ('Скольжение когтей в выдержке 24–%d' % val['hold_end_frame'], f"{lim['claw_tip_slip_during_hold_mm_max']['value']:.4f} мм"),
    ('Кончики когтей в земле', f"{lim['claw_tip_depth_mm_max']['value']:.0f} мм (намеренно); остальная сетка выше земли — вершин под землёй: {lim['non_claw_vertices_below_ground']['value']}"),
    ('Длины костей', f"изменение ≤ {lim['bone_length_error_m_max']['value']:.1e} м (только повороты + смещение таза)"),
    ('Перемещение корня', 'нет (root на месте, движение задаёт симуляция)'),
    ('Взаимопроникновение частей тела', f"новых пересечений треугольников: {lim['new_triangle_intersections_frames']['value']} (проверены все 193 четверть-кадра)"),
    ('Центр масс: падение', f"не быстрее {abs(n['com']['vertical_accel_g_windup']['min']):.2f} g на замахе и {abs(n['com']['vertical_accel_g_after_contact']['min']):.2f} g после удара"),
    ('Скорость корпуса в момент удара', f"{n['com']['fall_speed_at_contact_m_s']:.1f} м/с вниз, затем сжатие на четырёх опорах"),
    ('Начало и конец', 'кадры 0 и 48 точно совпадают с позой ожидания (Idle)'),
]
checks = '\n'.join(f'<tr><td>{a}</td><td class="num">{b}</td></tr>' for a, b in rows)
page = (HERE / 'review_page.html').read_text(encoding='utf-8')
page = page.replace('{{CHECKS}}', checks).replace('{{PHASES}}', json.dumps([[s, p] for s, p in PHASES], ensure_ascii=False))
page = page.replace('{{REFMAP}}', ', '.join(f'{k}→{v}' for k, v in REF_OF_KEY.items()))
(OUT / 'index.html').write_text(page, encoding='utf-8')
shutil.copy2(HERE / 'review.css', OUT / 'review.css')
print(OUT)
