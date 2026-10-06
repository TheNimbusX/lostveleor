"""Крушение v3, Swing2 — ПРОДОЛЖЕНИЕ ВРАЩЕНИЯ (решение 06.10: физика важнее старого темпа; восьмёрки нет).

Кадр 0 = Swing1 кадр 8 (копия ключей из его .blend). Голова якоря после первого удара летит влево 25,6 м/с — она идёт дальше
по кругу справа налево (влево → за спину → вправо → вперёд), корпус поворачивается вместе с ней на полный оборот (таз ≈ +345°
за 11 тиков, ≤ 34°/тик), и второй удар — снова плоский мах справа налево в позе контакта Swing1 (кадры C−1.. = Swing1 6..12,
тело копией из .blend Swing1, руки заново по пути хвата).
Ноги (таз по XY стоит, корень не едет): 0–2 правая шагает из-за спины под таз (на носок), 2–9 — пируэт на подушечке правой
(носок стоит под тазом, стопа поворачивается вместе с тазом, пятка поднята), левая в воздухе — колено поднято, стопа
у левого бедра; 9 — левая встаёт на своё место выпада (как Swing1), 9–11 правая шагает назад на носок (поза Swing1 кадр 6).
Рысканье таза по кадрам — PSI из пути gripopt_s4 (V3S4_PATH, иначе bake/Pelag_AN_Wreck2_Swing2.path.json).
"""
import json, math, os
from s_lib import pchip
from wk_clips import rot, toe_of
from wk_keys import step
import v3_keys

CLIP = "Pelag_AN_Wreck2_Swing2"
_V3 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
_P = json.load(open(os.environ.get("V3S4_PATH") or os.path.join(_V3, "bake", CLIP + ".path.json"), encoding="utf-8"))
PSI = _P["psi"]
CONTACT = int(_P.get("contact_frame", 12))
COPY = int(_P.get("copy_from", CONTACT - 1))
N = len(PSI) - 1
S1 = lambda f: f - CONTACT + 7                       # кадр Swing1 для кадров копии
SEAM = {0: ("Pelag_AN_Wreck2_Swing1", 8)}
BODY_COPY = {}                                       # конец — ключами Swing1 7..12 (правая не сзади, а у правого бедра: R_LAND)
HINT_FROM = ("Pelag_AN_Wreck2_Swing1", 8)
HINT_AT = {f: ("Pelag_AN_Wreck2_Swing1", S1(f)) for f in range(CONTACT + 3, CONTACT + 4)}   # конец = позы Swing1: решение левой оттуда (без упора в предел)
ARM_INTERP = {sd: (CONTACT, CONTACT + 3, {CONTACT + 1: 0.33, CONTACT + 2: 0.67}) for sd in ("Left", "Right")}   # переход к нему — смесью поворотов
STRIKE = (0, CONTACT + 3)                            # руки ≤ 70°/тик: весь оборот и удар (кисти несут цепь вокруг тела)
FULL0 = False
HIPS_FL = (0.059, 0.014)                             # таз в осях корня (f, l), Swing1
PIV = int(_P.get("piv", 3))                         # правая встала под таз (шаг 0→PIV)
LAND = int(_P.get("land", COPY))                    # левая встаёт на место выпада в кадр удара (после него таз почти не крутится)
RSTEP = 3                                            # правая после удара: с точки пируэта к правому бедру (R_LAND) за RSTEP тиков
RL = (v3_keys.R_LAND[0], v3_keys.R_LAND[1], v3_keys.R_LAND[2], v3_keys.R_LAND[3] + 360.0, v3_keys.R_LAND[4])
L_LANDP = 14.0                                       # левая садится на подушечку (пятка поднята) и доворачивает к удару
LPIV = 0.6                                           # левая до отрыва вертится на подушечке: доля поворота таза
KC = v3_keys.KEYS[S1(COPY)]                          # ключ Swing1 первого кадра копии
R_YAW = {PIV: -75.0, PIV + 2: -50.0, PIV + 4: -35.0, LAND: -30.0}      # носок правой при пируэте — от взгляда таза (стопа доворачивает быстрее таза)
BALL = 0.080                                         # опора пируэта — кончики пальцев (пятно касания мало: стопа вертится, не едет)
PITCH = 42.0
TW = ({f: float(t) for f, t in enumerate(_P["twist"][:COPY])} if _P.get("twist") else {0: 18.0, PIV: 20.0, 6: 21.0, LAND: 17.0})
TW[COPY] = KC[2] - KC[1]
LEAN = {0: v3_keys.KEYS[8][3], PIV: 16.0, 6: 15.0, COPY - 2: 18.0, COPY: KC[3]}
DZ = {0: v3_keys.KEYS[8][0], PIV: -0.080, 6: -0.070, COPY - 2: -0.100, COPY: KC[0]}
LOOKD = {0: v3_keys.KEYS[8][4] - v3_keys.KEYS[8][2], 3: -28.0, COPY - 2: -24.0, COPY: KC[4] - KC[2]}
R8 = v3_keys.rT(25, 22)
R6 = tuple(KC[5])                                     # правая в первом кадре копии
SL = v3_keys.SL_LOCK
SL3 = (SL[0], SL[1], SL[2], SL[3] + 360.0, SL[4])


def psi(t):
    """Рысканье таза в момент t (кадры; дробные — ключи на полкадра)."""
    i = min(int(math.floor(t)), N - 1); u = t - i
    return PSI[i] + (PSI[i + 1] - PSI[i]) * u


HALF = [int(x) for x in _P["half"]] if "half" in _P else []   # Build берёт только целые кадры (anchor_grip_export: ключ на кадр) — полукадров нет
# ↑ кадры с ключом на f + 0,5: отрыв и посадка стоп при повороте таза ~30°/тик (иначе между кадрами стопа ныряет и едет)


def _tab(t, f):
    xs = sorted(t)
    return pchip([(x, t[x]) for x in xs], f)


def ankle_on_ball(side, ball, yaw, pitch, ext=0.04, sink=0.0):
    """Лодыжка стопы с подушечкой в точке ball (f, l), носок по yaw, пятка поднята на pitch°."""
    base = (0.0, 0.0, 0.136, yaw, 0.0)
    tf, tl = toe_of(side, base)
    ux, uy = tf - base[0], tl - base[1]; un = math.hypot(ux, uy)
    bf, bl = tf + ux / un * ext, tl + uy / un * ext              # точка опоры (wk_clips.on_toe: подушечка 0,04)
    a = (base[0] + ball[0] - bf, base[1] + ball[1] - bl, 0.136, yaw, 0.0)
    if ext == 0.04: return v3_keys.on_toe(side, a, pitch, 0.0)
    # поворот вокруг точки опоры ext (как on_toe, но плечо ext)
    df, dl = a[0] - ball[0], a[1] - ball[1]; hh = math.hypot(df, dl); du = a[2] - 0.004; pp = math.radians(pitch)
    nh = hh * math.cos(pp) - du * math.sin(pp); nu = hh * math.sin(pp) + du * math.cos(pp)
    return (ball[0] + df / hh * nh, ball[1] + dl / hh * nh, 0.004 + nu - sink, yaw, pitch)


def _ball_r8():
    tf, tl = toe_of("Right", v3_keys.SRW); ux, uy = tf - v3_keys.SRW[0], tl - v3_keys.SRW[1]; un = math.hypot(ux, uy)
    return tf + ux / un * 0.04, tl + uy / un * 0.04


B8 = _ball_r8()                                      # подушечка правой в Swing1 кадр 8 (стоит на носке сзади)
POFF = float(os.environ.get("V3S4_POFF") or _P.get("poff", 0.18))      # точка пируэта: POFF м от таза в сторону PANG (° от «вперёд» корня,
PANG = math.radians(float(os.environ.get("V3S4_PANG") or _P.get("pang", -135.0)))   # + влево); −150° — назад-вправо, туда, где стояла правая
PIVPT = (HIPS_FL[0] + POFF * math.cos(PANG), HIPS_FL[1] + POFF * math.sin(PANG))


def r_piv(f):
    return ankle_on_ball("Right", PIVPT, psi(f) + _tab(R_YAW, min(f, LAND)), PITCH, BALL)


def l_air(f, rel, r, u, yrel, pitch):
    a = math.radians(psi(f) + rel)
    return (HIPS_FL[0] + r * math.cos(a), HIPS_FL[1] + r * math.sin(a), u, psi(f) + yrel, pitch)


SPOT = math.degrees(math.atan2(SL[1] - HIPS_FL[1], SL[0] - HIPS_FL[0]))   # место выпада левой от таза (°, + влево)
SPOT_R = math.hypot(SL[1] - HIPS_FL[1], SL[0] - HIPS_FL[0])


LAIR = dict(rel15=0.10, rel40=0.47, rel70=0.88, r30=0.47, r60=0.37, r85=0.42, h15=0.225, h40=0.29, h65=0.30, h85=0.24,
            pitch30=16.0, yaw50=0.0)                 # узлы пути левой в воздухе (подбор: v3s4_legopt.py → V3S4_LAIR / "lair" в пути)
if os.environ.get("V3S4_LAIR"): LAIR.update(json.load(open(os.environ["V3S4_LAIR"])))
elif _P.get("lair"): LAIR.update(_P["lair"])


def l_air_path(f):
    """Левая в воздухе (PIV..LAND−1): из перекрёста у правой ноги — к левому бедру (колено поднято) — и над местом посадки
    за тик до неё (садится отвесно). u — доля пути воздуха; углы — от взгляда таза; узлы — LAIR."""
    u = (f - PIV) / (LAND - 1 - PIV); A = LAIR
    b = l_land()
    r0 = SPOT - PSI[PIV]; r1 = math.degrees(math.atan2(b[1] - HIPS_FL[1], b[0] - HIPS_FL[0])) + 360.0 - PSI[LAND - 1]
    rb = math.hypot(b[1] - HIPS_FL[1], b[0] - HIPS_FL[0])
    y0 = SL[3] + l_dyaw(PIV) - PSI[PIV]; y1 = b[3] - PSI[LAND - 1]
    D = r1 - r0
    rel = pchip([(0, r0), (0.15, r0 + A["rel15"] * D), (0.4, r0 + A["rel40"] * D), (0.7, r0 + A["rel70"] * D), (1.0, r1)], u)
    rr = pchip([(0, SPOT_R), (0.3, A["r30"]), (0.6, A["r60"]), (0.85, A["r85"]), (1.0, rb)], u)
    h = pchip([(0, 0.17), (0.15, A["h15"]), (0.4, A["h40"]), (0.65, A["h65"]), (0.85, A["h85"]), (1.0, b[2] + 0.045)], u)
    yr = pchip([(0, y0), (0.5, (y0 + y1) / 2 + A["yaw50"]), (1.0, y1)], u)
    pt = pchip([(0, 22.0), (0.3, A["pitch30"]), (1.0, b[4] + 4.0)], u)
    return l_air(f, rel, rr, h, yr, pt)


def l_land():
    return SL3 if LAND >= COPY else v3_keys.on_toe("Left", SL3, L_LANDP, l_land_dyaw())


def l_land_dyaw():
    return -0.45 * (PSI[COPY] - PSI[LAND]) if LAND < COPY else 0.0


def l_dyaw(f):
    return LPIV * (psi(min(f, PIV)) - PSI[0])


def feet(f):
    if f == 0: return SL, R8
    if f >= COPY or f >= LAND and LAND >= COPY: Lf = SL3
    elif f >= LAND:                                  # на подушечке: носок отстаёт от таза и доворачивает, пятка опускается к удару
        w = (COPY - f) / (COPY - LAND)
        Lf = v3_keys.on_toe("Left", SL3, L_LANDP * w, l_land_dyaw() * w) if f > LAND else l_land()
    elif f <= PIV: Lf = v3_keys.on_toe("Left", SL, 22.0 * f / PIV, l_dyaw(f)) if f else SL
    elif f <= LAND - 1: Lf = l_air_path(f)
    else:                                            # последний тик: отвесно вниз на место (в осях корня, не за тазом)
        a = l_air_path(LAND - 1); b = l_land(); w = f - (LAND - 1)
        w = w * w
        Lf = tuple(x + (y - x) * w for x, y in zip(a, b))
    if f < PIV: Rf = step(R8, r_piv(PIV), f / PIV, 0.09, 0.0, (0.12, 0.95))     # вверх и под таз за PIV тиков
    elif f <= LAND: Rf = r_piv(f)
    elif LAND >= COPY:                               # пируэт до удара, потом шаг правой к бедру (как Swing1 9→12)
        Rf = RL if f >= LAND + RSTEP else step(r_piv(LAND), RL, (f - LAND) / RSTEP, 0.06, 0.0, (0.15, 0.95))
    else:
        Rf = RL if f >= COPY else step(r_piv(LAND), RL, (f - LAND) / (COPY - LAND), 0.06, 0.0, (0.15, 0.95))
    return Lf, Rf


def body_at(f):
    if f >= COPY:
        q = v3_keys.body_at(S1(f))
        Lf, Rf = feet(f)
        return dict(W=1.0, dz=q["dz"], pyaw=q["pyaw"] + 360, cyaw=q["cyaw"] + 360, lean=q["lean"], look=q["look"] + 360, gL=1.0, gR=1.0,
                    feet=dict(Left=Lf, Right=Rf))
    p = psi(f)
    Lf, Rf = feet(f)
    cy = p + _tab(TW, f)
    return dict(W=1.0, dz=_tab(DZ, f), pyaw=p, cyaw=cy, lean=_tab(LEAN, f), look=cy + _tab(LOOKD, f), gL=1.0, gR=1.0,
                feet=dict(Left=tuple(Lf), Right=tuple(Rf)))


P_FRONT = ((-0.05, 0.72, -0.69), (-0.05, -0.72, -0.69))
P_HIP = ((-0.45, 0.80, -0.40), (-0.05, -0.72, -0.69))
HIP_W = {0: 0.0, N: 0.0}                             # локти по P_FRONT/P_SPIN (P_HIP в конце даёт перескок решения левой)


P_SPIN = ((-0.10, 0.85, 0.15), (-0.05, -0.85, -0.35))   # в обороте локти в стороны (кисти впереди, предплечья мимо живота)
SPIN_W = {0: 0.0, 2: 1.0, min(LAND, CONTACT - 2): 1.0, CONTACT - 1: 0.0}
P_CROSS = (0.70, 0.35, -0.40)                         # левая кисть ушла вправо перед животом: левый локоть вперёд, не в живот
CROSS_W = {int(_P.get("cross0", 7)): 0.0, int(_P.get("cross0", 7)) + 1: 1.0, int(_P.get("cross1", 11)): 1.0, int(_P.get("cross1", 11)) + 1: 0.0}


def poles(f, p):
    w = _tab(HIP_W, f); sw = max(0.0, min(1.0, _tab(SPIN_W, f))) if f < CONTACT - 1 else 0.0
    out = {}
    for i, s in enumerate(("Left", "Right")):
        a = tuple(x + (y - x) * w for x, y in zip(P_FRONT[i], P_HIP[i]))
        a = tuple(x + (y - x) * sw for x, y in zip(a, P_SPIN[i]))
        if s == "Left" and min(CROSS_W) <= f <= max(CROSS_W):
            cw = max(0.0, min(1.0, _tab(CROSS_W, f)))
            a = tuple(x + (y - x) * cw for x, y in zip(a, P_CROSS))
        out[s] = rot(a, p["cyaw"])
    return out


def axw(f):
    return 0.30 if f <= CONTACT + 1 else 0.05


AXW = 0.30
GAP = 0.15
