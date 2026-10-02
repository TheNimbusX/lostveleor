"""Три черновых ролика THE WAR REMAINS (RU/EN): python reels.py <reel|all> [ru|en|both] [--sheet-only]"""
import os, sys
from reelkit import *

OUT = REPO + "/ART/VIDEO/reels/drafts-2026-09-29"
WORK = SCRATCH + "/work"
AUD = REPO + "/razlom/Assets/Resources/Audio/Combat"
MUSIC = REPO + "/razlom/Assets/Resources/Audio/Music/Forest"
# Концовка A 3 с со значком Steam — выбор владельца 29.09 (B заменена).
ENDCARD_B = REPO + "/ART/VIDEO/reels/endcard-2026-09-29/TWR-endcard-A-3s-1080x1920.mp4"
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)


def focus(fx, fy, zoom=1.2, fw=1080, fh=1920):
    """Кроп исходного кадра под 9:16 вокруг точки (fx, fy) с приближением zoom."""
    w, h = fw / zoom, fh / zoom
    x0 = min(max(0, fx - w / 2), fw - w); y0 = min(max(0, fy - h / 2), fh - h)
    return (x0, y0, x0 + w, y0 + h)


# Добивания: кадр вспышки удара (60 fps, прогоны k-*) и точка между героем и врагом.
KILLS = {
    'wendigo':     dict(flash=78,  f=(690, 880)),
    'thorncaster': dict(flash=124, f=(700, 880)),
    'bud':         dict(flash=124, f=(720, 930)),
    'guardian':    dict(flash=76,  f=(650, 900)),
    'stonehoof':   dict(flash=100, f=(530, 830)),
    'snarer':      dict(flash=106, f=(660, 880)),
    'splitter':    dict(flash=113, f=(560, 820)),
}
# Звуки добивания — те же клипы и громкости, что игра играла в записях mobsv2-check (*-death).
KILL_SFX = {
    'guardian':    [('Pelag/Attack_04', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_02', .36), ('Mobs/Guardian/death_02', .29), ('Dissolve/dissolve_sand_00', .25)],
    'bud':         [('Pelag/Attack_05', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_02', .20), ('Mobs/Bud/death_03', .29)],
    'stonehoof':   [('Pelag/Attack_01', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_03', .36), ('Mobs/Stonehoof/death_01', .29), ('Dissolve/dissolve_sand_00', .25)],
    'thorncaster': [('Pelag/Attack_03', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_04', .36), ('Mobs/Thorncaster/death_03', .29), ('Dissolve/dissolve_sand_00', .25)],
    'snarer':      [('Pelag/Attack_05', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_01', .36), ('Mobs/RootSnarer/death_02', .29), ('Dissolve/dissolve_sand_00', .25)],
    'splitter':    [('Pelag/Attack_03', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_04', .36), ('Mobs/Splitter/death_01', .29), ('Mobs/Splitter/crack_02', .26), ('Mobs/Splitter/pop_04', .19), ('Mobs/Splitter/pop_03', .16)],
    'wendigo':     [('Pelag/Attack_05', .31), ('Pelag/Finisher', .39), ('Mobs/Generic/kill_02', .36), ('Mobs/Wendigo/death_02', .29), ('Dissolve/dissolve_sand_00', .25)],
}
SFX_GAIN = 1.7


def sfx_path(rel):
    for ext in ('.wav', '.ogg'):
        p = AUD + '/' + rel + ext
        if os.path.exists(p):
            return p
    raise FileNotFoundError(rel)


def kill_sfx(mob, t):
    out = []
    for i, (rel, v) in enumerate(KILL_SFX[mob]):
        dt = -0.07 if 'Attack' in rel else (0.0 if i < 3 else 0.03)
        if 'Dissolve' in rel: dt = 0.10
        if 'pop_' in rel: dt = 0.12 + 0.07 * (rel.endswith('3'))
        out.append((max(0.0, t + dt), sfx_path(rel), v * SFX_GAIN))
    return out


def kill_clip(mob, at, dur, lead, zoom=1.2):
    k = KILLS[mob]
    return dict(src='k-' + mob, at=at, dur=dur, **{'from': k['flash'] - lead * 60},
                crop=focus(*k['f'], zoom=zoom), punch=[lead], flash=[lead])


# ------------------------------------------------------------------ 2. Сочные убийства
def reel_kills(lang):
    order = ['wendigo', 'thorncaster', 'bud', 'guardian', 'stonehoof', 'splitter']
    durs = [1.5, 1.0, 1.0, 1.0, 1.0, 1.5]
    leads = [0.5, 0.35, 0.35, 0.35, 0.35, 0.4]
    clips, ovs, sfx, t = [], [], [], 0.0
    beats = []
    for i, (m, d, l) in enumerate(zip(order, durs, leads)):
        clips.append(kill_clip(m, t, d, l))
        beats.append(t + l)
        sfx += kill_sfx(m, t + l)
        ovs.append(dict(text=str(i + 1), font='phil', size=210, at=t, until=t + d, x=70, y=1170,
                        align='left', pop=0.35, fin=0.04, fout=0.0, glow=EMBER))
        t += d
    q = 'Какое убийство\nсочнее?' if lang == 'ru' else 'Which kill\nhits hardest?'
    ovs.insert(0, dict(text=q, font='phil', size=104, at=0, until=t, y=235, pop=0.18, fin=0.06, fout=0.0))
    # Сильная доля музыки (120 BPM, доли на .02 с) — на первой вспышке.
    mstart = 72.02 - beats[0]
    return dict(dur=t, clips=clips, overlays=ovs), sfx, (MUSIC + '/Forest_Hard.ogg', mstart), 'kills'


# ------------------------------------------------------------------ 1. Было → стало
def reel_before(lang):
    ru = lang == 'ru'
    split = 4.6
    clips = [
        # Было: запись владельца 03.09 (окно редактора, вырезан Game view), толпа лесных тварей.
        dict(src='old0903', at=0, dur=split, **{'from': 1}, crop=(0, 0, 1185, 636), rect=(0, 0, 1080, 720), fx=477),
        # Стало: вся десятка леса против героя, метки B и знаки удара на телах.
        dict(src='h-all-fight', at=0, dur=split, **{'from': 150}, crop=focus(470, 880, 1.08), rect=(0, 720, 1080, 1200)),
        dict(src='h-wendigo-sweep', at=4.6, dur=1.0, **{'from': 168}, crop=focus(540, 900, 1.05)),
        kill_clip('wendigo', 5.6, 1.25, 0.5),
        dict(src='h-all-fight', at=6.85, dur=0.75, **{'from': 452}, crop=focus(470, 860, 1.12)),
        kill_clip('thorncaster', 7.6, 1.4, 0.5),
    ]
    dur = 9.0
    ov = [
        dict(kind='rect', at=0, until=split, rect=(0, 712, 1080, 16), color=INK, alpha=1.0),
        dict(kind='rect', at=0, until=split, rect=(0, 717, 1080, 6), color=(214, 170, 112), alpha=1.0),
        dict(text='БЫЛО' if ru else 'BEFORE', size=120, at=0, until=split, x=60, y=235, align='left', pop=0.2, fin=0.05, fout=0),
        dict(text='03.09.2026' if ru else 'Sep 3, 2026', font='nunito', size=50, at=0, until=split, x=64, y=378, align='left', fin=0.05, fout=0),
        dict(text='СТАЛО' if ru else 'AFTER', size=120, at=0, until=dur, x=60, y=790, align='left', pop=0.2, fin=0.05, fout=0),
        dict(text='29.09.2026' if ru else 'Sep 29, 2026', font='nunito', size=50, at=0, until=dur, x=64, y=933, align='left', fin=0.05, fout=0),
        dict(text='1 месяц разработки' if ru else '1 month of dev', font='nunito', size=56, at=0.25, until=split,
             cy=720, pill=True, pop=0.25, fin=0.08, fout=0),
    ]
    # После сплита подпись «Стало» уезжает наверх: весь кадр — новая игра.
    for o in ov:
        if o.get('text') in ('СТАЛО', 'AFTER', '29.09.2026', 'Sep 29, 2026'):
            o['until'] = split
    ov += [
        dict(text='СТАЛО' if ru else 'AFTER', size=120, at=split, until=dur, x=60, y=235, align='left', fin=0.0, fout=0),
        dict(text='29.09.2026' if ru else 'Sep 29, 2026', font='nunito', size=50, at=split, until=dur, x=64, y=378, align='left', fin=0.0, fout=0),
        dict(text='1 месяц разработки' if ru else '1 month of dev', font='nunito', size=56, at=split, until=dur,
             x=SAFE_CX, cy=1360, pill=True, fin=0.0, fout=0),
    ]
    sfx = kill_sfx('wendigo', 6.1) + kill_sfx('thorncaster', 8.1)
    # Доли Forest_Hard приходятся на .02 с; добивание Вендиго (6,1 с) — на сильную долю 134,02.
    return dict(dur=dur, clips=clips, overlays=ov), sfx, (MUSIC + '/Forest_Hard.ogg', 127.92), 'before-after'


# ------------------------------------------------------------------ 3. Придумай босса
def reel_boss(lang):
    ru = lang == 'ru'
    hooks = [('h-wendigo-howl', 44, (520, 900, 1.0)), ('h-stonehoof-hug', 52, (560, 900, 1.05)),
             ('h-bud-puddle', 98, (520, 860, 1.05)), ('h-snarer-tank', 38, (520, 900, 1.05)),
             ('h-wendigo-sweep', 176, (540, 900, 1.05)), ('h-all-tank', 236, (470, 870, 1.0))]
    clips, t, hd = [], 0.0, 0.62
    for src, f, (fx, fy, z) in hooks:
        clips.append(dict(src=src, at=t, dur=hd, **{'from': f}, crop=focus(fx, fy, z)))
        t += hd
    mont = t                        # ≈3,7 с монтажа
    slow = 1.7                      # «…но у него нет хозяина» — толпа в замедлении
    clips.append(dict(src='h-all-fight', at=mont, dur=slow, speed=0.55, **{'from': 400}, crop=focus(470, 860, 1.1)))
    last = 400 + int(slow * 0.55 * 60)
    q_at = mont + slow
    hold = 4.5
    clips.append(dict(src='h-all-fight', at=q_at, dur=hold, speed=0.0, **{'from': last}, crop=focus(470, 860, 1.1),
                      kb=(1.0, 1.08),
                      dark=lambda r: 1.0 - 0.72 * ease_out(r / 0.45),
                      blur=lambda r: 16 * ease_out(r / 0.45)))
    dur = q_at + hold
    t1 = 'В лесу\nполно тварей…' if ru else 'The forest is\nfull of monsters…'
    t2 = '…но у него\nнет хозяина' if ru else '…but it has\nno master'
    cta = ('Придумай\nбосса леса —\nлучший вариант\nдобавим в игру' if ru else
           'Design the\nforest boss —\nthe best idea\ngoes into the game')
    ov = [
        dict(text=t1, size=112, at=0.0, until=mont, y=240, pop=0.15, fin=0.06, fout=0.1),
        dict(text=t2, size=112, at=mont, until=q_at + 0.2, y=240, pop=0.15, fin=0.1, fout=0.2),
        dict(text='?', size=560, at=q_at + 0.3, until=dur, cy=660, pop=0.5, fin=0.08, fout=0, glow=EMBER),
        dict(text=cta, size=92, at=q_at + 0.9, until=dur, y=1000, pop=0.12, fin=0.15, fout=0),
    ]
    sfx = [(0.08, sfx_path('Mobs/Wendigo/howl_01'), 0.45)]
    return dict(dur=dur, clips=clips, overlays=ov), sfx, (MUSIC + '/Forest_Elite.ogg', 78.0), 'boss'


REELS = {'before': reel_before, 'kills': reel_kills, 'boss': reel_boss}
# Концовка A 3 с; у «Босса» — наплывом из тёмного кадра с «?».
REEL_END = {'boss': dict(end_from=0.0, end_dur=3.0, xfade=0.3)}


def build(name, lang, end_from=0.0, end_dur=3.0, xfade=0.0, music_gain=1.0):
    spec, sfx, music, slug = REELS[name](lang)
    content = WORK + '/%s_%s_content.mp4' % (slug, lang)
    render_video(spec, content)
    total = spec['dur'] + end_dur - xfade
    wav = WORK + '/%s_%s_mix.wav' % (slug, lang)
    build_audio(total, music, sfx, wav, music_gain=music_gain)
    base = OUT + '/TWR-reel-%s-%s' % (slug, lang)
    assemble(content, ENDCARD_B, end_from, end_dur, wav, base + '.mp4', base + '-silent.mp4',
             xfade=xfade, content_dur=spec['dur'])
    contact_sheet(base + '.mp4', OUT + '/TWR-reel-%s-%s-sheet.jpg' % (slug, lang), cols=8, every=0.5, w=200)
    print('ok', base, '%.2fs' % total)


if __name__ == '__main__':
    names = list(REELS) if sys.argv[1] == 'all' else sys.argv[1].split(',')
    langs = ['ru', 'en'] if len(sys.argv) < 3 or sys.argv[2] == 'both' else [sys.argv[2]]
    for n in names:
        for l in langs:
            build(n, l, **REEL_END.get(n, {}))
