import json
from pathlib import Path
import subprocess
import sys
import numpy as np

repo = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(repo / 'tools'))
from prepare_combat_audio import find_ffmpeg

ffmpeg = find_ffmpeg()
source = repo / 'razlom/Assets/Resources/Audio/Camp'
output = source / 'Prepared'
output.mkdir(exist_ok=True)
report = []
specs = [('Birds', 'Birds', 2, -27), ('Fire', 'Fire', 1, -25),
         ('Cauldron', 'Liquid', 1, -25), ('Anvil', 'Tools', 1, -25),
         ('River', 'Water', 2, -25), ('Music', None, 2, -24)]
for name, match, channels, loudness in specs:
    path = next(source.glob('ES_' + match + '*.mp3')) if match else repo / 'razlom/Assets/Resources/Audio/Music/camp_main_theme.wav'
    target = output / ('Camp_' + name + '.ogg')
    if target.exists():
        raise RuntimeError('Prepared version already exists: ' + str(target))
    raw = subprocess.run([ffmpeg, '-v', 'error', '-i', str(path), '-af',
        f'highpass=f={35 if name == "Music" else 65},loudnorm=I={loudness}:TP=-4:LRA=12',
        '-ac', str(channels), '-ar', '48000', '-f', 'f32le', 'pipe:1'], capture_output=True, check=True).stdout
    samples = np.frombuffer(raw, dtype='<f4').reshape(-1, channels).copy()
    original_seconds = len(samples) / 48000
    if name != 'Anvil':
        # Склейка хвоста с началом сохраняет непрерывность волн, без паузы на границе цикла.
        samples = samples[12000:-12000]
        overlap = 96000
        blend = np.linspace(0, 1, overlap, endpoint=False, dtype=np.float32)[:, None]
        seam = samples[-overlap:] * (1-blend) + samples[:overlap] * blend
        samples = np.concatenate((samples[overlap:-overlap], seam))
    else:
        fade = 960
        samples[:fade] *= np.linspace(0, 1, fade)[:, None]
        samples[-fade:] *= np.linspace(1, 0, fade)[:, None]
    peak = float(np.max(np.abs(samples)))
    if peak > .63:
        samples *= .63 / peak
    subprocess.run([ffmpeg, '-v', 'error', '-f', 'f32le', '-ar', '48000', '-ac', str(channels), '-i', 'pipe:0',
        '-c:a', 'libvorbis', '-q:a', '5', str(target)], input=samples.astype('<f4').tobytes(), check=True)
    report.append(dict(name=name, source=str(path), output=str(target), source_seconds=original_seconds,
        seconds=len(samples)/48000, channels=channels, integrated_loudness_target=loudness,
        peak_db=float(20*np.log10(np.max(np.abs(samples)))), rms_db=float(20*np.log10(np.sqrt(np.mean(samples*samples)))),
        seam_delta=float(np.max(np.abs(samples[-1]-samples[0])))))
(Path(__file__).parent / 'prepared-audio.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
