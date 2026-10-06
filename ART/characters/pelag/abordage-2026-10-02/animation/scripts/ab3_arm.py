"""Абордаж v3: правая рука ключа как в ab_keys.clips.K (плечо u, сгиб, плоскость локтя стойки, перенесённая
кратчайшим поворотом плеча) плюс поворот плоскости локтя вокруг плеча sw (°) — для позы «через плечо», где
предплечье складывается вверх, а не вперёд. sw = 0 — ровно K принятых клипов."""
import math
from ab_keys import pose, arm, crot, nrm, SU, SB


def K3(B, dz, pyaw, cyaw, lean, L, R, u, flex, hL, gL, gR, wd=32.0, sw=0.0, look=0.0):
    from mathutils import Vector, Quaternion
    cy0 = B["cyaw"]
    us, bs = Vector(crot(SU, cyaw - cy0)).normalized(), Vector(crot(SB, cyaw - cy0))
    n0 = us.cross(bs).normalized()
    uu = Vector(nrm(u))
    n = us.rotation_difference(uu) @ n0
    if abs(sw) > 1e-6:
        n = Quaternion(uu, math.radians(sw)) @ n
    v, pole = arm(tuple(uu), flex, tuple(n.cross(uu)))
    p = pose(dz, pyaw, cyaw, lean, L, R, v, hL, arm=(0.0, 0.0, wd, 0.0), pR=pole, wp=1.0, gL=gL, gR=gR, look=look)
    return p


def chest_dir(cyaw, az, el):
    """Направление в осях корня по азимуту az от «вправо от груди» (+ — вперёд груди) и подъёму el (°)."""
    a = math.radians(cyaw - 90 + az); e = math.radians(el)
    return (math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e))


def sw_for(B, cyaw, u, flex, fd, step=5):
    """Поворот плоскости локтя sw (°), при котором предплечье K3 ближе всего к направлению fd (оси корня)."""
    from mathutils import Vector, Quaternion
    cy0 = B["cyaw"]
    us, bs = Vector(crot(SU, cyaw - cy0)).normalized(), Vector(crot(SB, cyaw - cy0))
    n0 = us.cross(bs).normalized()
    uu = Vector(nrm(u)); n1 = us.rotation_difference(uu) @ n0
    want = Vector(nrm(fd)); a = math.radians(flex); best = None
    for sw in range(-180, 180, step):
        n = Quaternion(uu, math.radians(sw)) @ n1
        b = n.cross(uu)
        f = uu * math.cos(a) + b * math.sin(a)
        c = f.angle(want)
        if best is None or c < best[0]: best = (c, sw)
    return float(best[1])
