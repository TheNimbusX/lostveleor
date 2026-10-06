"""Крушение v2: проверочная модель кистеня для клипов (не запечка игры — её делает tools/anchorbake по
artifacts/anchor-core/DESIGN.md). Точечная голова 12 кг на тросе постоянной длины от кольца рукояти:
трос-неравенство |голова − кольцо| ≤ R, R = цепь 1,60 + кольцо→центр головы 0,38; g = 9,81; земля — центр головы
не ниже HEAD_FLOOR; воздух v *= exp(−0,6·dt). Шаг 1/240 с, хват между кадрами — кубический Эрмит (как шаг B §3.2).
Оси — корень (f вперёд, l влево, u вверх), метры. Без bpy: импортируется и из Blender, и из python.
"""
import math

FPS, SUB = 30, 8                 # 8 подшагов на тик = 240 Гц
CHAIN, EYE = 1.60, 0.38
R = CHAIN + EYE
G = 9.81
HEAD_FLOOR = 0.30                # центр головы над землёй, когда рога/тулья касаются
DRAG = 0.6


def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def norm(a): return math.sqrt(dot(a, a))
def unit(a):
    n = norm(a)
    return mul(a, 1.0 / n) if n > 1e-9 else (0.0, 0.0, -1.0)


def tangents(track):
    """Касательные Эрмита (м/кадр) по центральной разности; на концах — односторонняя."""
    n = len(track); out = []
    for i in range(n):
        a = track[max(0, i - 1)]; b = track[min(n - 1, i + 1)]
        span = (min(n - 1, i + 1) - max(0, i - 1)) or 1
        out.append(mul(sub(b, a), 1.0 / span))
    return out


def hermite(track, tan, x):
    """Точка хвата в дробном кадре x."""
    n = len(track)
    if x <= 0: return track[0], mul(tan[0], FPS)
    if x >= n - 1: return track[-1], mul(tan[-1], FPS)
    i = int(math.floor(x)); t = x - i
    p0, p1, m0, m1 = track[i], track[i + 1], tan[i], tan[i + 1]
    h00 = 2 * t ** 3 - 3 * t ** 2 + 1; h10 = t ** 3 - 2 * t ** 2 + t; h01 = -2 * t ** 3 + 3 * t ** 2; h11 = t ** 3 - t ** 2
    p = add(add(mul(p0, h00), mul(m0, h10)), add(mul(p1, h01), mul(m1, h11)))
    d00 = 6 * t ** 2 - 6 * t; d10 = 3 * t ** 2 - 4 * t + 1; d01 = -6 * t ** 2 + 6 * t; d11 = 3 * t ** 2 - 2 * t
    v = add(add(mul(p0, d00), mul(m0, d10)), add(mul(p1, d01), mul(m1, d11)))
    return p, mul(v, FPS)


def simulate(track, head0, vel0=(0.0, 0.0, 0.0), held_until=-1.0, held_track=None, frames=None):
    """track — кольцо по кадрам; head0/vel0 — голова в кадре 0; held_until — до этого кадра голова привязана к
    held_track (спина), дальше свободна. Возвращает по 8 сэмплов на кадр: (x, p, v, taut, tension)."""
    tan = tangents(track)
    htan = tangents(held_track) if held_track else None
    n = (frames if frames is not None else len(track) - 1)
    dt = 1.0 / (FPS * SUB)
    p, v = head0, vel0
    out = [(0.0, p, v, False, 0.0)]
    for k in range(1, n * SUB + 1):
        x = k / SUB
        if x <= held_until + 1e-9 and held_track:
            q, qv = hermite(held_track, htan, x)
            v = sub(q, p); v = mul(v, 1.0 / dt); p = q
            out.append((x, p, v, False, 0.0)); continue
        v = add(v, (0.0, 0.0, -G * dt))
        v = mul(v, math.exp(-DRAG * dt))
        pn = add(p, mul(v, dt))
        ring, rv = hermite(track, tan, x)
        d = sub(pn, ring); dist = norm(d)
        taut, tension = False, 0.0
        if dist > R:
            u = mul(d, 1.0 / dist)
            pn = add(ring, mul(u, R))
            vr = dot(sub(v, rv), u)
            if vr > 0:
                v = sub(v, mul(u, vr))
                tension = 12.0 * vr / dt
            taut = True
        if pn[2] < HEAD_FLOOR:
            pn = (pn[0], pn[1], HEAD_FLOOR)
            if v[2] < 0: v = (v[0] * 0.35, v[1] * 0.35, -v[2] * 0.04)
        p = pn
        out.append((x, p, v, taut, tension))
    return out


def at_frame(samples, f):
    best = min(samples, key=lambda s: abs(s[0] - f))
    return best


def contact_report(samples, f, direction=(1.0, 0.0, 0.0)):
    x, p, v, taut, ten = at_frame(samples, f)
    hr = math.hypot(p[0], p[1])
    ang = math.degrees(math.atan2(p[1], p[0]) - math.atan2(direction[1], direction[0]))
    return dict(frame=f, head=[round(c, 3) for c in p], radius=round(hr, 3), height=round(p[2], 3), angle=round(ang, 1),
                speed=round(norm(v), 2), vel=[round(c, 2) for c in v], taut=taut)


def grip_speed(track):
    """Скорость (м/с) и ускорение (м/с²) хвата по кадрам (центральные разности)."""
    sp, ac = [], []
    for i in range(1, len(track) - 1):
        v = mul(sub(track[i + 1], track[i - 1]), FPS / 2)
        a = mul(add(sub(track[i + 1], track[i]), sub(track[i - 1], track[i])), FPS * FPS)
        sp.append(round(norm(v), 2)); ac.append(round(norm(a), 1))
    return sp, ac
