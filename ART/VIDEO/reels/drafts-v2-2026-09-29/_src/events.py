"""Key moments of a reels capture in mp4 seconds, from the player log ([enemy-qa]) and attacks.csv.

python events.py <shot-dir> <video_start>
mp4 time = tick / 30 - video_start + OFFSET (OFFSET calibrated on the flash of the kill hit).
"""
import csv, json, os, re, sys

TPS = 30
OFFSET = -0.1
LINE = re.compile(r'^\[enemy-qa\] tick=(\d+) (\w+) src=(-?\d+) dst=(-?\d+) amount=(-?\d+) hero_hp=(-?\d+)')
KEEP = {'TelegraphOpened', 'TelegraphCancelled', 'Attack', 'Damage', 'Death', 'Stun', 'Evaded',
        'WendigoStarted', 'WendigoImpact', 'StonehoofStarted', 'StonehoofStopped', 'SplitterSplit',
        'EnemyActionStarted', 'EnemyActionImpact', 'EnemyProjectileLaunched', 'Burrowed'}


def events(shot_dir, video_start, duration=None):
    out = []
    log = os.path.join(shot_dir, 'player.log')
    if os.path.exists(log):
        with open(log, encoding='utf-8', errors='replace') as f:
            for line in f:
                m = LINE.match(line)
                if not m or m.group(2) not in KEEP:
                    continue
                tick, kind, src, dst, amount, hp = m.groups()
                t = int(tick) / TPS - video_start + OFFSET
                if t < -0.05 or (duration is not None and t > duration + 0.05):
                    continue
                src, dst = int(src), int(dst)
                if kind == 'Damage' and src == 0:
                    label = 'hero hits enemy %d (%s)' % (dst, amount)
                elif kind == 'Damage' and dst == 0:
                    label = 'HERO HIT by enemy %d (%s)' % (src, amount)
                elif kind == 'Death':
                    label = 'KILL enemy %d' % dst
                elif kind == 'Attack' and src == 0:
                    label = 'hero swing at %d' % dst
                elif kind == 'Attack':
                    label = 'enemy %d attack starts' % src
                elif kind == 'Stun':
                    label = 'stun %d' % dst
                else:
                    label = '%s src=%d dst=%d' % (kind, src, dst)
                out.append({'t': round(t, 2), 'tick': int(tick), 'event': label})
    # combat-feel runs (root swarm): deaths from attacks.csv, its time is the video timeline
    csv_path = os.path.join(shot_dir, 'attacks.csv')
    if not out and os.path.exists(csv_path):
        with open(csv_path, newline='') as f:
            prev = 0
            for row in csv.DictReader(f):
                deaths = int(row['deaths'])
                if deaths > prev:
                    t = float(row['time']) - video_start
                    if t >= -0.05 and (duration is None or t <= duration + 0.05):
                        out.append({'t': round(t, 2), 'tick': int(row['tick']), 'event': 'KILL x%d (total %d)' % (deaths - prev, deaths)})
                    prev = deaths
    return out


if __name__ == '__main__':
    print(json.dumps(events(sys.argv[1], float(sys.argv[2]), float(sys.argv[3]) if len(sys.argv) > 3 else None), indent=1, ensure_ascii=False))
