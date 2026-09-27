"""Key-pose timeline helpers: eased blends between pose dicts + deterministic tremble noise."""
import math
import stance


def ease(t, kind):
    t = max(0.0, min(1.0, t))
    if kind == "lin":
        return t
    if kind == "in":        # accelerate (falls, slams)
        return t * t
    if kind == "in3":
        return t * t * t
    if kind == "out":       # decelerate (recoils, settles)
        return 1 - (1 - t) * (1 - t)
    if kind == "hold":
        return 0.0
    return t * t * (3 - 2 * t)  # smooth


def at(keys, f):
    """keys: [(frame, pose, ease_into_this_key)], sorted. Returns the blended pose at frame f."""
    if f <= keys[0][0]:
        return stance.blend(keys[0][1], keys[0][1], 0)
    for (f0, a, _), (f1, b, kind) in zip(keys, keys[1:]):
        if f <= f1:
            return stance.blend(a, b, ease((f - f0) / (f1 - f0), kind))
    return stance.blend(keys[-1][1], keys[-1][1], 0)


def wobble(f, seed, freqs=(0.93, 1.71, 2.63)):
    """Smooth pseudo-noise in [-1, 1]; freqs in cycles per 30 frames (1 s)."""
    return sum(math.sin(2 * math.pi * fr * f / 30.0 + seed * (i + 1) * 1.7) for i, fr in enumerate(freqs)) / len(freqs)


def tremble(f, seed):
    """Fast muscle shake for the pinned strain (about 6-9 Hz)."""
    return wobble(f, seed, (6.1, 7.7, 9.3))
