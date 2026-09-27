"""Shared take helpers: vector math on tuples, the Idle pose function, key builder."""
import math

from mathutils import Vector


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def scale(a, k):
    return tuple(x * k for x in a)


def norm(a):
    v = Vector(a).normalized()
    return tuple(v)


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


IDLE_FRAMES = 60


def idle(D, f):
    """Idle pose at frame f (period 60). Soft knees, breathing, slow look, spikes drifting."""
    w = math.tau * f / IDLE_FRAMES
    s, c, s2 = math.sin(w), math.cos(w), math.sin(2 * w)
    p = dict(D)
    p["hip"] = (0.012 * s, 0.004 * s2, -0.04 + 0.008 * math.cos(2 * w))
    p["hip_rot"] = (2.0 + 0.8 * math.sin(w + 0.5), 1.2 * s, 1.0 * math.sin(w + 0.8))
    p["spine"] = (3.0 + 1.6 * math.sin(2 * w - 0.6), -1.2 * s, 1.5 * math.sin(w + 1.0))
    p["neck"] = (1.0 * math.sin(2 * w + 0.2), 0.0, 1.5 * math.sin(w + 0.6))
    p["head"] = (-3.0 + 1.8 * math.sin(2 * w + 0.9), 1.5 * math.sin(w + 2.0), 5.0 * math.sin(w + 0.3))
    for side, sg, ph in (("L", 1, 0.0), ("R", -1, 1.7)):
        p[side + "_tip"] = add(D[side + "_tip"], (-0.05 * sg + 0.02 * sg * math.sin(w + ph), -0.07 + 0.03 * math.sin(w + 0.4 + ph),
                                                  0.04 + 0.015 * math.sin(2 * w + ph)))
        p[side + "_dir"] = norm(add(D[side + "_dir"], (0.05 * sg * math.sin(w + ph), -0.12 + 0.06 * math.sin(w + 1 + ph), 0.0)))
        p[side + "_shrug"] = 2.0 * math.sin(2 * w + ph)
    return p


def K(base, f, mode="auto", **over):
    """Key = base params updated with overrides."""
    p = dict(base)
    p.update(over)
    return (f, p, mode)
