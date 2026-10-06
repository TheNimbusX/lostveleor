"""Абордаж v2: пальцы (правка 02.10 по проверке: «кулака на цепи нет, пальцы не анимированы»).

Хват g на кисть: 0 — пальцы стойки серии сабли (правая — С-хват под саблю, левая — полуоткрытая),
g > 0 — к кулаку, g < 0 (только правая) — раскрыть ладонь (выпуск якоря, поза 2 листа).
Сустав сгибается вокруг своей оси X покоя поверх стойки: q = Rx(g · ход) @ q_стойки — поворот кости
от стойки ровно |g| · ход, по прямой (монотонно), значит скорость за тик = |Δg| · ход.
Ходы ограничены так, что кулак собирается не быстрее 35° за тик:
  * левая (рукоять цепи): сгиб до кулака ≤ 99° (3 тика по 33°), большой палец — к зеркалу С-хвата правой;
  * правая: кулак +33° сверх С-хвата, раскрытие −32° (по тику на каждое);
  * концевые кости *4 (без веса) не трогаются — их поворот в стойке остаётся как есть.
"""
import math
from mathutils import Quaternion
from ab_rig import M

FINGERS = ("Index", "Middle", "Ring", "Pinky")
L_TARGET = {1: 80.0, 2: 95.0, 3: 62.0}    # сгиб кулака левой от покоя по X (°)
L_MAX, R_FIST, R_OPEN = 99.0, 33.0, 32.0
X = (1.0, 0.0, 0.0)
FG = {}


def flex_x(q):
    """Доля поворота вокруг X (крутка вокруг оси сгиба), °."""
    a = 2 * math.degrees(math.atan2(q.x, q.w))
    return (a + 180) % 360 - 180


def mirror(q):
    """Поворот правой кости → левой (риг Mixamo зеркален по X): (w, x, −y, −z)."""
    return Quaternion((q.w, q.x, -q.y, -q.z))


def prepare(rig):
    pb = rig.dst.pose.bones
    FG.clear()
    for s in ("Left", "Right"):
        for f in FINGERS:
            for j in (1, 2, 3):
                n = M("%sHand%s%d" % (s, f, j))
                q0 = pb[n].matrix_basis.to_quaternion().copy()
                fx = flex_x(q0)
                if s == "Left":
                    add, opn = min(L_MAX, max(0.0, L_TARGET[j] - fx)), 0.0
                else:
                    add, opn = R_FIST, min(R_OPEN, max(0.0, fx - 4.0))
                FG[n] = dict(q0=q0, add=add, open=opn, side=s)
        for j in (1, 2, 3):
            n = M("%sHandThumb%d" % (s, j))
            q0 = pb[n].matrix_basis.to_quaternion().copy()
            d = dict(q0=q0, side=s, thumb=True)
            if s == "Left":     # большой палец левой — к зеркалу С-хвата правой (обхват рукояти)
                qt = mirror(pb[M("RightHandThumb%d" % j)].matrix_basis.to_quaternion())
                if qt.dot(q0) < 0: qt.negate()
                d["fist"] = qt
            FG[n] = d
    return FG


def grip_q(n, g):
    d = FG[n]
    q0 = d["q0"]
    if d.get("thumb"):
        if d["side"] == "Left" and g > 0: return q0.slerp(d["fist"], min(1.0, g))
        return q0.copy()
    a = g * d["add"] if g >= 0 else g * d["open"]
    return Quaternion(X, math.radians(a)) @ q0


def apply(rig, gL, gR):
    pb = rig.dst.pose.bones
    for n, d in FG.items():
        pb[n].rotation_quaternion = grip_q(n, gL if d["side"] == "Left" else gR)


def report():
    out = {}
    for n, d in FG.items():
        if d.get("thumb"):
            if "fist" in d:
                a = math.degrees(d["q0"].rotation_difference(d["fist"]).angle)
                out[n.replace("mixamorig:", "")] = round(min(a, 360 - a), 1)
            continue
        out[n.replace("mixamorig:", "")] = (round(flex_x(d["q0"]), 1), round(d["add"], 1), round(d["open"], 1))
    return out
