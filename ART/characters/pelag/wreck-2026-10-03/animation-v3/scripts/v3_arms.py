"""Крушение v3: руки на рукояти и цепи по пути хвата запечки (gripopt plan.json).

Левая: ГНЕЗДО ХВАТА (то, что читают экспорт хвата и риг: mixamorig:LeftHand × grip_socket.json, 0,0819 м по оси кисти)
ставится ровно в точку пути; ось кулака «мизинец→указательный» — по цепи (к кольцу запечённой головы).
Правая: на цепи, кулак на оси хват→кольцо на GAP за левым кулаком, ось кулака — по цепи.
Решение руки — как wk_grip/wk_arm (локоть шарниром, пронация tv, кисть wd/wf, поворот локтя sv), но перебор в окне
вокруг решения прошлого кадра: кадры идут подряд без перескоков.
"""
import math, bpy
from mathutils import Vector, Quaternion
from wk_rig import M, fl
import wk_arm, wk_grip
from s_lib import bone_world_rot

K = 0.98881                      # единица Blender (риг в масштабе 1) → метры Unity (blenderToRoot экспорта / 100)
SOCK = Vector((0.0, 0.0819, 0.02184)) / K     # grip_socket.json v2, оси кисти (X-зеркало S на нуле не меняет)
GAP = 0.15
SV_LIM = 60.0                    # поворот локтя вокруг оси плечо–кисть, ° (дальше — выворот)
AXW = [0.30]                      # вес оси кулака сверх 10° (цепь выходит из кулака, гнётся у кольца — точность оси не нужна)


def to_unity(v):
    return [K * -v.x, K * v.z, K * -v.y]


def from_unity(u):
    return Vector((-u[0] / K, -u[2] / K, u[1] / K))


def socket(rig, s="Left"):
    return rig.P(s + "Hand") + wk_arm.wrot(rig, s + "Hand") @ SOCK


def _search(rig, s, H, pole, D, hint, target, rng, cont=1.0):
    """Перебор (sv, tv, wd, wf) по сетке rng = ((lo, hi, step) × 4); cont — вес близости к hint (решение прошлого кадра)."""
    a = rig.ARM[s]; F = wk_grip.FIST[s]
    ax = (H - rig.P(s + "Arm")).normalized()
    h_sv, h_tv, h_wd, h_wf = hint
    lo, hi = F["tv"]
    best = None
    (s0, s1, ss), (t0, t1, ts), (d0, d1, ds), (f0, f1, fs) = rng
    sv = max(s0, -SV_LIM)
    while sv <= min(s1, SV_LIM) + 1e-6:
        S, E, H2, Ru, Rf0, fa = wk_arm.arm_frame(rig, s, H, Quaternion(ax, math.radians(sv)) @ pole)
        tv = max(t0, lo)
        while tv <= min(t1, hi) + 1e-6:
            Rf = Quaternion(fa, math.radians(tv)).to_matrix() @ Rf0
            wd = max(d0, -wk_grip.WD_LIM)
            while wd <= min(d1, wk_grip.WD_LIM) + 1e-6:
                wf = max(f0, -wk_grip.WF_LIM)
                while wf <= min(f1, wk_grip.WF_LIM) + 1e-6:
                    Rh = Rf @ wk_arm.wrist(a["Qh"], wd, wf)
                    ang = math.degrees((Rh @ F["bax"]).angle(D))
                    ang = min(ang, 180.0 - ang + wk_grip.REVERSE)
                    reach = max(0.0, (target - Rh @ F["off"] - S).length - 0.985 * (a["L1"] + a["L2"])) * 400.0
                    cost = (AXW[0] * max(0.0, ang - 10.0) + .03 * ang + reach
                            + cont * (.12 * abs(tv - h_tv) + .08 * abs(wd - h_wd) + .08 * abs(wf - h_wf) + .1 * abs(sv - h_sv))
                            + .03 * (abs(wd) + abs(wf)) + .03 * abs(sv))
                    if best is None or cost < best[0]: best = (cost, sv, tv, wd, wf, ang)
                    wf += fs
                wd += ds
            tv += ts
        sv += ss
    return best


NARROW = [False]


def _best(rig, s, H, pole, D, hint, target, full, center=None):
    if full:
        b = _search(rig, s, H, pole, D, hint, target, ((-45, 45, 15), (-60, 90, 10), (-34, 34, 8.5), (-40, 40, 10)), cont=.15)
        hint2 = b[1:5]
    else:
        hint2 = center or hint
    h = hint2
    if NARROW[0]:
        return _search(rig, s, H, pole, D, hint, target, ((h[0] - 2, h[0] + 2, 1), (h[1] - 3, h[1] + 3, 1), (h[2] - 2, h[2] + 2, 1),
                                                           (h[3] - 2, h[3] + 2, 1)))
    return _search(rig, s, H, pole, D, hint, target, ((h[0] - 10, h[0] + 10, 5), (h[1] - 12, h[1] + 12, 3), (h[2] - 8, h[2] + 8, 4),
                                                       (h[3] - 10, h[3] + 10, 5)))


def solve(rig, s, fist_target, D, pole_t, hint, full=True):
    """Кулак s в fist_target (мир), ось по D; окно перебора вокруг hint. → решение, промах кулака, угол оси."""
    F = wk_grip.FIST[s]; a = rig.ARM[s]
    D = D.normalized()
    Rd = bone_world_rot(rig.dst, rig.dst.pose.bones[M("Spine2")]).to_quaternion() @ rig.B["chest_rot"].inverted()
    pole = fl(*pole_t).normalized()
    sh = rig.dst.pose.bones[M(s + "Shoulder")]
    if s not in CLAV: CLAV[s] = sh.matrix_basis.copy()
    sh.matrix_basis = CLAV[s]; bpy.context.view_layer.update()
    Rh = wk_arm.wrot(rig, s + "Hand")
    H = fist_target - Rh @ F["off"]
    wk_grip._clavicle(rig, s, H, Rd)
    sol = hint
    for it in range(3):
        _, sv, tv, wd, wf, _ = _best(rig, s, H, pole, D, hint, fist_target, full and it == 0, center=sol)
        sol = (float(sv), float(tv), float(wd), float(wf))
        ax = (H - rig.P(s + "Arm")).normalized()
        S, E, H2, Ru, Rf0, fa = wk_arm.arm_frame(rig, s, H, Quaternion(ax, math.radians(sv)) @ pole)
        Rf = Quaternion(fa, math.radians(tv)).to_matrix() @ Rf0
        Rh = Rf @ wk_arm.wrist(a["Qh"], wd, wf)
        H = fist_target - Rh @ F["off"]
    sv, tv, wd, wf = sol
    wk_arm.solve(rig, s, H, pole, None, tv, wd, wf, sv)
    ang = math.degrees(wk_grip.fist_axis(rig, s).angle(D))
    return sol, (wk_grip.fist_center(rig, s) - fist_target).length, min(ang, 180 - ang)


CLAV = {}


def place_hands(rig, grip_u, ring_u, poles, hints, full=False, gaps=(0.12, 0.15, 0.18, 0.21), narrow=False):
    """Левое гнездо в grip_u, ось по цепи к ring_u (Unity), правый кулак на цепи (зазор из gaps с меньшим промахом).
    Вызывать сразу после wk_pose.body (ключицы берутся из позы тела этого кадра)."""
    CLAV.clear(); NARROW[0] = narrow
    if narrow: gaps = (GAP,)
    g = from_unity(grip_u); ring = from_unity(ring_u)
    D = (ring - g).normalized()
    fist = g + (wk_grip.fist_center(rig, "Left") - socket(rig))        # первое приближение: смещение гнездо→кулак
    out = {}
    h = hints["Left"]
    for it in range(4):
        solL, missL, axL = solve(rig, "Left", fist, D, poles["Left"], h, full=full and it == 0)
        err = g - socket(rig)
        if err.length < 0.002: break
        fist = fist + err
        if it == 0: h = solL
    out["Left"] = (solL, (socket(rig) - g).length, axL)
    G = wk_grip.fist_center(rig, "Left")
    e = wk_grip.fist_axis(rig, "Left")
    if e.dot(D) < 0: e = -e
    axis = (e + D).normalized()                       # правый кулак — на оси рукояти/цепи
    best = None
    for gap in gaps:
        r = solve(rig, "Right", G + axis * gap, D, poles["Right"], hints["Right"], full=full)
        score = r[1] * 100 + abs(gap - GAP) * 5
        if best is None or score < best[0]: best = (score, gap, r)
    _, gap, r = best
    out["Right"] = solve(rig, "Right", G + axis * gap, D, poles["Right"], hints["Right"], full=full)
    out["gap"] = gap
    return out
