"""Крушение v3, Stow НА НОВОЕ КРЕПЛЕНИЕ (06.10): рукоять вешается за правую лопатку (ниже прежнего «за шеей справа»).

Кадр 0 = Wait1 кадр 0 (= Swing1 кадр 12; Wait2 кадр 0 — та же поза тела, вход блендом 2 кадра). Правая отпускает цепь 1–3.
Левая с рукоятью: 1 — перед левой грудью, 2 — над левым плечом (локоть вверх-наружу), 3 — за затылком, HAND — за правой
лопаткой у крепления (≤ 4 см: игра сажает рукоять, мотает цепь 5 м/с, ловит голову). Так рука идёт как в «пробе Эпли»
(кисть через верх за спину к противоположной лопатке) — без головы и шеи на пути (замер v3s4_mount_probe.py).
После HAND левая отпускает (смесь позы кадра HAND и стойки), ноги — в стойку серии сабли, как прежний Stow.
Точки пути левой — в осях груди (вправо, вверх, назад от Spine2, м): STOW_PTS; путь в осях корня пишет v3s4st_path.py.
"""
import math
from s_lib import pchip
from wk_clips import SL, SR, rot
from wk_keys import step
import v3_keys

CLIP = "Pelag_AN_Wreck2_Stow"
HAND = 6
D = int(__import__("os").environ.get("V3S4ST_DELAY", "6"))   # корпус ждёт поимку дольше: возврат в стойку позже на D кадров
N = HAND + 19 + D
SEAM = {0: ("Pelag_AN_Wreck2_Wait1", 0)}
HINT_FROM = ("Pelag_AN_Wreck2_Wait1", 0)
STRIKE = (0, HAND + 1)
RELEASE_FROM = HAND + 5
FREE = {HAND + 6: 0.12, HAND + 7: 0.25, HAND + 8: 0.38, HAND + 9: 0.5, HAND + 10: 0.62, HAND + 11: 0.72, HAND + 12: 0.8, HAND + 13: 0.86, HAND + 14: 0.9, HAND + 15: 0.93, HAND + 16: 0.96, HAND + 17: 0.98, HAND + 18: 0.99, HAND + 19: 1.0}
FREE.update({f: 1.0 for f in range(HAND + 20, N + 1)})
FREE_R = {3: 0.3, 4: 0.6, 5: 0.9, **{f: 1.0 for f in range(6, N + 1)}}
OFF_GRIP = tuple(range(HAND + 1, HAND + 6))
MOUNT_CHEST = (0.12, 0.02, 0.175)                       # крепление: вправо, вверх, назад от Spine2 (оси груди, м)
STOW_PTS = {1: (-0.30, 0.05, -0.40), 2: (-0.34, 0.30, -0.26), 3: (-0.27, 0.48, 0.0), 4: (-0.09, 0.50, 0.28), 5: (0.08, 0.26, 0.28), HAND: MOUNT_CHEST,
            HAND + 1: (0.05, 0.34, 0.31), HAND + 2: (-0.04, 0.48, 0.33), HAND + 3: (-0.15, 0.53, 0.20),
            HAND + 4: (-0.26, 0.51, 0.08), HAND + 5: (-0.33, 0.43, -0.02), HAND + 6: (-0.37, 0.31, -0.08)}   # после HAND — кисть пустая, назад через плечо
SLA = (SL[0], SL[1], SL[2] + 0.002, SL[3], SL[4])
SL_LOCK = v3_keys.SL_LOCK
R_LAND = v3_keys.R_LAND
dz0, py0, cy0, le0, lo0, _ = v3_keys.KEYS[12]
KEYS = {   # корпус доворачивает ВЛЕВО, правая лопатка (крепление) смотрит на якорь справа-впереди: намотка идёт к спине, не сквозь корпус
    0: (dz0, py0, cy0, le0, lo0, SL_LOCK, R_LAND),
    1: (-0.094, 27.0, 47.0, 11.0, 12.0, SL_LOCK, R_LAND),
    2: (-0.088, 29.0, 52.0, 10.0, 18.0, SL_LOCK, R_LAND),
    3: (-0.082, 36.0, 64.0, 9.5, 29.0, SL_LOCK, R_LAND),
    4: (-0.076, 41.0, 77.0, 9.0, 38.0, SL_LOCK, R_LAND),
    5: (-0.072, 44.0, 80.0, 8.5, 40.0, SL_LOCK, R_LAND),
    HAND: (-0.070, 46.0, 82.0, 8.0, 41.0, SL_LOCK, R_LAND),
    HAND + 4: (-0.066, 47.0, 83.0, 8.0, 42.0, SL_LOCK, R_LAND),
    HAND + 8: (-0.062, 46.0, 82.0, 8.0, 41.0, SL_LOCK, R_LAND),
    HAND + 12: (-0.056, 40.0, 77.0, 8.0, 38.0, SL_LOCK, R_LAND),       # якорь пойман (≈ HAND + 13) — потом корпус к стойке
    HAND + 14: (-0.046, 30.0, 60.0, 7.8, 26.0, SL_LOCK, step(R_LAND, SR, 1 / 4, 0.06, 0.0)),
    HAND + 15: (-0.038, 8.0, 30.0, 7.6, 10.0, SL_LOCK, step(R_LAND, SR, 2 / 4, 0.06, 0.0)),
    HAND + 16: (-0.028, -16.0, 4.0, 7.3, -2.0, step(SL_LOCK, SLA, 1 / 4, 0.05, 0.0), step(R_LAND, SR, 3 / 4, 0.06, 0.0)),
    HAND + 17: (-0.016, -36.0, -16.0, 7.0, -6.0, step(SL_LOCK, SLA, 2 / 4, 0.05, 0.0), SR),
    HAND + 18: (-0.005, -49.0, -27.0, 6.7, -2.0, step(SL_LOCK, SLA, 3 / 4, 0.05, 0.0), SR),
    HAND + 19: (0.0, -52.1, -30.0, 6.5, -0.4, SLA, SR),
}
P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
P_UP = ((0.0, 0.9, 0.4), (0.10, -0.85, -0.35))         # левая: локоть вверх-наружу (кисть через верх за спину)
UP_W = {0: 0.0, 1: 0.25, 2: 0.7, 3: 1.0, HAND + 6: 1.0}
AXW = 0.08
GAP = 0.15
FULL0 = False


def _tab(t, f):
    xs = sorted(t)
    return pchip([(x, t[x]) for x in xs], f)


KEYS = {(f if f < HAND + 12 else f + D): k for f, k in KEYS.items()}
TURN = float(__import__("os").environ.get("V3S4ST_TURN", "1.1"))   # доля доворота груди влево (1 — как в KEYS)
if TURN != 1.0:
    KEYS = {f: (k[0], k[1] if f >= HAND + 14 + D else py0 + (k[1] - py0) * TURN, k[2] if f >= HAND + 14 + D else cy0 + (k[2] - cy0) * TURN) + tuple(k[3:])
            for f, k in KEYS.items()}


def body_at(f):
    fr = sorted(KEYS)
    if f in KEYS: k = KEYS[f]
    else:
        k = tuple(pchip([(x, KEYS[x][i]) for x in fr], f) for i in range(5)) + tuple(
            tuple(pchip([(x, KEYS[x][j][c]) for x in fr], f) for c in range(5)) for j in (5, 6))
    dz, pyaw, cyaw, lean, look, L, R = k
    return dict(W=1.0, dz=dz, pyaw=pyaw, cyaw=cyaw, lean=lean, look=look, gL=1.0 if f <= HAND else 0.35, gR=1.0, feet=dict(Left=tuple(L), Right=tuple(R)))


def poles(f, p):
    w = max(0.0, min(1.0, _tab(UP_W, f))) if f <= HAND + 6 else 0.0
    return {s: rot(tuple(x + (y - x) * w for x, y in zip(P_FRONT[i], P_UP[i])), p["cyaw"]) for i, s in enumerate(("Left", "Right"))}


def handle_dir(f, target, to_r):
    """Ось кулака (рукоять): у бедра — к якорю на земле справа-впереди, через верх — вниз по спине (рукоять ложится на лопатку)."""
    a = (0.55, -0.55, 0.55) if f <= 1 else (0.3, -0.6, 0.2) if f <= 3 else (0.15, -0.9, -0.35)
    n = math.sqrt(sum(c * c for c in a))
    return [c / n for c in a]
