"""Tiny key-curve helper for procedural takes (pure Python, no bpy).

track([(frame, value, ease), ...])(t) interpolates scalars or tuples. `ease` shapes the segment
that ENDS at that key: 'io' smooth (default), 'in' accelerate, 'out' decelerate, 'lin', 'snap'
(very fast accelerate, for claps/snaps), 'hold' (keeps the previous value until the key).
"""
import math


def _ease(kind, s):
    if kind == "lin":
        return s
    if kind == "in":
        return s * s
    if kind == "snap":
        return s * s * s
    if kind == "out":
        return 1 - (1 - s) * (1 - s)
    if kind == "hold":
        return 0.0 if s < 1 else 1.0
    return s * s * (3 - 2 * s)


def _mix(a, b, w):
    if isinstance(a, (tuple, list)):
        return tuple(x + (y - x) * w for x, y in zip(a, b))
    return a + (b - a) * w


def track(keys):
    keys = [k if len(k) == 3 else (k[0], k[1], "io") for k in keys]

    def at(t):
        if t <= keys[0][0]:
            return keys[0][1]
        for (f0, v0, _), (f1, v1, e) in zip(keys, keys[1:]):
            if t <= f1:
                return _mix(v0, v1, _ease(e, (t - f0) / (f1 - f0)))
        return keys[-1][1]

    return at


def smoothstep(s):
    s = min(1.0, max(0.0, s))
    return s * s * (3 - 2 * s)


def wave(t, period, phase=0.0):
    return math.sin(2 * math.pi * (t / period + phase))
