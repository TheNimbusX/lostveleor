"""Крушение v3 · Девятый вал, ChargeRelease: выход из «вертолёта» в удар оземь (поза 9) и в выход базового Slam (поза 7 → стойка).

Кадр 0 = ChargeLoop кадр 0 (фаза 0: голова якоря сзади-справа, как Slam@7 «над головой»; копия ключей .blend петли).
Спуск 0→6 повторяет путь базового Slam 7→13 (голова сзади → справа → вниз в точку 2,2 м): контакт в кадре 6 = отпускание
(последний тик удержания) + 6 тиков спуска Sim (WreckSlamDownTicks). Поза 9: цепь на всю длину, выпад шире и ниже (таз −0,13,
наклон 38°), правая сзади на носке (колено к земле). После контакта кадр 6 + k = Slam 13 + k: удержание 7–9, выход 10–17,
кадры 14–17 — копии Slam 21–24 (кадр 17 = стойка серии сабли, как Slam@24).
Ноги: левая стоит (место выпада, как в петле и в Slam), правая сзади — с полной стопы петли на носок к удару; дальше — как Slam.
Руки: левое гнездо — на пути хвата gripopt (--slam от состояния петли@0), ось кулаков от оси рукояти петли к цепи за 3 кадра.
"""
import math, os
from s_lib import pchip
from wk_clips import on_toe
import v3s5nw_loop_keys as LK
import v3s4sl_keys as SK

CLIP = "Pelag_AN_Wreck2_ChargeRelease"
CONTACT = 6
SLAM_SHIFT = SK.CONTACT - CONTACT          # кадр Slam = кадр выхода + 7 (контакт 6 ↔ 13)
N = CONTACT + 3 + 8                        # 17
SEAM = {0: ("Pelag_AN_Wreck2_ChargeLoop", 0)}
SEAM.update({f: ("Pelag_AN_Wreck2_Slam", f + SLAM_SHIFT) for f in range(int(os.environ.get("V3S5NW_RCOPY", "14")), N + 1)})
HINT_FROM = ("Pelag_AN_Wreck2_ChargeLoop", 0)
STRIKE = (0, CONTACT + 1)                  # руки ≤ 70°/тик: спуск и удар
_RC = int(os.environ.get("V3S5NW_RCOPY", "14"))
ARM_INTERP = {"Right": (_RC - 5, _RC, {_RC - 4: .2, _RC - 3: .4, _RC - 2: .6, _RC - 1: .8})}   # правая к копии Slam — смесью поворотов

_L0 = LK.body_at(0)
E = lambda k, d: float(os.environ.get("V3S5NW_R" + k, d))
C_DZ, C_LEAN, C_PY, C_CY = E("DZ", -0.130), E("LEAN", 38.0), E("PY", -4.0), E("CY", 2.0)
T = {0: (_L0["dz"], _L0["pyaw"], _L0["cyaw"], _L0["lean"], _L0["look"]),   # петля@0: корпус довёрнут вправо за кулаками
     1: (-0.110, -30.0, -26.0, 7.0, -20.0),                           # раскрутка корпуса к цели (≤ 15°/тик), наклон растёт
     2: (-0.115, -18.0, -12.0, 13.0, -10.0),
     3: (-0.120, -10.0, -5.0, 19.0, -4.0),
     4: (-0.125, -6.0, -1.0, 26.0, -2.0),
     CONTACT - 1: (-0.128, -4.0, 1.0, 32.0, 0.0),
     CONTACT: (C_DZ, C_PY, C_CY, C_LEAN, 2.0)}                       # поза 9: удар, выпад ниже и шире
for k in range(1, 8):                                                # удержание и выход — ключи Slam после его контакта
    T[CONTACT + k] = SK.T[SK.CONTACT + k]
RT_C = E("TOE", 26.0)                                                # правая на носке к удару (°)


def _tab(t, f):
    xs = sorted(t)
    return pchip([(x, t[x]) for x in xs], f)


def feet(f):
    if f >= CONTACT + 1:
        return SK.feet(f + SLAM_SHIFT)
    u = max(0.0, min(1.0, f / CONTACT)); w = u * u * (3 - 2 * u)
    R = on_toe("Right", LK.R_BACK, RT_C * w) if w > 1e-4 else LK.R_BACK
    return LK.FEET["Left"], R


def body_at(f):
    ks = sorted(T)
    v = [pchip([(k, T[k][i]) for k in ks], f) for i in range(5)]
    L, R = feet(f)
    return dict(W=1.0, dz=v[0], pyaw=v[1], cyaw=v[2], lean=v[3], look=v[4], gL=1.0, gR=1.0, feet=dict(Left=tuple(L), Right=tuple(R)))


def poles(f, p):
    """Как Slam от «над головой» (кадр 7 Slam = кадр 0 выхода): локти над головой → вниз-вперёд к удару → к бедру в выходе."""
    if f <= 2:      # первые кадры — локти петли (в стороны и вперёд), смесью к локтям Slam
        a = LK.poles(f, p); b = SK.poles(f + SLAM_SHIFT, p); w = f / 2.0
        return {s: tuple(x + (y - x) * w for x, y in zip(a[s], b[s])) for s in ("Left", "Right")}
    return SK.poles(f + SLAM_SHIFT, p)


def axw(f):
    return 0.30 if f <= CONTACT + 1 else 0.08


AXW = 0.30
GAP = 0.15
SMOOTH = SK.SMOOTH
UPFWD = SK.UPFWD


def handle_over(f):
    return 0.0


def fist_axis(f, target, ring):
    """Ось кулаков: в кадре 0 — ось рукояти петли, к кадру 3 — по цепи к голове (как Slam при спуске)."""
    a = LK.fist_axis(0, target, ring)
    d = [r - t for r, t in zip(ring, target)]; n = math.sqrt(sum(c * c for c in d)) or 1.0; d = [c / n for c in d]
    u = max(0.0, min(1.0, f / 3.0)); w = u * u * (3 - 2 * u)
    m = [x + (y - x) * w for x, y in zip(a, d)]; n = math.sqrt(sum(c * c for c in m)) or 1.0
    return [c / n for c in m]


def handle_dir(f, target, to_r):
    """Без запечки (первое тело): к голове якоря по плану Slam — вниз-вперёд."""
    return SK.handle_dir(f + SLAM_SHIFT, target, to_r)
