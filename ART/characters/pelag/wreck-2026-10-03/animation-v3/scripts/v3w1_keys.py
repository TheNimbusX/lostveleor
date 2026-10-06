"""Крушение v3, Wait1: петля верха тела в окне после маха 1 (12 кадров, кадр 12 = кадр 0 = Swing1 кадр 12).

Ноги, таз и присед — ровно кадр 12 Swing1 (поза 3, правая уже шагнула; в игре маска — руки, грудь, голова).
Хват — путь gripopt_s2 --wait 2 против живого маятника (голова не запекается): кисти у левого бедра уходят
влево-вниз (тормозят якорь: голова уходит за спину низко и садится на землю справа, цепь проходит позади корпуса),
потом возвращаются. Грудь чуть доворачивает за кистями (рысканье ≤ 6°), взгляд за грудью.
Рукоять в кулаках держит ось рукояти, а не цепи: цепь живая и в каждом проходе петли идёт в другую сторону —
правый кулак на рукояти в 0,12–0,15 м от левого, цепь гнётся у кулака.
"""
import math
from s_lib import pchip
from wk_clips import rot
import v3_keys

CLIP = "Pelag_AN_Wreck2_Wait1"
N = 12
SEAM = {0: ("Pelag_AN_Wreck2_Swing1", 12), 12: ("Pelag_AN_Wreck2_Swing1", 12)}   # кадры-копии (стык и замыкание петли)
HINT_FROM = ("Pelag_AN_Wreck2_Swing1", 12)                                        # решения рук для первого кадра
K12 = v3_keys.KEYS[12]
# рысканье груди сверх кадра 12 Swing1 (°, + влево), наклон вперёд и влево только позвоночником (таз и ноги — кадр 12):
# кисти уходят вниз за левое бедро — грудь доворачивает влево и клонится к ним: якорь проходит за спиной низко,
# цепь — позади корпуса и под тазом, голова садится на землю справа и тормозит; потом обратно в позу 3
DCY = {0: 0.0, 2: 7.0, 4: 16.0, 6: 20.0, 8: 13.0, 10: 4.0, 12: 0.0}
DLEAN = {0: 0.0, 2: 8.0, 4: 18.0, 6: 21.0, 8: 13.0, 10: 3.0, 12: 0.0}
DSIDE = {0: 0.0, 2: 4.0, 4: 10.0, 6: 12.0, 8: 8.0, 10: 2.0, 12: 0.0}   # наклон влево (левое плечо ниже — кисть к земле за левым бедром)
POLE_L = (-0.45, 0.80, -0.40)          # локоть левой наружу-назад, как Swing1 с кадра 10
P_FRONT_R = (-0.05, -0.72, -0.69)
AXW = 0.05
SMOOTH = (("RightShoulder", "RightArm", "RightForeArm", "RightHand"), 2, 0.5)   # правая на рукояти — без рывков
GAP = 0.19


def _lerp_tab(tab, f):
    xs = sorted(tab)
    return pchip([(x, tab[x]) for x in xs], f)


def body_at(f):
    dz, pyaw, cyaw, lean, look, R = K12
    dc = _lerp_tab(DCY, f)
    return dict(W=1.0, dz=dz, pyaw=pyaw, cyaw=cyaw + dc, lean=lean, look=look + 0.8 * dc, gL=1.0, gR=1.0,
                feet=dict(Left=v3_keys.SL_LOCK, Right=tuple(R)), dlean=_lerp_tab(DLEAN, f), dside=_lerp_tab(DSIDE, f))


def post_body(rig, p):
    """Наклон позвоночником (Spine/Spine1/Spine2), таз и ноги не трогаются — маска верха тела."""
    from mathutils import Vector, Quaternion
    from s_lib import rotate_world
    from wk_rig import M
    for n, w in (("Spine", .35), ("Spine1", .35), ("Spine2", .30)):
        rotate_world(rig.dst, rig.dst.pose.bones[M(n)], Quaternion(Vector((1, 0, 0)), math.radians(p["dlean"] * w)), rig.P(n))
        rotate_world(rig.dst, rig.dst.pose.bones[M(n)], Quaternion(Vector((0, 1, 0)), math.radians(p["dside"] * w)), rig.P(n))


def poles(f, p):
    return {"Left": rot(POLE_L, p["cyaw"]), "Right": rot(P_FRONT_R, p["cyaw"])}


HANDLE_W = 0.55       # доля поворота оси рукояти к правому плечу в середине петли (правая дотягивается до рукояти)


def handle_dir(f, base, to_r):
    """Ось рукояти (Unity, от левого кулака к правому): на стыке — ось кадра 12 Swing1, к середине петли
    поворачивается к правому плечу (левая уходит влево-назад — правая остаётся на рукояти, а не тянется за ней)."""
    w = HANDLE_W * math.sin(math.pi * f / N)
    v = [a + (b - a) * w for a, b in zip(base, to_r)]
    n = math.sqrt(sum(c * c for c in v))
    return [c / n for c in v]
