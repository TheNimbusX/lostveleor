"""Звуковая карта по журналу [audio-log] (прогоны run-captures-v3-audio.ps1).

python audiolog.py [shot ...] — печатает для каждого сыгранного боевого звука время в mp4 reels-v2
(t = tick/30 − VideoStart − 0.1 + delay), клип и громкость; audio_map(shot) — то же списком для сборки.
"""
import os, re, sys

REPO = r"C:/Users/d.grab/Desktop/the-game"
LOGS = REPO + "/artifacts/capture/reels-v3-audiolog"
AUD = REPO + "/razlom/Assets/Resources/Audio/Combat"
# VideoStart прогонов (как в run-captures-v2.ps1 / run-captures-v3-audio.ps1).
VIDEO_START = {
    'dodge-stonehoof-z10': 0.5, 'dodge-snarer-z09': 0.5, 'dodge-thorncaster-z10': 0.5,
    'dodge-wendigo-turn-z10': 0.5, 'after-wendigo-howl': 0.5, 'boss-hero-charge': 0.5,
    'boss-hero-roots': 0.5, 'kill-wendigo-z07': 2.0,
}
LINE = re.compile(r"\[audio-log\] f=\d+ t=[\d.]+ tick=(-?\d+) .*? (play|drop) (\S+) clip=(\S+)(?: vol=([\d.]+))?(?: budget-full)?(?: delay=([\d.]+))? cause=(.*)$")

_index = None


def clip_path(name):
    """Путь клипа по имени из журнала: ищем в Audio/Combat/** (.wav/.ogg/.mp3)."""
    global _index
    if _index is None:
        _index = {}
        for root, _, files in os.walk(AUD):
            for f in files:
                stem, ext = os.path.splitext(f)
                if ext.lower() in ('.wav', '.ogg', '.mp3'):
                    _index.setdefault(stem, os.path.join(root, f).replace('\\', '/'))
    return _index.get(name)


def audio_map(shot):
    """[(t_mp4, sound, clip, vol, path, cause)] — только сыгранные звуки."""
    out = []
    path = os.path.join(LOGS, shot, 'player.log')
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = LINE.search(line)
            if not m or m.group(2) != 'play':
                continue
            tick, _, sound, clip, vol, delay, cause = m.groups()
            t = int(tick) / 30.0 - VIDEO_START[shot] - 0.1 + (float(delay) if delay else 0.0)
            out.append((round(t, 3), sound, clip, float(vol or 0), clip_path(clip), cause.strip()))
    return out


if __name__ == '__main__':
    for shot in sys.argv[1:] or sorted(VIDEO_START):
        print('==', shot)
        for t, sound, clip, vol, p, cause in audio_map(shot):
            if sound == 'Footstep':
                continue
            print('  %6.2f  %-18s %-16s vol %.3f  %s  | %s' % (t, sound, clip, vol, 'ok' if p else 'NO FILE', cause[:70]))
