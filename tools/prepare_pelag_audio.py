"""Prepare the owner's September 15 Pelag recordings; retain the original takes."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np
from prepare_combat_audio import find_ffmpeg

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ART/PELAG/audio/2026-09-15/original'
OUTPUT = ROOT / 'ART/PELAG/audio/2026-09-15/prepared'
# Тайминги относятся к исходным записям; пять взмахов не играют одной очередью.
TAKES = [
    ('cleave-attack.mp3', 'Cleave', .06, .60, -6, .035),
    ('dash--pelag.wav', 'Dash', .07, .78, -6, .065),
    ('whirlewind.wav', 'Whirlwind', 0, .62, -6, .055),
    ('ladno-smazal.mp3', 'BlazePrepare', 0, .85, -8, .055),
    ('ladno-smazal-fireburst.mp3', 'BlazeFire', .10, 3.10, -9, .25),
    ('last-hit-with-blood.mp3', 'Finisher', .15, .85, -7, .055),
]
for i, (start, end) in enumerate([(.06,.57),(1.04,1.54),(1.85,2.36),(2.77,3.28),(3.61,4.08)]):
    TAKES.append(('autoattack-pelag.mp3', f'Attack_{i+1:02d}', start, end, -8, .035))

def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    decoded = {}
    report = []
    for filename, name, start, end, peak_db, fade_out in TAKES:
        if filename not in decoded:
            # У кувырка единичные пики сильно выше тела звука; мягкая компрессия
            # позволяет услышать движение без чрезмерно громкого щелчка.
            filters = ['-af','acompressor=threshold=0.08:ratio=3:attack=3:release=70:makeup=1'] if name == 'Dash' else []
            raw = subprocess.run([find_ffmpeg(), '-v','error','-i',str(SOURCE/filename), *filters,
                '-f','f32le','-ar','48000','-ac','1','pipe:1'],capture_output=True,check=True).stdout
            decoded[filename] = np.frombuffer(raw,dtype='<f4')
        samples = decoded[filename][round(start*48000):round(end*48000)].astype(np.float64)
        samples -= samples.mean()
        gain = 10**(peak_db/20)/np.max(np.abs(samples))
        samples *= gain
        attack = min(144,len(samples))
        release = min(round(fade_out*48000),len(samples))
        samples[:attack] *= np.linspace(0,1,attack)
        samples[-release:] *= np.linspace(1,0,release)
        pcm = np.round(np.clip(samples,-1,1)*32767).astype('<i2')
        with wave.open(str(OUTPUT/(name+'.wav')),'wb') as writer:
            writer.setparams((1,2,48000,len(pcm),'NONE','not compressed'))
            writer.writeframes(pcm.tobytes())
        report.append(dict(source=filename,sha256=hashlib.sha256((SOURCE/filename).read_bytes()).hexdigest(),
            output=name+'.wav',window=[start,end],duration=len(pcm)/48000,
            gain_db=round(float(20*np.log10(gain)),2),peak_dbfs=round(float(20*np.log10(np.max(np.abs(samples)))),2),
            fade_out=fade_out,clipped=int(np.count_nonzero(np.abs(samples)>=1))))
    (OUTPUT.parent/'preparation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))

if __name__ == '__main__': main()
