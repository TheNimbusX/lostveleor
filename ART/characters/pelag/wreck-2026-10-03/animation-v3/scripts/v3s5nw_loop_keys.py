"""Крушение v3 · Девятый вал, ChargeLoop («вертолёт», поза 8 листа B2): петля 12 кадров, кадр 12 = кадр 0.

Вход: в тик «над головой» (OverheadTick = Slam@7) — Sim начинает заряд, если третье нажатие ещё держат. Голова якоря
в Slam@7 позади героя на ~1,8 м, 27 м/с вправо: это уже начало горизонтального круга над головой. Петля продолжает его:
голова кружит над героем (период 12 тиков при скорости вида 1), кулаки — малый круг (≈ 0,16 м) перед макушкой, ВПЕРЕДИ
головы на ~70° (раскачка: противофаза-противовес неустойчива и гаснет — прогон gripopt_s5nw, все старты сходятся сюда).
Рукоять держат горизонтально, её рысканье постоянно (кулаки только переносятся по кругу) — цепь вертится у гнезда хвата.
Стопы — там же, где в Slam@7 (левая на месте выпада, правая сзади), но на всю стопу; таз низко — очень низкая широкая
стойка; кадр 0 петли — голова сзади (как Slam@7): отсюда выход ChargeRelease (фаза 0) идёт тем же путём, что Slam 7→13.
Верх тела крутится с «вертолётом»: грудь доворачивает за кулаками ±25° (с опережением 15°), таз ±8°, рукоять — за грудью;
иначе предплечья, перенося кулаки на дальнюю сторону круга, проходят сквозь волосы (перебор 06.10, nw9/sw3: x2 — 0 пересечений
треугольников; 6 «глубоких вершин» у прядей — ложные: по лучам все снаружи). Стопы не двигаются.
Окружение: V3S5NW_DZ / _PY / _CY / _LEAN / _SWAY (амплитуда доворота груди, °), _SIDE (наклон к хвату, °).
"""
import math, os
from s_lib import pchip
from wk_clips import rot
import v3_keys

CLIP = "Pelag_AN_Wreck2_ChargeLoop"
N = 12
LOOP = True                         # v3s5nw_author: кадр N = копия кадра 0 (замыкание петли без перескока решения IK)
SEAM = {}
HINT_FROM = ("Pelag_AN_Wreck2_Slam", 7)            # руки над головой — решения из Slam@7
FULL0 = True
STRIKE = None                       # петля: везде ≤ 35°/тик

E = lambda k, d: float(os.environ.get("V3S5NW_" + k, d))
DZ, PYAW, CYAW, LEAN = E("DZ", -0.11), E("PY", -35.0), E("CY", -14.0), E("LEAN", 4.0)
SWAY, SIDE, PSWAY, LEANAMP, SWAYLAG = E("SWAY", 25.0), E("SIDE", 0.0), E("PSWAY", 8.0), E("LEANAMP", 2.0), E("SWAYLAG", -15.0)
TH0 = E("TH0", 170.0)               # азимут головы якоря в кадре 0 (° atan2(x, z) корня Unity; 180 — прямо сзади), убывает 30°/кадр

R_BACK = (-0.36, -0.16, 0.136, -62.0, 0.0)         # правая сзади — как Slam (v3s4sl_keys), но на всю стопу
FEET = dict(Left=v3_keys.SL_LOCK, Right=R_BACK)


def head_az(f):
    """Азимут головы якоря (рад, atan2(x, z) корня Unity) в кадре f."""
    return math.radians(TH0 - 30.0 * f)


TG0 = E("TG0", 100.5)               # азимут кулаков на их круге в кадре 0 (° от центра круга), убывает 30°/кадр; голова отстаёт на ~70°
AXIS = tuple(float(x) for x in os.environ.get("V3S5NW_AXIS", "0.95,-0.10,-0.30").split(","))   # рукоять: левый кулак → правый (Unity)
AXIS_FOLLOWS = E("AXFOL", 1.0) > 0.5


def grip_az(f):
    """Азимут кулаков на их малом круге (рад)."""
    return math.radians(TG0 - 30.0 * f)


def fist_axis(f, target, ring):
    """Ось кулаков (левый → правый, Unity): рукоять горизонтально вправо, рысканье — за грудью (доворот SWAY), цепь вертится у гнезда."""
    n = math.sqrt(sum(c * c for c in AXIS)); a = [c / n for c in AXIS]
    d = math.radians(body_at(f)["cyaw"] - CYAW) if AXIS_FOLLOWS else 0.0       # + — влево
    c, s_ = math.cos(d), math.sin(d)
    return [a[0] * c - a[2] * s_, a[1], a[0] * s_ + a[2] * c]


def body_at(f):
    # грудь доворачивает к кулакам (азимут кулаков в осях «f, l»: + влево = −x), наклон к ним — боком и вперёд
    g = grip_az(f) + math.radians(SWAYLAG)            # корпус идёт за кулаками (SWAYLAG — запаздывание/опережение, °)
    gx, gz = math.sin(g), math.cos(g)                # Unity x (вправо), z (вперёд)
    toward_left = -gx                                 # + — кулаки слева
    cy = CYAW + SWAY * toward_left                    # грудь доворачивает за кулаками (верх тела крутится с «вертолётом»)
    py = PYAW + PSWAY * toward_left                   # таз — чуть, стопы стоят
    return dict(W=1.0, dz=DZ, pyaw=py, cyaw=cy, lean=LEAN + LEANAMP * gz, look=cy + 2.0, gL=1.0, gR=1.0,
                feet=dict(FEET), dside=SIDE * toward_left, dlean=0.0)


def post_body(rig, p):
    """Наклон к кулакам боком (позвоночник, таз и ноги стоят)."""
    from mathutils import Vector, Quaternion
    from s_lib import rotate_world
    from wk_rig import M
    if abs(p.get("dside", 0.0)) < 1e-4: return
    a = math.radians(p["cyaw"])
    fw = Vector((math.sin(a), -math.cos(a), 0.0))     # взгляд груди в осях Blender (вперёд — −Y, влево — +X; wk_rig.fl)
    for n, w in (("Spine", .35), ("Spine1", .35), ("Spine2", .30)):
        rotate_world(rig.dst, rig.dst.pose.bones[M(n)], Quaternion(fw, math.radians(-p["dside"] * w)), rig.P(n))


_PO = os.environ.get("V3S5NW_POLES", "0.45,0.88,0.30;0.35,-0.93,0.30")
P_OVER = tuple(tuple(float(x) for x in v.split(",")) for v in _PO.split(";"))   # локти в стороны и чуть вверх (над головой)


def poles(f, p):
    import wk_grip
    wk_grip.CLAV_K = E("CLAVK", 0.6); wk_grip.CLAV_MAX = E("CLAVMAX", 50.0)
    return {s: rot(P_OVER[i], p["cyaw"]) for i, s in enumerate(("Left", "Right"))}


AXW = E("AXW", 0.30)
GAP = 0.15
SMOOTH = (("RightShoulder", "RightArm", "RightForeArm", "RightHand"), 1, 0.35)


def handle_dir(f, target, to_r):
    """Ось кулаков без запечки: горизонтально к голове якоря (через ось вращения над макушкой)."""
    h = head_az(f)
    return [math.sin(h), -0.03, math.cos(h)]


def guess():
    """Путь хвата первого тела: малый круг перед макушкой (раскачка «вертолёта»). Оси корня Unity."""
    cx, cy, cz, r = E("GCX", 0.04), E("GCY", 1.71), E("GCZ", 0.30), E("GR", 0.16)
    out = []
    for f in range(N + 1):
        g = grip_az(f)
        out.append([round(cx + r * math.sin(g), 4), round(cy, 4), round(cz + r * math.cos(g), 4)])
    return out
