"""Крушение v2: ключевые позы листов B (1–7) и раскладка клипов по тикам (кадр = тик, 30 к/с, корень стоит).

Оси корня: f — вперёд по Direction, l — влево, u — вверх (метры v6). Сабля за кушаком у левого бедра (вид), обе
кисти на рукояти цепи: левый кулак у кисточки, правый ниже на цепи (зазор GAP). Голова якоря в клипе НЕ
анимирована: «голова» в ключах — плановая точка, только чтобы направить ось рукояти (цепь натянута к голове);
путь головы считает запечка (anchor-core) от хвата. Хват ведёт кистень: малая петля кистей у пояса опережает
голову, восьмёрка маха 2 — через левое плечо, удар оземь — кисти над головой и рывком вниз-вперёд.
Ведущая нога меняется шагами на месте (позы 3 и 5): стопа отрывается, переносится и встаёт; таз по XY стоит.
"""
import copy, math

GAP = 0.17
SFX = "Pelag_AN_Wreck2_"
LIFT = 0.07                     # стопа в шаге над своей высотой стойки


def nrm(v):
    n = math.sqrt(sum(c * c for c in v)) or 1.0
    return tuple(c / n for c in v)


def sub(a, b): return tuple(x - y for x, y in zip(a, b))


def grip(G, head, gap=GAP):
    """Обе кисти на рукояти: левый кулак G, ось на плановую голову, правый кулак ниже по оси."""
    d = nrm(sub(head, G))
    R = tuple(g + gap * c for g, c in zip(G, d))
    return dict(Left=tuple(G), Right=R), dict(Left=d, Right=d), d


def key(dz, pyaw, cyaw, lean, L, R, hands, poles, gL=1.0, gR=1.0, look=0.0, W=1.0, hint=None, handle=True):
    hand, hax = hands[0], hands[1]
    chain = hands[2] if len(hands) > 2 else hax["Right"]
    return dict(W=W, dz=dz, pyaw=pyaw, cyaw=cyaw, lean=lean, look=look, gL=gL, gR=gR, chain=tuple(chain),
                feet=dict(Left=tuple(L), Right=tuple(R)), hand=hand, hax=hax, poles=dict(Left=poles[0], Right=poles[1]),
                sol=None, hint=hint or {}, handle=handle)


def stance(B, SF):
    """Стойка серии сабли: все слои с весом 0; кисти и локти — как в стойке (для интерполяции рук)."""
    ft = {s: (-B["ankle"][s].y, B["ankle"][s].x, B["ankle"][s].z, B["foot_yaw"][s], 0.0) for s in ("Left", "Right")}
    root = lambda v: (-v.y, v.x, v.z)
    hand = {s: tuple(root(SF["pos"][s])) for s in ("Left", "Right")}
    hax = {s: tuple(root(SF["ax"][s])) for s in ("Left", "Right")}
    k = key(0.0, B["pyaw"], B["cyaw"], B["lean"], ft["Left"], ft["Right"], (hand, hax),
            (SF["pole"]["Left"], SF["pole"]["Right"]), gL=0.0, gR=0.0, look=B["head"]["faceYaw"], W=0.0)
    k["sol"] = {"Left": (0.0, 0.0, 0.0, 0.0), "Right": (0.0, 0.0, 0.0, 0.0)}
    return k


def foot(base, df=0.0, dl=0.0, du=0.0, yaw=None, pitch=0.0):
    f, l, u, y, p = base
    return (f + df, l + dl, u + du, y if yaw is None else yaw, p + pitch)


def step(a, b, t, lift=LIFT, swing=0.0, win=(0.2, 0.8)):
    """Стопа в шаге: сначала вверх, перенос в середине шага, вниз на место (носок не волочится, стопа не едет у земли).
    Разворот носка и наклон стопы — к двум третям шага (в воздухе); swing — стопа за голенью в воздухе
    (+ носок вниз при шаге назад, − носок вверх при шаге вперёд), чтобы стопа не крутилась против голени."""
    th = min(1.0, max(0.0, (t - win[0]) / (win[1] - win[0]))); th = th * th * (3 - 2 * th)
    tr = min(1.0, t / 0.75)
    up = lift * math.sin(math.pi * t) ** 0.4
    return (a[0] + (b[0] - a[0]) * th, a[1] + (b[1] - a[1]) * th, a[2] + (b[2] - a[2]) * th + up,
            a[3] + (b[3] - a[3]) * tr, a[4] + (b[4] - a[4]) * tr + swing * math.sin(math.pi * t))


def lerp_key(a, b, t):
    q = copy.deepcopy(a)
    for k in ("dz", "pyaw", "cyaw", "lean", "look", "gL", "gR"):
        q[k] = a[k] + (b[k] - a[k]) * t
    L = lambda x, y: tuple(u + (v - u) * t for u, v in zip(x, y))
    for grp in ("feet", "hand", "hax", "poles"):
        q[grp] = {s: L(a[grp][s], b[grp][s]) for s in a[grp]}
    q["chain"] = L(a["chain"], b["chain"])
    q["W"] = 1.0; q["sol"] = None
    return q
