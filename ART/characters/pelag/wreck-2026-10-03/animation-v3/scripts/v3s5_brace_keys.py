"""Крушение v3, «Якорная броня» — раскладка позы 10 по кадрам для v3s5_brace.py (поверх принятой базы).

Поза 10 листа B2: стойка шире и ниже, стопы плашмя, колени наружу, подбородок вниз; хват — база (якорь тот же).
* Таз: вниз до потолка смещения таза 0,495 длины корпуса (Build 0,5), но не дальше, чем позволяют руки до кистей базы
  (в подъёме Slam над голову руки прямые — там таз как в базе). Сглажено под потолком.
* Правая стопа шире: в упоре сзади (Swing1 0–8, Slam 5–17) на 5 см назад и 12 см наружу, после шага к бедру (Swing1 12,
  Swing2 17–19) — на 10 см наружу; смена сдвига — только пока стопа в воздухе по шагам базы. Пятка в упоре — вдвое ниже базы.
  Пируэт Swing2 3–14 — подушечка базы (как есть).
* Левая стопа «прибита» к месту выпада базы (Swing1 кадр 0): где база её не дотягивала (висела до 4–9 см), она стоит на
  земле; если нога короче — пятка вверх вокруг подушечки (носок стоит, не едет).
* Колени наружу 10°, подбородок вниз 4,5° (голова за грудью ±5° — предел).
* Стыки: Swing2_Braced 0 = Swing1_Braced 8, Slam_Braced 0 = Swing2_Braced 15 (копии снимков); Slam_Braced 24 = Slam 24 (стойка).
"""
P = "Pelag_AN_Wreck2_"
O_R8 = (-0.06, -0.16)          # правая в упоре сзади (Swing1 0–8, = Swing2 0)
O_LAND = (0.0, -0.16)          # правая у бедра после шага (Swing1 12, Swing2 17–19)
O_BACK = (-0.02, -0.12)        # правая в упоре выпада Slam 5–17 (над головой таз высоко — нога короче)
K_HEEL = 0.5
KNEE = 18.0
CHIN = 4.5


def ss(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def mix(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def z(): return (0.0, 0.0)


def s1_feet(f, s):
    if s == "Left": return dict(mode="pin", ref="Left")
    if f <= 8: return dict(mode="base", off=O_R8, k={5: 0.6, 6: 0.7, 7: 0.85, 8: 1.0}.get(f, K_HEEL))   # к стыку пятка как в базе
    if f <= 11: return dict(mode="base", off=mix(O_R8, O_LAND, {9: 0.25, 10: 0.6, 11: 0.92}[f]))
    return dict(mode="base", off=O_LAND)


def s2_feet(f, s):
    if s == "Left":
        if f >= 14: return dict(mode="pin", ref="Left")
        if f == 1: return dict(mode="hover", ref="Left", h=0.045)        # отрыв отвесно: над местом выпада
        if f in (2, 3): return dict(mode="blend", ref="Left", w={2: 0.45, 3: 0.15}[f])     # дальше — к пути базы
        if f == 13: return dict(mode="hover", ref="Left", h=0.03)         # над местом посадки, потом отвесно вниз
        return dict(mode="base")
    if f <= 2: return dict(mode="base", off=mix(O_R8, z(), {0: 0.0, 1: 0.08, 2: 0.55}[f]))      # сдвиг — пока стопа в воздухе
    if f <= 14: return dict(mode="base")
    if f <= 16: return dict(mode="base", off=mix(z(), O_LAND, {15: 0.4, 16: 0.95}[f]))
    return dict(mode="base", off=O_LAND)


SLAM0 = mix(z(), O_LAND, 0.4)            # правая в Swing2_Braced 15 (в шаге)


def sl_feet(f, s):
    if s == "Left":                                  # над головой таз высоко (руки прямые) — передняя стопа отрывается, как в базе,
        if f == 9: return dict(mode="blend", ref="Left", w=0.5)       # и садится с пятки к удару (притоп)
        return dict(mode="pin", ref="Left") if 10 <= f <= 21 else dict(mode="base")
    if f <= 4: return dict(mode="base", off=mix(SLAM0, O_BACK, {0: 0.0, 1: 0.1, 2: 0.45, 3: 0.8, 4: 0.98}[f]))
    if f <= 17: return dict(mode="base", off=O_BACK, k=1.0 if f <= 9 else (0.75 if f == 10 else K_HEEL))
    if f <= 20: return dict(mode="base", off=mix(O_BACK, z(), {18: 0.05, 19: 0.5, 20: 0.97}[f]))
    return dict(mode="base")


SL_CHIN = {1: 0.8, 2: 0.45, 3: 0.1, 4: 0.0, 5: 0.0, 6: 0.0, 7: 0.1, 8: 0.45, 9: 0.8}   # кулаки над головой — голова не кивает в руки
EXIT = {17: 1.0, 18: 0.9, 19: 0.75, 20: 0.55, 21: 0.35, 22: 0.18, 23: 0.06, 24: 0.0}
LREF = {"Left": (P + "Swing1", 0)}

CFG = {
    P + "Swing1_Braced": dict(base=P + "Swing1", N=12, STRIKE=(0, 10), pin_ref=LREF, feet=s1_feet,
                              knee=lambda f, s: KNEE, chin=lambda f: CHIN),
    P + "Swing2_Braced": dict(base=P + "Swing2", N=19, STRIKE=(0, 17), pin_ref=LREF, feet=s2_feet,
                              SEAM={0: (P + "Swing1_Braced", 8)},
                              knee=lambda f, s: (4.0 if s == "Left" and 4 <= f <= 13 else KNEE), chin=lambda f: CHIN),
    P + "Slam_Braced": dict(base=P + "Slam", N=24, STRIKE=(0, 14), pin_ref=LREF, feet=sl_feet,
                            SEAM={0: (P + "Swing2_Braced", 15)}, IDENT=(24,), EXIT_W=EXIT,
                            LEG_INTERP={"Right": {1: (0, 2, 0.5)}},       # правая в воздухе: 0→2 без рывка стопы
                            knee=lambda f, s: KNEE * EXIT.get(f, 1.0),
                            chin=lambda f: CHIN * EXIT.get(f, 1.0) * SL_CHIN.get(f, 1.0)),
}
