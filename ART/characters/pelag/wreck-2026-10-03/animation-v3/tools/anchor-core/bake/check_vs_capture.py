"""Pipeline check without Unity (substitute for DESIGN 3.5 until AnchorGripTrackDump runs):
compare the exported grip track with the chain-grip point the GAME logged per rendered frame.

python check_vs_capture.py <clip.grip.json> <capture run dir> [--strike 0] [--clip-state-hash -857884016]
The run must be a -LiveSkill wreck capture: player.log has `[anchor-slam] ... hand=(x, y, z)` once per frame
(ChainGripPosition, world) and locomotion.csv has root position, yaw and the animator state + normalised phase.
Lines are paired by order with locomotion rows; the row offset and the clip length are fitted (offset -4..+4).
Prints RMS / max distance in root space and the best uniform scale (1.0 = units are right).
"""
import json, math, re, sys, argparse, os

ap = argparse.ArgumentParser()
ap.add_argument('grip'); ap.add_argument('run')
ap.add_argument('--strike', type=int, default=0, help='which [anchor-slam] restart (0 = first strike)')
ap.add_argument('--state', default='', help='animator state hash of the clip in locomotion.csv (default: first non-idle)')
ap.add_argument('--out', default='')
a = ap.parse_args()
grip = json.load(open(a.grip, encoding='utf8'))
sub, frames = grip['sub'], grip['frames']
track = [s['grip'] for s in grip['samples']]


def at(frame):
    frame = max(0.0, min(frames, frame))
    i = min(int(frame * sub), len(track) - 2)
    u = frame * sub - i
    return [track[i][c] + (track[i + 1][c] - track[i][c]) * u for c in range(3)]


num = r'(-?\d+,\d+|-?\d+\.\d+)'
pat = re.compile(r'^\[anchor-slam\] tick=(\d+) time=' + num + r'.*?hand=\(([^)]*)\)')
lines, strike, prev = [], -1, None
for line in open(os.path.join(a.run, 'player.log'), encoding='utf-8', errors='replace'):
    m = pat.match(line)
    if not m:
        continue
    t = float(m.group(2).replace(',', '.'))
    if prev is None or t < prev - .2:
        strike += 1
    prev = t
    if strike == a.strike:
        lines.append([float(x) for x in m.group(3).split(', ')])
rows = []
with open(os.path.join(a.run, 'locomotion.csv'), encoding='utf8') as f:
    head = f.readline().strip().split(',')
    for line in f:
        v = line.strip().split(',')
        rows.append(dict(zip(head, v)))
idle = ('CombatIdle_v5', 'RelaxedIdle_v5', 'Run_v5')
state = a.state
first = None
for i, r in enumerate(rows):
    if (state and r['state'] == state) or (not state and r['state'] not in idle and r['state'].lstrip('-').isdigit()):
        if a.strike == 0 or first is not None:
            first = i
            break
        first = i
state = rows[first]['state']
# rows of this strike: consecutive rows with the clip state (restart = phase drop)
seg = [first]
while seg[-1] + 1 < len(rows) and rows[seg[-1] + 1]['state'] == state and float(rows[seg[-1] + 1]['phase']) >= float(rows[seg[-1]]['phase']):
    seg.append(seg[-1] + 1)


def local(p, r):
    yaw = math.radians(float(r['yaw']))
    dx, dy, dz = p[0] - float(r['rootX']), p[1] - float(r['rootY']), p[2] - float(r['rootZ'])
    return [dx * math.cos(yaw) - dz * math.sin(yaw), dy, dx * math.sin(yaw) + dz * math.cos(yaw)]


best = None
for off in range(-4, 5):
    pairs = []
    for j, p in enumerate(lines):
        ri = first + off + j
        if ri < first or ri > seg[-1]:
            continue
        phase = float(rows[ri]['phase'])
        fr = phase * frames
        pairs.append((fr, local(p, rows[ri]), at(fr)))
    if len(pairs) < 6:
        continue
    d = [math.dist(g, e) for _, g, e in pairs]
    rms = math.sqrt(sum(x * x for x in d) / len(d))
    if best is None or rms < best[0]:
        best = (rms, off, pairs, d)
rms, off, pairs, d = best
# uniform scale about the root that best maps export -> game (1.0 = metres agree)
num_s = sum(g[c] * e[c] for _, g, e in pairs for c in range(3))
den_s = sum(e[c] * e[c] for _, g, e in pairs for c in range(3))
scale = num_s / den_s
skip = [x for (fr, _, _), x in zip(pairs, d) if fr >= 1.0]   # first frame is the 0.025 s cross-fade from idle
res = {'clip': grip['clip'], 'run': os.path.basename(a.run.rstrip('/\\')), 'strike': a.strike, 'rowOffset': off,
       'pairs': len(pairs), 'rms': round(rms, 4), 'max': round(max(d), 4),
       'rmsAfterBlend': round(math.sqrt(sum(x * x for x in skip) / max(1, len(skip))), 4),
       'maxAfterBlend': round(max(skip) if skip else 0, 4), 'bestUniformScale': round(scale, 4),
       'perFrame': [[round(fr, 3), [round(x, 3) for x in g], [round(x, 3) for x in e], round(x, 3)]
                    for (fr, g, e), x in zip(pairs, d)]}
print('CAPTURE_CHECK clip=%s pairs=%d offset=%d rms=%.3f max=%.3f afterBlend rms=%.3f max=%.3f scale=%.3f'
      % (res['clip'], res['pairs'], off, rms, max(d), res['rmsAfterBlend'], res['maxAfterBlend'], scale))
for fr, g, e, x in res['perFrame']:
    print('  frame %6.3f game %s export %s d=%.3f' % (fr, g, e, x))
if a.out:
    json.dump(res, open(a.out, 'w', encoding='utf8'), indent=1)
