"""Бросок якоря (Pelag_AN_AnchorThrow_*): ключевые позы по листу B-key-poses-higgsfield.png (позы 1–6) и видео
anchor-throw-kling.mp4 (бросок, натяг, рывок; ловля — по позе 6), раскладка клипов по тикам — at_check.py.

Оси корня: f — вперёд по Dir (к курсору), l — влево, u — вверх (метры v6). Сабля за кушаком у левого бедра (вид,
BeginAnchorUse), рукоять цепи в ЛЕВОМ кулаке весь навык (gL), бросает и ловит ПРАВАЯ (gR: С-хват → ладонь → кулак).
Цепь в бросках натянута и прямая: из рукояти левой вдоль Dir к голове на линии Sim (anchor-core DESIGN §1.2, §4.1),
поэтому правая на цепи лежит впереди левой по f (cR, offR) — на той же прямой, что цепь.

Клипы (кадр = тик при самой длинной раскладке: W = 2, F = 10, R = 8):
  Throw 0 стойка, 1 замах (лист 2, ≤ 35° за тик, как поза 1 Абордажа v2: правая берёт якорь у правого плеча, корпус
        закручен назад, правая стопа оторвана — вид крутит корень вокруг левой лодыжки), 2 ВЫПУСК (лист 3: плоско от
        плеча вперёд, ладонь раскрыта), 3 рука прямая по Dir, 4–5 правая ложится на цепь впереди левой
  Fly   0 = Throw 5; 1–3 упор: колени собраны, левая подаёт цепь короткими движениями, 3 = НАТЯГ (лист 4) = Yank 0
  Yank  0 натяг, 1 рывок плечами, корпус назад, правая стопа шагает назад, 2 тянет (лист 5) = Haul 0
  Haul  0–3 и 3–6 — перехват правой (дотянуться вперёд по цепи, схватить, рвануть к себе); 3 = 0 (та же поза:
        короткий возврат начинается с кадра 3); 5 правая отпускает цепь и раскрывается навстречу; 6 ЛОВЛЯ = Catch 0
  Catch 0 ловля (лист 6: кольцо в правой у груди), 1 рывок кисти от удара, 2–3 якорь через правое плечо на спину,
        4–6 шаг правой в стойку, 9 стойка серии сабли (= конец рывка, Шквала, Абордажа)
"""
import copy, math

FIST = (1.0, 0.0, 0.0)
TOE = {"Left": (0.630, 0.145, 0.004), "Right": (-0.171, -0.228, 0.004)}   # носки стойки (замер at_probe)
UP = (0.0, 0.0, 1.0)
SU, SFLEX, SB = (-0.51, -0.26, -0.82), 48.2, (0.75, -0.59, -0.28)       # правая рука стойки (плечо, сгиб, плоскость)
ZERO = (0.0, 0.0, 0.0)


def pose(dz, pyaw, cyaw, lean, L, R, hR, hL, arm=(0.0, 0.0, 32.0, 0.0), pR=(0.0, -0.6, -0.8), wp=0.0, W=1.0, look=0.0,
         gL=0.0, gR=0.0, habsL=None, habsR=None, offR=None, pL=(0.0, 0.6, -0.8), wpL=0.0, wP=0.0, lookP=0.0):
    p = dict(W=W, dz=dz, pyaw=pyaw, cyaw=cyaw, lean=lean, feet=dict(Left=L, Right=R), hands=dict(Right=hR, Left=hL),
             poles=dict(Right=pR, Left=pL), blade=FIST, wB=0.0, look=look, sv=0.0, tv=0.0, wd=0.0, wf=0.0, wp=wp, wpL=wpL,
             gL=gL, gR=gR, habs=dict(Left=habsL or ZERO, Right=habsR or ZERO), offR=offR or ZERO,
             haL=1.0 if habsL else 0.0, haR=1.0 if habsR else 0.0, cR=1.0 if offR else 0.0, wP=wP, lookP=lookP)
    p["arm"] = arm
    return p


def stance(B):
    """Стойка серии сабли на v6 (все слои с весом 0) — в ней кончаются рывок, Шквал, Абордаж и выход Броска."""
    ft = {s: (-B["ankle"][s].y, B["ankle"][s].x, B["ankle"][s].z, B["foot_yaw"][s], 0.0) for s in ("Left", "Right")}
    p = pose(0.0, B["pyaw"], B["cyaw"], B["lean"], ft["Left"], ft["Right"], tuple(B["hand_rootR"]), tuple(B["hand_rootL"]),
                arm=(0.0, 0.0, 0.0, 0.0), W=0.0, look=B["head"]["faceYaw"],
                habsL=tuple((-B["hand"]["Left"].y, B["hand"]["Left"].x, B["hand"]["Left"].z)),
                habsR=tuple((-B["hand"]["Right"].y, B["hand"]["Right"].x, B["hand"]["Right"].z)))
    p["haL"] = p["haR"] = 0.0          # кисти стойки — от плеча; абсолютные числа только для плавной интерполяции
    return p


def on_toe(side, ankle, deg, shift=(0.0, 0.0), lift=0.0):
    """Пятка вверх на deg вокруг носка (носок стойки, сдвинутый на shift по f, l): носок стоит, лодыжка поднимается."""
    tf, tl, tu = TOE[side]
    tf, tl = tf + shift[0], tl + shift[1]
    af, al, au = ankle[0] + shift[0], ankle[1] + shift[1], ankle[2]
    df, dl, du = af - tf, al - tl, au - tu
    h = math.hypot(df, dl)
    a = math.radians(deg)
    nh = h * math.cos(a) - du * math.sin(a)
    nu = h * math.sin(a) + du * math.cos(a)
    return (tf + df / h * nh, tl + dl / h * nh, tu + nu + lift, ankle[3], ankle[4] + deg)


def crot(v, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return (v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2])


def nrm(v):
    n = math.sqrt(sum(c * c for c in v))
    return tuple(c / n for c in v)


def tilt(v, to, deg):
    from mathutils import Vector
    v, to = Vector(nrm(v)), Vector(nrm(to))
    w = (to - to.dot(v) * v)
    if w.length < 1e-6: return tuple(v)
    w.normalize(); a = math.radians(deg)
    return tuple(v * math.cos(a) + w * math.sin(a))


def arm(u, flex, b):
    """Правая рука по плечевой кости: u — куда смотрит плечо, flex — сгиб локтя (°), b — куда складывается предплечье."""
    from mathutils import Vector
    u = Vector(u).normalized(); b = Vector(b); b = (b - b.dot(u) * u).normalized()
    a = math.radians(flex)
    f = u * math.cos(a) + b * math.sin(a)
    v = u * 0.254 + f * 0.255
    vn = v.normalized()
    pole = u - u.dot(vn) * vn
    return tuple(round(c, 4) for c in v), tuple(round(c, 4) for c in pole.normalized())


def clips(B):
    """Все клипы: имя → {кадр: поза}. Кадры между ключами — монотонная кубика по каждому числу (at_measure.sample)."""
    S = stance(B)
    sL, sR = S["feet"]["Left"], S["feet"]["Right"]
    cy0 = B["cyaw"]
    yR = sR[3]
    PR_CHAIN = (-0.05, -0.75, -0.65)                                      # правый локоть на цепи: наружу и вниз

    def K(dz, pyaw, cyaw, lean, L, R, u, flex, hL, gL, gR, wd=32.0, habsL=None, look=0.0):
        """Правая рука по плечу u и сгибу (плоскость локтя — плоскость стойки, повёрнутая с грудью, без крутки)."""
        from mathutils import Vector
        us, bs = Vector(crot(SU, cyaw - cy0)).normalized(), Vector(crot(SB, cyaw - cy0))
        n0 = us.cross(bs).normalized()
        uu = Vector(nrm(u))
        n = us.rotation_difference(uu) @ n0
        v, pole = arm(tuple(uu), flex, tuple(n.cross(uu)))
        return pose(dz, pyaw, cyaw, lean, L, R, v, hL, arm=(0.0, 0.0, wd, 0.0), pR=pole, wp=1.0, gL=gL, gR=gR,
                    habsL=habsL, look=look)

    def KA(dz, pyaw, cyaw, lean, L, R, hL, hR=None, off=None, gL=1.0, gR=0.0, wd=32.0, pR=None, wP=0.0):
        """Обе кисти — абсолютные цели в осях корня; правая — своя цель hR или на цепи впереди левой (off)."""
        return pose(dz, pyaw, cyaw, lean, L, R, tuple(B["hand_rootR"]), tuple(B["hand_rootL"]), arm=(0.0, 0.0, wd, 0.0),
                    pR=pR or PR_CHAIN, wp=1.0 if pR else 0.0, gL=gL, gR=gR, habsL=hL, habsR=hR, offR=off, wP=wP)

    # ---- стопы -------------------------------------------------------------------------------------------------
    rUp = (sR[0] - 0.04, sR[1] - 0.01, sR[2] + 0.07, sR[3], sR[4] + 3)    # замах: правая оторвана (вид крутит корень)
    rB2 = on_toe("Right", sR, 6, shift=(-0.10, -0.03))                   # выпуск: правая встала назад на носок
    rB = on_toe("Right", sR, 10, shift=(-0.10, -0.03))                   # проводка: на носке
    rFlat = on_toe("Right", sR, 0, shift=(-0.10, -0.03))                 # упор: пятка опустилась вокруг носка
    rToe = on_toe("Right", sR, 8, shift=(-0.10, -0.03))                  # натяг: пятка оторвалась — шаг назад пошёл
    rAir = (-0.42, -0.135, 0.21, yR, 4.0)                                 # рывок: стопа в воздухе над местом посадки
    rBack = (-0.42, -0.135, sR[2] + 0.006, yR, 0.0)                       # тяга: правая сзади плашмя (лист 5)
    rRet1 = (-0.40, -0.13, 0.20, yR, 4.0)                                # выход: шаг правой в стойку, в воздухе
    rRet2 = (sR[0], sR[1], sR[2] + 0.06, yR, 2.0)                        #        над местом стойки

    # ---- Throw: замах 1 тик (≤ 35°), выпуск, рука прямая по Dir, правая ложится на цепь ----------------------
    T1 = K(-0.03, -55, -62, -5, sL, rUp, tilt(crot(SU, -32), UP, 26), 76, (0.31, -0.02, -0.31), 0.34, 0.0, 18,
           habsL=(0.465, 0.055, 0.915))                                                                            # лист 2
    T2 = K(-0.06, -46, -16, 15, sL, rB2, (0.46, -0.88, 0.06), 40, (0.32, -0.05, -0.27), 0.68, -1.0,
           habsL=(0.515, -0.02, 0.955))                                                                           # лист 3
    T3 = KA(-0.07, -42, -8, 18, sL, rB, (0.48, -0.06, 0.985), hR=(0.59, -0.21, 1.055), gR=-0.85, pR=PR_CHAIN)
    T4 = KA(-0.07, -42, -9, 17, sL, rB, (0.42, -0.09, 1.015), off=(0.205, 0.0, 0.0), gR=-0.5, pR=PR_CHAIN)
    T5 = KA(-0.07, -43, -11, 14, sL, rB, (0.37, -0.10, 1.025), off=(0.25, 0.0, 0.0), gR=-0.2, pR=PR_CHAIN)
    throw = {0: S, 1: T1, 2: T2, 3: T3, 4: T4, 5: T5}

    # ---- Fly: упор к натягу, левая подаёт цепь короткими движениями (правая на цепи стоит) ---------------------
    F1 = KA(-0.07, -44, -13, 10, sL, rFlat, (0.33, -0.10, 1.03), off=(0.26, 0.02, 0.0), gR=0.0, pR=PR_CHAIN)
    F2 = KA(-0.075, -44, -14, 6, sL, rFlat, (0.30, -0.10, 1.03), off=(0.28, 0.0, 0.0), gR=0.1, wP=0.5, pR=PR_CHAIN)
    F3 = KA(-0.075, -44, -15, 3, sL, rToe, (0.30, -0.10, 1.025), off=(0.27, 0.0, -0.02), gR=0.2, wP=1.0, pR=PR_CHAIN)     # лист 4
    fly = {0: T5, 1: F1, 2: F2, 3: F3}

    # ---- Yank: натяг, рывок плечами (кулаки к груди) и шаг назад, тяга --------------------------------------
    PR_OUT = (-0.05, -0.75, -0.65)                                     # локоть наружу: предплечье мимо левого кулака
    Y1 = KA(-0.07, -46, -21, -13, sL, rAir, (0.22, -0.11, 1.10), off=(0.17, 0.0, -0.05), gR=1.0, wP=1.0, pR=PR_OUT)
    Y2 = KA(-0.07, -47, -24, -15.5, sL, rBack, (0.20, -0.11, 1.12), off=(0.155, 0.0, -0.10), gR=1.0, wP=1.0, pR=PR_OUT)  # лист 5
    yank = {0: F3, 1: Y1, 2: Y2}

    # ---- Haul: перехват правой дважды, 3 = 0; 5 правая навстречу; 6 ловля ------------------------------------
    H1 = KA(-0.07, -47, -21, -14, sL, rBack, (0.20, -0.11, 1.12), off=(0.25, 0.0, -0.12), gR=0.4, wP=1.0, pR=PR_OUT)
    H2 = KA(-0.07, -47, -24, -16, sL, rBack, (0.19, -0.13, 1.12), off=(0.22, 0.0, -0.15), gR=1.0, wP=1.0, pR=PR_OUT)
    H5 = KA(-0.07, -47, -21, -14, sL, rBack, (0.235, -0.07, 1.10), hR=(0.37, -0.20, 1.10), gR=-0.2, wP=1.0, pR=PR_OUT)
    H6 = KA(-0.07, -47, -21, -13, sL, rBack, (0.23, -0.05, 1.10), hR=(0.35, -0.205, 1.12), gR=-0.3, wP=1.0, pR=PR_OUT)  # лист 6
    haul = {0: Y2, 1: H1, 2: H2, 3: Y2, 4: H1, 5: H5, 6: H6}

    # ---- Catch: ловля, рывок кисти, якорь через правое плечо на спину, шаг в стойку --------------------------
    C1 = KA(-0.07, -47, -24, -15, sL, rBack, (0.24, -0.05, 1.07), hR=(0.32, -0.20, 1.15), gR=0.7, wP=1.0, pR=PR_OUT)
    C2 = KA(-0.065, -48, -27, -11, sL, rBack, (0.25, -0.02, 1.04), hR=(0.25, -0.26, 1.21), gR=1.0, wP=0.7, pR=PR_OUT)
    C3 = KA(-0.055, -49, -29, -6, sL, rBack, (0.30, 0.02, 1.00), hR=(0.19, -0.30, 1.27), gR=0.8, wP=0.4)
    C4 = KA(-0.045, -50, -30, -1, sL, rRet1, (0.37, 0.07, 0.97), hR=(0.16, -0.34, 1.20), gL=0.66, gR=0.5, wP=0.1)
    C5 = KA(-0.03, -51, -30, 3, sL, rRet2, (0.44, 0.115, 0.92), hR=(0.08, -0.40, 1.06), gL=0.33, gR=0.25)
    C6 = KA(-0.015, -52, -30, 5.5, sL, sR, (0.49, 0.145, 0.885), hR=(0.03, -0.44, 0.955), gL=0.1, gR=0.08)
    catch = {0: H6, 1: C1, 2: C2, 3: C3, 4: C4, 5: C5, 6: C6, 9: S}
    P = "Pelag_AN_AnchorThrow_"
    return {P + "Throw": throw, P + "Fly": fly, P + "Yank": yank, P + "Haul": haul, P + "Catch": catch}
