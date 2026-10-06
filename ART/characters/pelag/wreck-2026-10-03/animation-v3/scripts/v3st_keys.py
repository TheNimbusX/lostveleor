"""Крушение v3, Stow: якорь на спину после окна (8 кадров, кадр 0 = Wait1 кадр 0 = Swing1 кадр 12, кадр 7 = стойка серии сабли).

Игра убирает якорь сама (PelagAnchorRig.Modes.StowDirective, живая физика без запечки): ждёт, пока левая кисть клипа
подойдёт к креплению рукояти на спине (≤ 4 см; крепление — у правой стороны шеи сзади), сажает рукоять на спину и
сматывает цепь 5 м/с — голову тянет натяжение, корпус её не толкает; у крепления (≤ 10 см, ≤ 2,5 м/с) — сшивка в позу спины.
Клип: 0–3 рывок — кисти от левого бедра (поза 3) вверх через грудь к переду правого плеча, корпус доворачивает вправо; правая
отпускает цепь 1–3, левая 4–7 (руки — смесь позы кадра 3 и стойки), правая шагает назад в стойку (1–4), левая подтягивается (5–7).
Крепление рукояти — за шеей справа, в 0,48 м от левого плеча: левой кистью до него только сквозь голову и шею (проверено полюсами локтя
и креном головы), поэтому кисть останавливается у переда правого плеча (0,32 м от крепления) и риг идёт запасным путём:
ждёт 0,35 с, тянет рукоять на спину 0,22 с, потом намотка.
"""
import math
from s_lib import pchip
from wk_clips import SL, SR, rot
from wk_keys import step
import v3_keys

CLIP = "Pelag_AN_Wreck2_Stow"
N = 7
HAND = 3
SEAM = {0: ("Pelag_AN_Wreck2_Wait1", 0)}
HINT_FROM = ("Pelag_AN_Wreck2_Wait1", 0)
STRIKE = (0, 5)
RELEASE_FROM = HAND
FREE = {4: 0.25, 5: 0.50, 6: 0.75, 7: 1.0}
FREE_R = {1: 0.25, 2: 0.5, 3: 0.75}
ARM_INTERP = {"Left": (0, 3, {1: 0.30, 2: 0.66})}   # левая от бедра к креплению — смесью поз кадров 0 и 3 (без перескока IK)                 # правая отпускает цепь первой (рукоять на спину несёт левая)
SLA = (SL[0], SL[1], SL[2] + 0.002, SL[3], SL[4])
SL_LOCK = v3_keys.SL_LOCK
R_LAND = v3_keys.R_LAND
dz0, py0, cy0, le0, lo0, _ = v3_keys.KEYS[12]
KEYS = {
    0: (dz0, py0, cy0, le0, lo0, SL_LOCK, R_LAND),
    1: (-0.096, 18.0, 28.0, 12.0, 8.0, SL_LOCK, step(R_LAND, SR, 1 / 4, 0.06, 0.0)),
    2: (-0.086, 2.0, 4.0, 10.0, -2.0, SL_LOCK, step(R_LAND, SR, 2 / 4, 0.06, 0.0)),
    3: (-0.070, -14.0, -20.0, 9.0, -10.0, SL_LOCK, step(R_LAND, SR, 3 / 4, 0.06, 0.0)),   # левая у переда правого плеча (крепление недостижимо — запасной путь рига)
    4: (-0.050, -30.0, -32.0, 8.5, -10.0, SL_LOCK, SR),
    5: (-0.030, -41.0, -34.0, 8.0, -6.0, step(SL_LOCK, SLA, 1 / 3, 0.05, 0.0), SR),
    6: (-0.012, -48.0, -32.0, 7.0, -3.0, step(SL_LOCK, SLA, 2 / 3, 0.05, 0.0), SR),
    7: (0.0, -52.1, -30.0, 6.5, -0.4, SLA, SR),
}
P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
P_LIFT = ((0.55, -0.70, 0.10), (0.10, -0.85, -0.35))     # левая через грудь к правому плечу: локоть вперёд-вверх, мимо лица
LIFT_W = {0: 0.0, 1: 0.4, 2: 0.8, 3: 1.0}
AXW = 0.10
GAP = 0.15
FULL0 = False
# голова якоря по плану (ось кулаков в кадрах без запечки; настоящая голова — живая физика StowModel)
HEAD_PLAN = {0: (0.81, 0.19, 1.23), 1: (0.85, 0.5, 0.9), 2: (0.9, 1.4, 0.2), 3: (0.4, 1.9, -0.6), 7: (0.1, 1.0, -0.2)}
GUESS = [(-0.354, 0.923, 0.325), (-0.15, 1.04, 0.41), (0.10, 1.30, 0.25), (0.238, 1.365, -0.096), (0.21, 1.40, -0.12),
         (0.20, 1.38, 0.0), (0.20, 1.38, 0.0), (0.20, 1.38, 0.0)]


def _tab(t, f):
    xs = sorted(t)
    return pchip([(x, t[x]) for x in xs], f)


def body_at(f):
    fr = sorted(KEYS)
    if f in KEYS: k = KEYS[f]
    else:
        k = tuple(pchip([(x, KEYS[x][i]) for x in fr], f) for i in range(5)) + tuple(
            tuple(pchip([(x, KEYS[x][j][c]) for x in fr], f) for c in range(5)) for j in (5, 6))
    dz, pyaw, cyaw, lean, look, L, R = k
    return dict(W=1.0, dz=dz, pyaw=pyaw, cyaw=cyaw, lean=lean, look=look, gL=1.0, gR=1.0, feet=dict(Left=tuple(L), Right=tuple(R)))


def poles(f, p):
    w = _tab(LIFT_W, f) if f <= 3 else 1.0
    return {s: rot(tuple(x + (y - x) * w for x, y in zip(P_FRONT[i], P_LIFT[i])), p["cyaw"]) for i, s in enumerate(("Left", "Right"))}


def handle_dir(f, target, to_r):
    h = tuple(_tab({k: v[i] for k, v in HEAD_PLAN.items()}, f) for i in range(3))
    d = [a - b for a, b in zip(h, target)]
    n = math.sqrt(sum(c * c for c in d))
    return [c / n for c in d]

