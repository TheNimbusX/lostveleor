"""Крушение v2: помощники раскладки (ступни, носок, грудь); сами ключи — wk_clipkeys.py. Раскладка по кадрам (= тики Sim, самый быстрый темп 7 / 14 / 24).
Swing1 0..12 (контакт 7; 8 = Swing2 0; 12 = Wait1 0), Swing2 0..11 (контакт 6; 7 = Slam 0; 11 = Wait2 0),
Slam 0..20 (над головой 5, контакт 9, удержание 10–12, выход 13–20 в стойку ног серии сабли), Wait1/Wait2 0..12
(цикл, 12 = 0), Stow 0..7 (якорь на спину, 7 = стойка серии сабли). Номера поз — лист B (1–7).
"""
import math
from wk_keys import key, grip, stance, foot, step, lerp_key, SFX

# ступни стойки серии сабли (лодыжка f, l, u, рысканье носка, наклон) и носки (ToeBase), замер wk_probe
SL = (0.476, 0.163, 0.136, -6.5, 0.0)
SR = (-0.23, -0.085, 0.136, -67.8, 0.0)
TOE = {"Left": (0.630, 0.145), "Right": (-0.171, -0.228)}
STANCE_FOOT = {"Left": SL, "Right": SR}


def toe_of(side, a):
    """Носок стопы с лодыжкой a (по смещению носка стойки, повёрнутому на разницу рысканья)."""
    s = STANCE_FOOT[side]; t = TOE[side]
    dx, dy = t[0] - s[0], t[1] - s[1]
    r = math.radians(a[3] - s[3]); c, sn = math.cos(r), math.sin(r)
    return (a[0] + dx * c - dy * sn, a[1] + dx * sn + dy * c)


def on_toe(side, a, pitch, dyaw=0.0):
    """Пятка вверх на pitch° и поворот на dyaw° вокруг носка: носок стоит, лодыжка идёт по кругу носка."""
    tf, tl = toe_of(side, a)
    ux, uy = tf - a[0], tl - a[1]; un = math.hypot(ux, uy)
    tf, tl = tf + ux / un * 0.04, tl + uy / un * 0.04          # ось — подушечки пальцев (4 см за ToeBase)
    r = math.radians(dyaw); c, sn = math.cos(r), math.sin(r)
    df, dl = a[0] - tf, a[1] - tl
    df, dl = df * c - dl * sn, df * sn + dl * c
    h = math.hypot(df, dl); du = a[2] - 0.004
    p = math.radians(pitch)
    nh = h * math.cos(p) - du * math.sin(p); nu = h * math.sin(p) + du * math.cos(p)
    return (tf + df / h * nh, tl + dl / h * nh, 0.004 + nu, a[3] + dyaw, a[4] + pitch)


SPINE = (0.05, -0.03)          # ось позвоночника в осях корня (кисти задаются от груди)


def rot(v, deg):
    a = math.radians(deg); c, sn = math.cos(a), math.sin(a)
    return (v[0] * c - v[1] * sn, v[0] * sn + v[1] * c) + tuple(v[2:])


def cr(f, l, u, cy):
    """Точка, заданная от груди (f — куда смотрит грудь, l — влево от груди, u — высота), → оси корня."""
    x, y = rot((f, l), cy)
    return (x + SPINE[0], y + SPINE[1], u)


# локти от груди (f — куда смотрит грудь): кисти перед животом / у левого плеча / над головой / снятие со спины
P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
P_MID = ((0.20, 0.75, -0.60), (0.45, -0.45, -0.75))
P_LSH = ((0.00, 0.50, -0.85), (0.60, -0.20, -0.75))
P_OVER = ((0.20, 0.95, 0.25), (0.20, -0.95, 0.25))
P_DRAW = ((0.60, 0.30, -0.70), (0.10, -0.80, -0.60))
P_DRAW1 = ((-0.10, 0.90, -0.30), (-0.30, -0.70, -0.65))
