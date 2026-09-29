"""Страница проверки клипов Корнеполза v2: ролики с игрового угла, сбоку и с ракурса рефа
рядом с листами рефов Higgsfield.

Запуск после render_rootswarm_v2_review.py (кадры в ROOTSWARM_V2_TILES):
    python make_rootswarm_v2_review_page.py
Выход: production/review/animation_v2/ (index.html, *.mp4, листы кадров, manifest.json).

Темп роликов — игровой: бег ×(3,4 / 2,38), смерть ×2 (скорость состояния), укус — по тикам
Sim через те же ключи, что RootSwarmSwing в CharacterAnimatorView.
"""
import json, os, shutil, subprocess, tempfile
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r'C:/Users/d.grab/Desktop/the-game')
PROD = ROOT / 'ART/characters/act-1-enemies/1.forest-rootswarm/production'
TILES = Path(os.environ.get('ROOTSWARM_V2_TILES', str(ROOT / 'artifacts/rootswarm-v2-review')))
OUT = PROD / 'review/animation_v2'
REFS_REL = '../../../../review/mobs-v2-concepts-2026-09-29/refs'
OUT.mkdir(parents=True, exist_ok=True)

try:
    import imageio_ffmpeg
    FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
except ImportError:
    FFMPEG = shutil.which('ffmpeg')

build = json.loads((PROD / 'rootswarm_v2_build.json').read_text(encoding='utf8'))
qc = json.loads((TILES / 'qc.json').read_text(encoding='utf8'))
RUN_TEMPO = 3.4 / build['run_ground_speed_mps']      # Simulation.RootSwarmMoveSpeed / скорость клипа


def warp(xs, ys, t):
    """Монотонная кубика — копия CharacterAnimatorView.Warp."""
    last = len(xs) - 1
    if t <= xs[0]: return ys[0]
    if t >= xs[last]: return ys[last]
    k = 0
    while t > xs[k + 1]: k += 1

    def slope(i):
        if i == 0: return (ys[1] - ys[0]) / (xs[1] - xs[0])
        if i == last: return (ys[last] - ys[last - 1]) / (xs[last] - xs[last - 1])
        left = (ys[i] - ys[i - 1]) / (xs[i] - xs[i - 1])
        right = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i])
        return 0.0 if left * right <= 0 else 2 * left * right / (left + right)

    h = xs[k + 1] - xs[k]
    s = (t - xs[k]) / h
    s2, s3 = s * s, s * s * s
    return ((2 * s3 - 3 * s2 + 1) * ys[k] + (s3 - 2 * s2 + s) * h * slope(k)
            + (3 * s2 - 2 * s3) * ys[k + 1] + (s3 - s2) * h * slope(k + 1))


def bite_ticks():
    """Кадр клипа на каждом тике удара: 12 замаха + 8 восстановления, как в игре."""
    w, r = build['bite']['windup'], build['bite']['recovery']
    frames = [warp(w['ticks'], w['frames'], t) for t in range(0, 12)]
    frames += [warp(r['ticks'], r['frames'], t) for t in range(0, 9)]
    return [int(round(f)) for f in frames]


SEQUENCES = {
    'Idle': (list(range(0, 120)) * 2, 30),
    'Run': (list(range(0, 12)) * 12, round(30 * RUN_TEMPO)),
    'AttackA': (([8] * 12 + bite_ticks() + [24] * 10) * 3, 30),
    'AttackB': (([8] * 12 + bite_ticks() + [24] * 10) * 3, 30),
    'Hit': (([0] * 8 + list(range(0, 11)) + [10] * 12) * 3, 30),
    'Death': ([0] * 16 + list(range(0, 67)) + [66] * 50, 60),
}
REF = {'Idle': 'idle', 'Run': 'run', 'AttackA': 'bite', 'AttackB': 'bite', 'Hit': 'hit', 'Death': 'death'}
VIEWS = ['game', 'side', 'ref']


def encode(clip, view):
    frames, fps = SEQUENCES[clip]
    with tempfile.TemporaryDirectory() as tmp:
        for i, f in enumerate(frames):
            shutil.copyfile(TILES / clip / f'{view}_{f:03d}.png', Path(tmp) / f'{i:05d}.png')
        out = OUT / f'{view}_{clip}.mp4'
        subprocess.run([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y', '-framerate', str(fps),
                        '-i', str(Path(tmp) / '%05d.png'), '-c:v', 'libx264', '-pix_fmt', 'yuv420p',
                        '-crf', '21', '-movflags', '+faststart', str(out)], check=True)
    return out.name


def sheet(clip, view, picks, labels):
    font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 18)
    tiles = [Image.open(TILES / clip / f'{view}_{f:03d}.png').convert('RGB').crop((30, 30, 450, 450)) for f in picks]
    w, h = tiles[0].size
    cols = min(6, len(tiles))
    rows = (len(tiles) + cols - 1) // cols
    img = Image.new('RGB', (cols * w, rows * (h + 28)), (18, 20, 17))
    d = ImageDraw.Draw(img)
    for k, (tile, f, text) in enumerate(zip(tiles, picks, labels)):
        x, y = (k % cols) * w, (k // cols) * (h + 28)
        img.paste(tile, (x, y))
        d.text((x + 8, y + h + 3), f'кадр {f} · {text}', font=font, fill=(236, 226, 196))
    name = f'{view}_{clip}_keys.jpg'
    img.save(OUT / name, quality=86)
    return name


KEYS = {
    'Idle': ([0, 12, 24, 30, 46, 72, 84, 100], ['стойка', 'рывок головы', 'голова в сторону', 'когти сжаты',
                                                  'голова в другую', 'крен', 'дрожь шипов', 'когти']),
    'Run': ([0, 2, 4, 6, 8, 10], ['сбор', 'толчок задних', 'полёт вытянут', 'передние опора', 'перекат', 'вынос']),
    'AttackA': ([8, 11, 14, 16, 17, 18, 20, 22, 24], ['стойка', 'оттяг', 'сжат', 'дрожь', 'бросок', 'КОНТАКТ',
                                                        'полёт выпада', 'касание', 'стойка']),
    'AttackB': ([8, 11, 14, 16, 17, 18, 20, 22, 24], ['стойка', 'оттяг', 'сжат', 'дрожь', 'бросок', 'КОНТАКТ',
                                                        'полёт выпада', 'касание', 'стойка']),
    'Hit': ([0, 1, 2, 3, 5, 7, 10], ['стойка', 'удар', 'на дыбы', 'пик', 'опускается', 'лапы на земле', 'стойка']),
    'Death': ([0, 4, 8, 16, 24, 30, 34, 38, 44, 50], ['стойка', 'на дыбы', 'лапы вскинуты', 'шатается',
                                                        'ноги подломились', 'падает', 'лапы врозь', 'удар о землю',
                                                        'отскок', 'покой']),
}

TEXT = {
    'Idle': ('Покой (петля)', '120 кадров, 4 с, три вдоха. Рывки головы на 10, 42, 70, 94; когти сжимаются на 24 '
             '(левая) и 58 (правая); дрожь спинных шипов 80–92. Лапы и ноги стоят на месте, шов петли 0.'),
    'Run': ('Бег — суетливый галоп (петля)', f'12 кадров. Опорная лапа проходит {build["run_stride_units"]:.2f} '
            f'единицы за цикл = {build["run_ground_speed_mps"]:.2f} м/с при ×1; Sim 3,4 м/с → клип ×{RUN_TEMPO:.2f}, '
            'цикл 0,28 с (в ролике — этот темп). Задние касаются первыми, передние тянутся вперёд; две фазы полёта.'),
    'AttackA': ('Укус A (левая лапа)', 'Окно импорта 8…24. Замах 12 тиков: оттяг назад-вниз 8→14, дрожь 14→16, '
                'бросок 16→18, контакт на 18 (12-й тик). Восстановление 8: полёт выпада 18→22 (Sim везёт тело '
                '0,5 м за 4 тика — на ролике тоже), оседание 22→24. Ролик — по тикам, как в игре.'),
    'AttackB': ('Укус B (правая лапа)', 'Та же стойка на входе и выходе, тот же контакт на 18; тянется правой '
                'лапой и доворачивает вправо. Игра чередует A и B.'),
    'Hit': ('Попадание', '11 кадров, 0,33 с родным темпом (выход на 0,8 + смешивание 0,14 с = 0,43 с). '
            'Корпус встаёт на дыбы, лицо запрокидывается, лапы вскинуты; к 9 лапы снова на земле.'),
    'Death': ('Смерть', '67 кадров, играется ×2 (в ролике — этот темп): на дыбы с раскинутыми лапами 0→8, '
              'шатается 8→24, ноги подламываются и тело падает ничком 24→38, отскок и покой с 50 (0,83 с). '
              'Лежит брюхом на земле, лапы врозь, лицо видно.'),
}


def fmt_qc(clip):
    q = qc[clip]
    lo, hi = min(q['min_y']), max(q['min_y'])
    parts = [f'нижняя точка меша {lo * 100 * 1.5868:+.1f}…{hi * 100 * 1.5868:+.1f} см игры']
    if clip in ('Idle', 'Run'):
        parts.append(f'шов петли {q["seam"]:.0e}')
    if clip.startswith('Attack'):
        parts.append(f'клюв дальше всего вперёд на кадре {q["beak_max_forward_frame"]}, пик скорости на '
                     f'{q["beak_peak_speed_frame"]}')
    if clip in ('Run', 'Death'):
        parts.append(f'центр масс гуляет {q["com_xz_drift"] * 158.68:.1f} см — импортёр снимает, съёмка тоже')
    return '; '.join(parts)


sections = []
manifest = {'build': build, 'qc_summary': {}, 'videos': {}}
for clip in ['Idle', 'Run', 'AttackA', 'AttackB', 'Hit', 'Death']:
    videos = {view: encode(clip, view) for view in VIEWS}
    picks, labels = KEYS[clip]
    keys = sheet(clip, 'ref', picks, labels)
    game_keys = sheet(clip, 'game', picks, labels)
    ref = REF[clip]
    title, text = TEXT[clip]
    manifest['videos'][clip] = videos
    manifest['qc_summary'][clip] = fmt_qc(clip)
    sections.append(f'''
<section class="clip" id="{clip}">
  <h2>{title} <code>Forest_RootSwarm@{clip}.fbx</code></h2>
  <p>{text}</p>
  <p class="qc">{fmt_qc(clip)}</p>
  <div class="row">
    <figure><video src="{videos['game']}" autoplay loop muted playsinline></video><figcaption>Игровая камера (орто 48°, вблизи)</figcaption></figure>
    <figure><video src="{videos['side']}" autoplay loop muted playsinline></video><figcaption>Сбоку (орто)</figcaption></figure>
    <figure><video src="{videos['ref']}" autoplay loop muted playsinline></video><figcaption>Ракурс рефа</figcaption></figure>
    <figure><video src="{REFS_REL}/swarm-{ref}.mp4" autoplay loop muted playsinline></video><figcaption>Реф Higgsfield swarm-{ref}.mp4</figcaption></figure>
  </div>
  <div class="sheets">
    <figure><img src="{REFS_REL}/swarm-{ref}_sheet.jpg" alt="Лист рефа swarm-{ref}"><figcaption>Реф: лист кадров</figcaption></figure>
    <figure><img src="{keys}" alt="Ключевые кадры {clip}, ракурс рефа"><figcaption>Наш клип: ключевые кадры, ракурс рефа</figcaption></figure>
    <figure><img src="{game_keys}" alt="Ключевые кадры {clip}, игровая камера"><figcaption>Наш клип: те же кадры с игровой камеры</figcaption></figure>
  </div>
</section>''')

html = f'''<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Корнеполз · анимации v2</title>
<style>
:root {{ --bg:#151813; --panel:#1f241d; --ink:#ece2c4; --muted:#a8ad98; --accent:#d9a441; }}
body {{ margin:0; background:var(--bg); color:var(--ink); font:15px/1.5 "Segoe UI",system-ui,sans-serif; }}
main {{ max-width:1500px; margin:0 auto; padding:24px 16px 64px; }}
h1 {{ font-size:28px; margin:0 0 6px; }} h2 {{ font-size:20px; margin:0 0 6px; }}
code {{ color:var(--muted); font-size:13px; margin-left:8px; }}
.lead {{ color:var(--muted); max-width:1100px; }}
nav a {{ color:var(--accent); margin-right:14px; text-decoration:none; }}
.clip {{ background:var(--panel); border-radius:10px; padding:16px; margin:22px 0; }}
.qc {{ color:var(--muted); font-size:13px; }}
.row {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(260px,1fr)); gap:12px; }}
.sheets {{ display:grid; grid-template-columns:1fr; gap:12px; margin-top:12px; }}
figure {{ margin:0; }} video, img {{ width:100%; border-radius:6px; background:#000; display:block; }}
figcaption {{ color:var(--muted); font-size:13px; margin-top:4px; }}
ul {{ max-width:1100px; }}
</style></head><body><main>
<h1>Корнеполз — все анимации заново (v2)</h1>
<p class="lead">По пяти рефам Higgsfield, принятым 29.09 (покой, суетливый бег, оттяг и укус, попадание, смерть).
Модель, меш, веса и скелет прежние; клипы собраны в Blender скриптом
<code>production/build_rootswarm_v2.py</code>, кадры сняты с выгруженных FBX
(<code>render_rootswarm_v2_review.py</code>). 30 кадров/с, корень на месте — выпад укуса делает Sim.
Кадры снимались в Blender: освещение условное, персонаж не высветлен.</p>
<nav><a href="#Idle">Покой</a><a href="#Run">Бег</a><a href="#AttackA">Укус A</a><a href="#AttackB">Укус B</a><a href="#Hit">Попадание</a><a href="#Death">Смерть</a></nav>
<ul class="lead">
<li>Игровая камера на роликах приближена: в бою Корнеполз ростом около 55 пикселей на 1080p, читаются только оттяг, бросок и падение.</li>
<li>У бега и смерти импортёр снимает проезд центра масс — на роликах он снят так же.</li>
<li>Что смотреть в игре: оттяг перед укусом виден заранее, укус совпадает с уроном, стопы на бегу не скользят, после смерти тело лежит на земле, а не висит и не тонет.</li>
</ul>
{''.join(sections)}
</main></body></html>
'''
(OUT / 'index.html').write_text(html, encoding='utf8')
(OUT / 'manifest.json').write_text(json.dumps(manifest, indent=1, ensure_ascii=False), encoding='utf8')
print('ROOTSWARM_V2_PAGE', OUT / 'index.html')
