"""Вихрь, звук v3 — одна семья с обычными ударами сабли.

Каст и импульсы строятся из записей владельца Attack_01..05 (те же, что у автоатаки),
удар — из HitBody + HitMetal (как у обычного попадания) с низом из записи
Epidemic «Designed Impact Low Deep Thuds» для веса. Воздушный свиш Epidemic —
только тихая «воздушная» подложка каста. Кишки, точилка и кино-гул целиком не
используются: у нас деревянные стражи и никакой крови.

Запуск из корня репозитория: python tools/audio/build-whirlwind-sfx.py
"""
import glob, os, subprocess, tempfile, wave
import numpy as np

SR = 48000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FF = glob.glob(os.path.join(ROOT, 'artifacts', 'tools', 'python', 'imageio_ffmpeg', 'binaries', 'ffmpeg-*.exe'))[0]
AUDIO = os.path.join(ROOT, 'razlom', 'Assets', 'Resources', 'Audio', 'Combat')
PELAG = os.path.join(AUDIO, 'Pelag')
ES = os.path.join(ROOT, 'ART', 'PELAG', 'sfx', '2026-09-24-whirlwind')
THUD = os.path.join(ES, 'ES_Designed, Impact, Hit, Low, Deep Thuds, Boom - Epidemic Sound - 3881-5321.wav')
SWISH = os.path.join(ES, 'ES_Swooshes, Swish, Airy, Short, Mid Freq 01 - Epidemic Sound.mp3')
SWISH_EVENTS = [(0.03, 0.098), (0.99, 0.030), (2.00, 0.053)]
THUD_PEAK = 0.150

def decode(path, filters=None):
    cmd = [FF, '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR)]
    if filters: cmd += ['-af', ','.join(filters)]
    cmd += ['-f', 'f32le', '-']
    return np.frombuffer(subprocess.run(cmd, capture_output=True).stdout, dtype=np.float32).copy()

def pitched(path, rate, extra=None):
    filters = [] if rate == 1.0 else [f'asetrate={int(SR * rate)},aresample={SR}']
    if extra: filters += extra
    return decode(path, filters)

def seg(a, start, length):
    s = int(start * SR); out = np.zeros(int(length * SR), dtype=np.float32)
    part = a[max(0, s):s + len(out)]; out[:len(part)] = part; return out

def fade(a, fin=0.003, fout=0.08, curve=1.5):
    a = a.copy(); ni = int(fin * SR); no = int(fout * SR)
    if ni: a[:ni] *= np.linspace(0, 1, ni, dtype=np.float32)
    if no: a[-no:] *= np.linspace(1, 0, no, dtype=np.float32) ** curve
    return a

def peak_time(a): return int(np.argmax(np.abs(a))) / SR

def place(layers, length):
    """layers: (array, peak_time_in_array, target_peak_time, gain_db)"""
    out = np.zeros(int(length * SR), dtype=np.float32)
    for a, peak, target, gain in layers:
        shift = int((target - peak) * SR); g = 10 ** (gain / 20)
        lo = max(0, -shift); hi = min(len(a), len(out) - shift)
        if hi > lo: out[lo + shift:hi + shift] += a[lo:hi] * g
    return out

def db(x): return 20 * np.log10(max(float(x), 1e-9))

def write(path, a):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())

def master(a, peak_db, mean_db, comp='acompressor=threshold=-16dB:ratio=2:attack=3:release=80:makeup=1'):
    """Лёгкий компрессор и лимитер, затем нормализация по пику и среднему (как prepare-sounds.ps1)."""
    tmp_in = tempfile.mktemp(suffix='.wav'); tmp_out = tempfile.mktemp(suffix='.wav')
    write(tmp_in, a / max(np.abs(a).max(), 1e-6) * 0.7)
    subprocess.run([FF, '-v', 'error', '-y', '-i', tmp_in, '-af', f'{comp},alimiter=limit=0.97:attack=1:release=40',
                    '-ar', str(SR), '-ac', '1', '-f', 'f32le', tmp_out], capture_output=True)
    b = np.fromfile(tmp_out, dtype=np.float32); os.remove(tmp_in); os.remove(tmp_out)
    peak = np.abs(b).max(); rms = np.sqrt((b ** 2).mean())
    gain = min(10 ** (peak_db / 20) / max(peak, 1e-9), 10 ** (mean_db / 20) / max(rms, 1e-9))
    return np.clip(b * gain, -1, 1)

def save(name, a):
    write(os.path.join(PELAG, name + '.wav'), a)
    print(f'{name}: {len(a)/SR:.2f}s peak {db(np.abs(a).max()):.1f}dB @ {peak_time(a):.3f}s mean {db(np.sqrt((a**2).mean())):.1f}dB')

attack = {i: decode(os.path.join(PELAG, f'Attack_{i:02d}.wav')) for i in range(1, 6)}
body = [decode(p) for p in sorted(glob.glob(os.path.join(AUDIO, 'HitBody', '*.ogg')))]
metal = [decode(p) for p in sorted(glob.glob(os.path.join(AUDIO, 'HitMetal', '*.ogg')))]
thud_low = decode(THUD, ['lowpass=f=180', 'lowpass=f=180'])
swish = decode(SWISH)

# ---------- Каст: два взмаха сабли владельца (тёмный + светлый), воздух, подъём низа. Пики на 0,21 с. ----------
for i, (dark, bright, sw) in enumerate([(4, 3, 0), (2, 5, 1)]):
    a_dark = fade(attack[dark], 0.002, 0.10)
    a_bright = fade(pitched(os.path.join(PELAG, f'Attack_{bright:02d}.wav'), 0.94), 0.002, 0.10)
    s0, sp = SWISH_EVENTS[sw]
    air = fade(seg(swish, s0, 0.5), 0.004, 0.14)
    rise = fade(seg(thud_low, 0.0, 0.38)[::-1].copy(), 0.05, 0.02)
    mix = place([
        (a_dark, peak_time(a_dark), 0.21, 0.0),
        (a_bright, peak_time(a_bright), 0.245, -3.0),       # второй клинок чуть позже: оборот, а не один мах
        (air, sp, 0.20, -9.0),
        (rise, len(rise) / SR, 0.215, -13.0),
    ], 0.72)
    save(f'Whirlwind_{i + 1:02d}', master(fade(mix, 0.002, 0.16), -5.0, -20.5))

# ---------- Удар по толпе: обычный удар тела + сталь (как у автоатаки) и низ для веса. ----------
for i, (b, m) in enumerate([(2, 0), (0, 2), (3, 4)]):
    punch = fade(seg(body[b], 0.0, 0.4), 0.001, 0.10)
    ring = fade(seg(metal[m], 0.0, 0.32), 0.001, 0.12)
    low = fade(seg(thud_low, 0.0, 0.5), 0.002, 0.2)
    mix = place([
        (punch, peak_time(punch), 0.012, 0.0),
        (ring, peak_time(ring), 0.014, -9.0),
        (low, THUD_PEAK, 0.03, -5.0),
    ], 0.5)
    save(f'WhirlwindHit_{i + 1:02d}', master(fade(mix, 0.001, 0.14), -4.0, -18.0,
         comp='acompressor=threshold=-14dB:ratio=2.5:attack=1:release=90:makeup=1.5'))

# ---------- Импульсы удержания: светлые взмахи владельца чуть ниже, пик на 0,08 с. ----------
for i, k in enumerate([1, 3, 5]):
    swing = fade(pitched(os.path.join(PELAG, f'Attack_{k:02d}.wav'), 0.92), 0.002, 0.10)
    low = fade(seg(thud_low, 0.0, 0.32)[::-1].copy(), 0.04, 0.02)
    mix = place([(swing, peak_time(swing), 0.08, 0.0), (low, len(low) / SR, 0.085, -16.0)], 0.5)
    save(f'WhirlwindPulse_{i + 1:02d}', master(fade(mix, 0.002, 0.12), -6.0, -22.0))

# ---------- Конец: убран. Слот WhirlwindEnd остаётся пустым — воздух после оборота не читался.
end = os.path.join(PELAG, 'WhirlwindEnd.wav')
if os.path.exists(end): os.remove(end)
if os.path.exists(end + '.meta'): os.remove(end + '.meta')
print('done')
