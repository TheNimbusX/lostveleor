"""Доля «лунной походки» мобов по записи -capture-body-yaw (ревью 01.10).

Запись пишет EnemyBodyYawRecorder (razlom/Assets/Game.View/EnemyBodyYawRecorder.cs): строка на кадр,
тела всех живых врагов. Мера — та же, что в tools/Combat.Presentation.Tests/EnemyBodyFacingTests.cs:
моб «идёт», когда его шаг не меньше 45% полного, и «едет боком», когда показанное тело дальше 45°
от хода (Плюй-плод пятится честным обратным шагом — у него только 45–135°). Вендиго, Камнекопыт,
Шипомёт и Корнехват ходят телом по Sim — их доля показывается для сравнения. Кадры «в действии»
(mode 1: замах, перекат, залп, оглушение, волок — там Velocity бывает устаревшим) не считаются.

«Было» считается из той же записи: тело = интерполированный взгляд Sim (столбцы simX/simZ), то есть
как рисовалось до правки, — на том же сиде и тех же кадрах. Вторая запись (необязательно) —
отдельный прогон для сравнения.

    python tools/body_yaw_report.py after.jsonl [other.jsonl]
"""
import json
import math
import sys
from collections import defaultdict

KINDS = {0: 'None', 1: 'Хранитель', 2: 'Корнеполз', 3: 'Плюй-плод', 4: 'Вендиго', 5: 'Камнекопыт',
         6: 'Шипомёт', 7: 'Корнехват', 8: 'Расщепень', 9: 'Детёныш'}
MODES = {0: 'по Sim', 1: 'действие', 2: 'по ходу', 3: 'пятится'}
BUD = 3


def angle(ax, ay, bx, by):
    return abs(math.degrees(math.atan2(ax * by - ay * bx, ax * bx + ay * by)))


def measure(path, use_sim_body=False):
    walking, moon, modes, frames = defaultdict(int), defaultdict(int), defaultdict(int), 0
    with open(path, encoding='utf-8') as source:
        for line in source:
            row = json.loads(line)
            if 'meta' in row:
                continue
            frames += 1
            for mob in row['mobs']:
                _, kind, bx, bz, sx, sz, vx, vy, step, mode, _, _ = mob
                if use_sim_body:
                    bx, bz = sx, sz
                modes[mode] += 1
                speed = math.hypot(vx, vy)
                if step <= 0 or speed < step * 0.45 or mode == 1:
                    continue
                off = angle(bx, bz, vx / speed, vy / speed)
                walking[kind] += 1
                if (45 < off < 135) if kind == BUD else off > 45:
                    moon[kind] += 1
    return frames, walking, moon, modes


def report(label, path, use_sim_body=False):
    frames, walking, moon, modes = measure(path, use_sim_body)
    total_w, total_m = sum(walking.values()), sum(moon.values())
    share = 100.0 * total_m / total_w if total_w else 0.0
    print(f'{label}: {path}\n  кадров {frames}, «лунная походка» {share:.1f}% ({total_m}/{total_w})')
    for kind in sorted(walking):
        print(f'    {KINDS.get(kind, kind)}: {100.0 * moon[kind] / walking[kind]:.1f}% из {walking[kind]}')
    print('  что вело тело: ' + ', '.join(f'{MODES.get(m, m)} {n}' for m, n in sorted(modes.items())))
    return share


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    before = report('было (тело = взгляд Sim)', sys.argv[1], use_sim_body=True)
    after = report('стало (показанное тело)', sys.argv[1])
    print(f'итог: {before:.1f}% → {after:.1f}%')
    if len(sys.argv) > 2:
        report('другой прогон', sys.argv[2])
