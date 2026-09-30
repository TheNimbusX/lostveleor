"""Offline decoding check, independent of the browser and its file-URL policy."""
from pathlib import Path
import json
import re
import subprocess

root = Path(__file__).resolve().parent
workspace = root.parents[4]
decoder = workspace / 'artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
report = []
for category in ('game', 'game-verified', 'fps', 'fps-verified', 'poses-r02'):
    for movie in sorted((root / category).rglob('*.mp4')):
        if category == 'game' and movie.parent.name in ('basic-roll-after', 'basic-attacks-r02',
            'slam-after', 'roll-after', 'death-after', 'basic-attack-handoff-r02'):
            continue
        if category == 'fps' and movie.parent.name.startswith('roll-'):
            continue
        result = subprocess.run([str(decoder), '-hide_banner', '-nostdin', '-i', str(movie),
            '-map', '0:v:0', '-f', 'null', '-'], capture_output=True, text=True, errors='replace', timeout=45)
        assert result.returncode == 0, (movie, result.stderr[-600:])
        assert 'Video: h264' in result.stderr, movie
        duration = re.search(r'Duration: ([\d:.]+)', result.stderr).group(1)
        stream = next(line.strip() for line in result.stderr.splitlines() if 'Video: h264' in line)
        report.append({'file': movie.relative_to(root).as_posix(), 'duration': duration,
            'stream': stream, 'full_decode_passed': True})
(root / 'video-formats.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS: decoded all frames of', len(report), 'H.264 videos without errors')
