"""Крушение v3, «Волнорез»: Pelag_AN_Wreck2_Slam_Drag (06.10) — удар оземь базы + протяжка (поза 11), для v3s4_author.

Кадры 0–16 (= контакт 13 + 3) — копия ключей принятого Slam (тот же хват → та же запечка до удара и в удержании).
17–25 — поза 11 листа B2: шаг правой из упора вперёд под таз (смена опоры на месте: корень стоит — Sim держит героя по
удар + 9), корпус клонится до ~41°, голова поднята (кивок вверх 4,5°, голова за грудью ±5°), кисти тянут цепь назад к
правому бедру — голова якоря тащится по земле к герою, цепь натянута. С 21 — выход: правая назад в стойку, левая
подтягивается, кисти к рукояти стойки; кадр 25 — стойка навыка (корпус и кисти — Slam 24, левая на месте выпада, как Wait1/Stow 0, правая — стойка сабли),
к отпуску Sim (ходьба с удар + 10 = кадр 23) поза почти стойка.
"""
import math
from s_lib import pchip
from wk_clips import SR, on_toe
from wk_keys import step
import v3_keys
import v3s4sl_keys as SLK

CLIP = "Pelag_AN_Wreck2_Slam_Drag"
CONTACT, HOLD = 13, 3
FROM = CONTACT + HOLD                      # 16 — последний кадр копии
N = 25
SEAM = {f: ("Pelag_AN_Wreck2_Slam", f) for f in range(FROM + 1)}
HINT_FROM = ("Pelag_AN_Wreck2_Slam", FROM)
STRIKE = (0, CONTACT + 1)
FULL0 = False
GAP = 0.15
AXW = 0.30
SMOOTH = (("RightShoulder", "RightArm", "RightForeArm", "RightHand"), 1, 0.35)
SMOOTH_FROM = FROM + 1

SL_LOCK = v3_keys.SL_LOCK
SLA = SLK.SLA
R16 = on_toe("Right", SLK.R_BACK, 22.0)    # правая в упоре на носке (Slam 16)
R_F = (0.06, -0.36, 0.140, -38.0, 0.0)     # правая шагнула из упора вперёд под таз и наружу (поза 11; бедро мимо кистей)
HEAD_UP = 4.5

# кадр: (dz, pyaw, cyaw, lean, look) — 16 и 25 только для гладкости кривых (сами кадры — копии)
T = {FROM: (-0.125, -3.0, 3.0, 36.0, 1.0),
     17: (-0.128, 2.0, -2.0, 38.0, -1.0),
     18: (-0.130, 6.0, -6.0, 40.0, -3.0),
     19: (-0.130, 9.0, -9.0, 41.0, -5.0),        # поза 11: шаг сделан, наклон ~41°, кисти низко впереди, тянут
     20: (-0.128, 9.0, -11.0, 41.0, -6.0),
     21: (-0.118, -2.0, -16.0, 34.0, -7.0),       # выход: корпус встаёт
     22: (-0.095, -20.0, -24.0, 24.0, -6.0),
     23: (-0.075, -38.0, -28.0, 15.0, -4.0),
     24: (-0.062, -48.0, -29.5, 9.0, -1.5),
     N: (-0.058, -52.1, -30.0, 6.5, -0.4)}         # стойка навыка: корпус как Slam 24, левая на месте выпада (как Wait1/Stow 0)
ARM_INTERP = {sd: (21, N, {22: 0.25, 23: 0.5, 24: 0.75}) for sd in ("Left", "Right")}   # к рукояти стойки — смесью поворотов рук


def feet(f):
    if f <= FROM: R = R16
    elif f <= 19: R = step(R16, R_F, (f - FROM) / 3.0, 0.07, 0.0, (0.05, 0.7))
    elif f == 20: R = R_F
    elif f <= 23: R = step(R_F, SR, (f - 20) / 3.0, 0.10, 0.0, (0.2, 0.8))
    else: R = SR
    L = SL_LOCK
    return L, R


def body_at(f):
    ks = sorted(T)
    v = [pchip([(k, T[k][i]) for k in ks], f) for i in range(5)]
    L, R = feet(f)
    hu = HEAD_UP * max(0.0, min(1.0, pchip([(FROM, 0.0), (18, 1.0), (20, 1.0), (22, 0.3), (N, 0.0)], f)))
    return dict(W=1.0, dz=v[0], pyaw=v[1], cyaw=v[2], lean=v[3], look=v[4], gL=1.0, gR=1.0, head_up=hu,
                feet=dict(Left=tuple(L), Right=tuple(R)))


def post_body(rig, p):
    """Голова поднята (подбородок вверх) — поза 11: «наклон ~40°, голова вверх»; голова за грудью ±5°."""
    import v3s5_lib as L5
    L5.nod(rig, -p.get("head_up", 0.0))


def poles(f, p):
    return SLK.poles(min(f, 24), p)


def axw(f):
    return SLK.axw(f)
