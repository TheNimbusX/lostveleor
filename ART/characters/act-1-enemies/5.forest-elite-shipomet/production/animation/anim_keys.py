"""Key interpolation for pose parameters (floats or tuples).

keys: list of (frame, params_dict, mode).  mode is how the segment ARRIVING at
that key moves:
  'auto'  cubic Hermite, Catmull-Rom tangents clamped to 0 at extremes (like Blender AUTO_CLAMPED)
  'in'    accelerate into the key (t^2), for impacts/contacts
  'in3'   harder accelerate (t^3)
  'out'   decelerate into the key (1-(1-t)^2), for snaps out of a hit
  'lin'   linear
  'hold'  keep previous key's values until this key, then jump (rarely used)
Unspecified params in a key fall back to the defaults given to Track.
"""
import math

EASE = {
    "lin": lambda t: t,
    "in": lambda t: t * t,
    "in3": lambda t: t * t * t,
    "out": lambda t: 1 - (1 - t) ** 2,
    "out3": lambda t: 1 - (1 - t) ** 3,
    "smooth": lambda t: t * t * (3 - 2 * t),
}


def _as_list(v):
    return list(v) if isinstance(v, (tuple, list)) else [v]


class Track:
    def __init__(self, keys, defaults, dirs=()):
        self.keys = sorted(keys, key=lambda k: k[0])
        self.frames = [k[0] for k in self.keys]
        self.dirs = set(dirs)
        self.names = list(defaults)
        self.vals = []
        for f, p, mode in self.keys:
            full = dict(defaults)
            full.update(p)
            self.vals.append({n: _as_list(full[n]) for n in self.names})
        self.scalar = {n: not isinstance(defaults[n], (tuple, list)) for n in self.names}
        self.modes = [k[2] for k in self.keys]

    def _tangent(self, n, k, c):
        fr, vs = self.frames, self.vals
        if k == 0 or k == len(fr) - 1:
            return 0.0
        a, b, m = vs[k - 1][n][c], vs[k + 1][n][c], vs[k][n][c]
        if (m >= a and m >= b) or (m <= a and m <= b):
            return 0.0
        # only smooth through a key when both neighbouring segments are 'auto'
        if self.modes[k] != "auto" or self.modes[k + 1] != "auto":
            return 0.0
        t = (b - a) / (fr[k + 1] - fr[k - 1])
        # clamp so the curve cannot overshoot either neighbour
        lim = 3 * min(abs(m - a) / (fr[k] - fr[k - 1]), abs(b - m) / (fr[k + 1] - fr[k]))
        return max(-lim, min(lim, t))

    def __call__(self, f):
        fr = self.frames
        if f <= fr[0]:
            return self._pack(self.vals[0])
        if f >= fr[-1]:
            return self._pack(self.vals[-1])
        k = max(i for i in range(len(fr)) if fr[i] <= f)
        f0, f1 = fr[k], fr[k + 1]
        h = f1 - f0
        t = (f - f0) / h
        mode = self.modes[k + 1]
        out = {}
        for n in self.names:
            p0, p1 = self.vals[k][n], self.vals[k + 1][n]
            if mode == "auto":
                h00, h10, h01, h11 = 2 * t ** 3 - 3 * t ** 2 + 1, t ** 3 - 2 * t ** 2 + t, -2 * t ** 3 + 3 * t ** 2, t ** 3 - t ** 2
                out[n] = [h00 * p0[c] + h10 * h * self._tangent(n, k, c) + h01 * p1[c] + h11 * h * self._tangent(n, k + 1, c)
                          for c in range(len(p0))]
            elif mode == "hold":
                out[n] = list(p0)
            else:
                e = EASE[mode](t)
                out[n] = [p0[c] + (p1[c] - p0[c]) * e for c in range(len(p0))]
        return self._pack(out)

    def _pack(self, vals):
        out = {}
        for n in self.names:
            v = vals[n]
            if self.scalar[n]:
                out[n] = v[0]
            else:
                if n in self.dirs:
                    l = math.sqrt(sum(x * x for x in v)) or 1.0
                    v = [x / l for x in v]
                out[n] = tuple(v)
        return out
