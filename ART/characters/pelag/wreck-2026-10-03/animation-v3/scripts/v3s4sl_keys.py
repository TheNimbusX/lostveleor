"""Крушение v3, Slam ОТ НАСТОЯЩЕГО КОНЦА Swing2 (решение 06.10): кадр 0 = Swing2 кадр C2+1 (контакт маха 2 + 1 тик).

Голова якоря после второго маха летит влево (≈ 29 м/с, впереди-слева) — она уходит дальше влево и вверх, за спину высоко,
через верх и вниз в точку 2,2 м впереди. Корпус после маха скручен влево (таз +24°, грудь +42°) — раскручивается обратно
к цели, кисти поднимаются через левое плечо над головой (поза 5, кадр OVERHEAD), рывок вниз-вперёд — выпад на левую
(поза 6, КОНТАКТ, наклон ~35°), удержание 3 кадра, выход 8 кадров в стойку серии сабли (как прежний Slam 20→27).
Ноги: левая стоит на месте выпада (как в Swing2); правая с R_LAND (у правого бедра после маха 2) шагает назад в упор выпада
(R_BACK) за время подъёма, к удару — на носок; в выходе правая к стойке, левая подтягивается.
Тики: CONTACT/OVERHEAD — V3S4SL_C / V3S4SL_O (поиск самого раннего контакта), C2 — V3S4SL_C2 (контакт Swing2).
Кадр 0 — копия ключей .blend Swing2 (V3S4SL_SEAM=1) или ключ, равный Swing2@C2+1 (ключи Swing1 кадр 8, правая R_LAND).
"""
import math, os
from s_lib import pchip
from wk_clips import SL, SR, on_toe, rot
from wk_keys import step
import v3_keys

CLIP = "Pelag_AN_Wreck2_Slam"
C2 = int(os.environ.get("V3S4SL_C2", "14"))
CONTACT = int(os.environ.get("V3S4SL_C", "13"))
OVERHEAD = int(os.environ.get("V3S4SL_O") or 7)
HOLD = 3
N = CONTACT + HOLD + 8
STRIKE = (0, CONTACT + 1)
SEAM = {0: ("Pelag_AN_Wreck2_Swing2", C2 + 1)} if os.environ.get("V3S4SL_SEAM") else {}
HINT_FROM = ("Pelag_AN_Wreck2_Swing1", 8)
FULL0 = not SEAM

SLW = v3_keys.SLW
SL_LOCK = v3_keys.SL_LOCK
R_LAND = v3_keys.R_LAND
R_BACK = (-0.36, -0.16, 0.136, -62.0, 0.0)            # правая назад: упор выпада
SLA = (SL[0], SL[1], SL[2] + 0.002, SL[3], SL[4])
K8 = v3_keys.KEYS[8]


def rToe(pitch, dyaw=0.0):
    return on_toe("Right", R_BACK, pitch, dyaw)


O, C = OVERHEAD, CONTACT
RS = int(os.environ.get("V3S4SL_RS0", "0")), int(os.environ.get("V3S4SL_RS1") or max(3, O - 2))                                  # шаг правой назад: кадры RS[0]..RS[1] (с первого кадра, растянут)
R14 = rToe(14)
RT0 = float(os.environ.get("V3S4SL_RTOE0", "14"))      # правая встаёт назад сразу на подушечку (°), 0 — на всю стопу
RB0 = rToe(RT0) if RT0 > 0 else R_BACK
RLIFT = float(os.environ.get("V3S4SL_RLIFT", "0.09"))
# кадр: (dz, pyaw, cyaw, lean, look)
T = {0: (K8[0], K8[1], K8[2], K8[3], K8[4]),
     O: (0.050, -14.0, -14.0, 2.0, -10.0), # грудь чуть вправо — левое плечо ближе к хвату;  # подъём: таз +5 см, ключицы вверх — кисти достают хват над головой;               # поза 5: кисти над головой, грудь открыта к цели
     C - 2: (-0.100, -4.0, 2.0, 24.0, 0.0),
     C: (-0.124, -2.0, 4.0, 35.0, 2.0),                # поза 6: КОНТАКТ
     C + 1: (-0.127, -2.0, 4.0, 37.0, 2.0), C + 2: (-0.127, -2.0, 4.0, 37.0, 2.0), C + 3: (-0.125, -3.0, 3.0, 36.0, 1.0),
     C + 4: (-0.120, -8.0, -6.0, 32.0, -3.0), C + 5: (-0.106, -16.0, -14.0, 28.0, -6.0), C + 6: (-0.092, -26.0, -20.0, 22.0, -8.0),
     C + 7: (-0.078, -36.0, -26.0, 17.0, -9.0), C + 8: (-0.062, -44.0, -30.0, 13.0, -7.0), C + 9: (-0.042, -48.0, -31.0, 10.0, -5.0),
     C + 10: (-0.020, -51.0, -30.5, 8.0, -2.0), C + 11: (0.0, -52.1, -30.0, 6.5, -0.4)}
if O >= 3: T[max(1, O // 2)] = (-0.040, 10.0, 18.0, 10.0, 0.0)
for _k, _e in (("O", O), ("OH", max(1, O // 2)), ("O1", O + 1), ("OM2", O - 2), ("OM1", O - 1)):
    if os.environ.get("V3S4SL_T" + _k): T[_e] = tuple(float(x) for x in os.environ["V3S4SL_T" + _k].split(","))


def feet(f):
    if f <= RS[0] or f == 0: R = R_LAND
    elif f <= RS[1]: R = step(R_LAND, RB0, (f - RS[0]) / max(1, RS[1] - RS[0]), RLIFT, 0.0, (0.05, 0.95))
    elif f <= C - 4: R = RB0
    elif f <= C + 3: R = rToe(min(22.0, RT0 + (22.0 - RT0) * (f - (C - 4)) / 4.0))
    elif f == C + 4: R = R14
    elif f <= C + 8: R = step(R14, SR, (f - (C + 4)) / 4.0, 0.06, 0.0)
    else: R = SR
    if f <= C + 8: L = SL_LOCK
    else: L = step(SL_LOCK, SLA, (f - (C + 8)) / 3.0, 0.05, 0.0)
    return L, R


def _tab(t, f):
    xs = sorted(t)
    return pchip([(x, t[x]) for x in xs], f)


def body_at(f):
    ks = sorted(T)
    v = [pchip([(k, T[k][i]) for k in ks], f) for i in range(5)]
    L, R = feet(f)
    return dict(W=1.0, dz=v[0], pyaw=v[1], cyaw=v[2], lean=v[3], look=v[4], gL=1.0, gR=1.0, feet=dict(Left=tuple(L), Right=tuple(R)))


P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
P_OVER = tuple(tuple(float(x) for x in v.split(",")) for v in __import__("os").environ.get("V3S4SL_POVER", "0.20,0.95,0.25;0.20,-0.95,0.25").split(";"))
P_HIP = ((0.70, 0.35, -0.40), (-0.05, -0.72, -0.69))     # выход: кисти к правому бедру — левый локоть вперёд-наружу (предплечье мимо живота)
P_OUT = ((0.25, 0.95, 0.05), (-0.05, -0.72, -0.69))
POLE_W = {0: 0.0, max(1, O - 3): 0.6, O: 1.0, C - 2: 0.8, C: 0.0}
OUT_W = {C - 3: 0.0, C - 1: 0.8, C: 1.0, C + 3: 1.0, C + 5: 0.0}
HIP_W = {C + 3: 0.0, C + 6: 0.6, C + 8: 1.0, N: 1.0}
AXW = 0.30
P_RDOWN = tuple(float(x) for x in os.environ.get("V3S4SL_PRD", "0.35,-0.45,-0.82").split(","))
RDOWN_W = float(os.environ.get("V3S4SL_RDW", "0.5"))
UPFWD = tuple(float(x) for x in os.environ.get("V3S4SL_UPFWD", "0.60,0.55,-0.15").split(","))                           # ось рукояти над головой: вверх-вправо (правая справа от левой, не через лицо)
_OW = float(os.environ.get("V3S4SL_OVERW", "1.0"))
_OS = int(os.environ.get("V3S4SL_OWS", "4"))
OVER_W = {max(0, O - _OS): 0.0, O - 1: _OW, O + 1: _OW, min(C - 1, O + 3): 0.0}
GAP = 0.15
GAP_AT = {int(k): float(v) for k, v in (x.split(":") for x in os.environ.get("V3S4SL_GAPAT", "3:0.12,4:0.12").split(",") if x)}
SMOOTH = (("RightShoulder", "RightArm", "RightForeArm", "RightHand"), 1, 0.35)


def handle_over(f):
    lo, hi = min(OVER_W), max(OVER_W)
    return max(0.0, min(1.0, _tab(OVER_W, f))) if lo <= f <= hi else 0.0


CLAV_UP = tuple(float(x) for x in os.environ.get("V3S4SL_CLAV", "0.6,50").split(","))   # ключица над головой: (доля, предел °)
CLAV_W = {max(0, O - 3): 0.0, O - 1: 1.0, O + 1: 1.0, min(C - 1, O + 3): 0.0}


def poles(f, p):
    import wk_grip
    cw = max(0.0, min(1.0, _tab(CLAV_W, f))) if min(CLAV_W) <= f <= max(CLAV_W) else 0.0
    wk_grip.CLAV_K = 0.25 + (CLAV_UP[0] - 0.25) * cw; wk_grip.CLAV_MAX = 20.0 + (CLAV_UP[1] - 20.0) * cw
    import v3_arms
    v3_arms.GAP = GAP_AT.get(f, GAP)             # подъём через левое плечо: правый кулак ближе к левому — мимо лица
    w = max(0.0, min(1.0, _tab(POLE_W, f))) if f <= C else 0.0
    h = max(0.0, min(1.0, _tab(HIP_W, f))) if f >= C + 3 else 0.0
    o = max(0.0, min(1.0, _tab(OUT_W, f))) if C - 3 <= f <= C + 5 else 0.0
    out = {}
    for i, s in enumerate(("Left", "Right")):
        a = tuple(x + (y - x) * w for x, y in zip(P_FRONT[i], P_OVER[i]))
        a = tuple(x + (y - x) * h for x, y in zip(a, P_HIP[i]))
        a = tuple(x + (y - x) * o for x, y in zip(a, P_OUT[i]))
        if s == "Right" and O < f < C:          # рывок вниз: правый локоть вниз-вперёд — предплечье не ложится на цепь, идущую к голове якоря
            d = RDOWN_W * math.sin(math.pi * (f - O) / (C - O))
            a = tuple(x + (y - x) * d for x, y in zip(a, P_RDOWN))
        out[s] = rot(a, p["cyaw"])
    return out


def axw(f):
    return AXW if f <= C + 1 else 0.08


def guess(g0):
    """Путь хвата для первого тела (до запечки): от хвата Swing2@C2+1 вверх через левое плечо над голову, рывок вниз-вперёд,
    выход к правому бедру. Оси корня Unity."""
    top = (0.02, 1.86, 0.22); hit = (0.0, 0.78, 0.56); ex = (0.24, 0.96, 0.30)
    mid = {"L": (-0.30, 1.55, 0.25), "R": (0.22, 1.50, 0.35), "F": (-0.05, 1.50, 0.45)}[os.environ.get("V3S4SL_GUESS", "L")]
    pts = []
    for f in range(N + 1):
        if f <= O:
            u = f / O
            k = [(0, g0), (0.5, mid), (1.0, top)]
        elif f <= C:
            u = (f - O) / (C - O); k = [(0, top), (0.5, (0.04, 1.35, 0.55)), (1.0, hit)]
        else:
            u = min(1.0, (f - C) / 8.0); k = [(0, hit), (1.0, ex)]
        pts.append(tuple(pchip([(a, b[i]) for a, b in k], u) for i in range(3)))
    return pts


HEAD_PLAN = None


def handle_dir(f, target, to_r):
    """Ось кулаков без запечки (первое тело): от кулака вверх-назад-влево до над головой, потом вниз-вперёд."""
    if f <= O: d = (-0.6, 0.3 + 0.6 * f / max(1, O), -0.5)
    elif f <= C: u = (f - O) / max(1, C - O); d = (-0.2 * (1 - u), 0.8 - 1.4 * u, -0.4 + 1.3 * u)
    else: d = (0.5, -0.5, 0.6)
    n = math.sqrt(sum(c * c for c in d))
    return [c / n for c in d]
