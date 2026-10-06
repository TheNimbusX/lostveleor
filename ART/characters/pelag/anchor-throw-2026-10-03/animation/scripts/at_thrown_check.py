"""Бросок якоря: проверка режима Thrown/Yank anchor-core (DESIGN §1.2, §4.1, §5) по трекам хвата клипов (grip/*.grip.json,
шаг A конвейера запечки — anchor_grip_export.py) и формуле головы Sim (Simulation.AnchorThrow.cs AnchorThrowHead).
python at_thrown_check.py <anim_dir> [out.json]

Цепь в бросках всегда натянута и прямая: кольцо рукояти в левой (grip) → кольцо головы на линии Sim; длина = расстояние.
Что меряем по тикам Sim (W = 2, F и R из дальности; вид — время Tick − 2 + Alpha, здесь целые тики и ¼ тика):
  #3  цепь сквозь тело: расстояние прямой хват→кольцо до осей капсул (торс r 0,22, бёдра/голени 0,10, плечи/предплечья 0,07)
      ≥ r − 1 см; левая кисть и предплечье (держат рукоять) и правая кисть (опора на цепи) не в счёт;
  #11 опорная рука: правая (точка опоры кисти) от цепи ≤ 6 см, пока обе руки на цепи (Fly, Yank, Haul 0–4), кисти 0,10–0,30 м;
  #DESIGN §6 хват: скорость ≤ 8 м/с, ускорение ≤ 150 м/с² (по времени Sim, с растяжкой клипов);
  ловля: правая кисть в тик ловли от линии возврата (хват → конец) ≤ 0,10 м и впереди хвата (кольцо приходит в ладонь);
  #4  растяжение: в Thrown/Yank длина = расстояние — растяжения нет по построению (проверяется только выдача ≥ 0).
Оси — корень Unity: x вправо, y вверх, z вперёд (Dir), метры тела 1,78 м.
"""
import sys, os, json, math

ANIM = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ANIM, "grip", "thrown-check.json")
P = "Pelag_AN_AnchorThrow_"
CLIPS = ["Throw", "Fly", "Yank", "Haul", "Catch"]
LAST = {"Throw": 5, "Fly": 3, "Yank": 2, "Haul": 6, "Catch": 9}
HAND, STEP, RET_STEP, BEND = 0.5, 1.0, 0.8, 2
G = {c: json.load(open(os.path.join(ANIM, "grip", P + c + ".grip.json"), encoding="utf-8")) for c in CLIPS}
NAMES = G["Throw"]["boneNames"]
CAPS = [("Hips", "Spine2", .22), ("Spine2", "Neck", .18), ("Neck", "Head", .10), ("Head", "HeadTop_End", .11),
        ("LeftUpLeg", "LeftLeg", .10), ("LeftLeg", "LeftFoot", .08), ("RightUpLeg", "RightLeg", .10), ("RightLeg", "RightFoot", .08),
        ("LeftArm", "LeftForeArm", .07), ("RightArm", "RightForeArm", .07), ("RightForeArm", "RightHand", .06)]


def V(a, b, t): return [a[i] + (b[i] - a[i]) * t for i in range(3)]
def sub(a, b): return [a[i] - b[i] for i in range(3)]
def dot(a, b): return sum(a[i] * b[i] for i in range(3))
def norm(a): return math.sqrt(dot(a, a))


def sample(clip, frame):
    """Трек хвата в дробный кадр (сэмплы ⅛ кадра, по прямой между ними — как вид между кадрами)."""
    ss = G[clip]["samples"]
    x = max(0.0, min(frame, ss[-1]["frame"])) * G[clip]["sub"]
    i = min(int(math.floor(x)), len(ss) - 2); t = x - i
    a, b = ss[i], ss[i + 1]
    return dict(grip=V(a["grip"], b["grip"], t), support=V(a["support"], b["support"], t),
                bones={n: V(a["bones"][k], b["bones"][k], t) for k, n in enumerate(NAMES)})


def seg_dist(p0, p1, q0, q1):
    """Наименьшее расстояние между отрезками p0p1 и q0q1 (+ параметр на p)."""
    best = (1e9, 0.0)
    for k in range(41):
        u = k / 40.0
        p = V(p0, p1, u)
        d = sub(q1, q0); L2 = max(1e-12, dot(d, d))
        w = max(0.0, min(1.0, dot(sub(p, q0), d) / L2))
        q = V(q0, q1, w)
        dd = norm(sub(p, q))
        if dd < best[0]: best = (dd, u)
    return best


def prog(k, R):
    if k <= 0: return 0.0
    if k >= R: return 1.0
    return k * (R + BEND * k) / ((BEND + 1) * R * R)


def virtual(F):
    if F >= 3: return [0.0, 1.0, 2.0, 3.0] + [3.0 + 3.0 * j / (F - 2) for j in range(1, F - 1)]
    return [0.0, 1.0, 3.5, 6.0] if F == 2 else [0.0, 1.0, 6.0]


HAUL = {8: [0, 1, 2, 3, 4, 5, 6], 7: [0, 1, 2, 3, 4.5, 6], 6: [0, 1.5, 3, 4.5, 6], 5: [3, 4, 5, 6], 4: [3, 4.5, 6]}


def layout(W, F, R):
    out = [("Throw", 2.0 * k / W) for k in range(W + 1)]
    for v in virtual(F)[1:]:
        out.append(("Throw", 2.0 + v) if v <= 3.0 else (("Fly", v - 3.0) if v < 6.0 else ("Yank", 0.0)))
    out.append(("Yank", 1.0))
    h = HAUL[R]
    out.append(("Haul", float(h[0])) if h[0] > 0 else ("Yank", 2.0))
    out += [("Haul", float(x)) for x in h[1:-1]] + [("Catch", float(j)) for j in range(10)]
    return out


def head(tick, W, F, s, reach, T, R, catch, hand_pt):
    """Кольцо головы: Sim XZ (AnchorThrowHead), высота — вид (полёт 1,3 → 0,9 м; возврат низко 0,45; к ловле — к руке)."""
    if tick <= W: return None
    k = tick - W
    if k < F:
        at = min(reach, HAND + s * k); return [0.0, 1.3 + (0.9 - 1.3) * at / reach, at]
    if tick <= T: return [0.0, 0.9, reach]
    if tick >= catch: return [hand_pt[0], hand_pt[1], hand_pt[2]]
    p = prog(tick - T, R)
    z = reach + (HAND - reach) * p
    return [0.0, 0.45 + (hand_pt[1] - 0.45) * max(0.0, (p - 0.75) / 0.25), z]


report = dict(rule=__doc__.splitlines()[2:12], scenarios=[])
LIM = dict(body_clear_min=-0.01, support_max=0.06, hands_along=[0.10, 0.30], grip_speed_max=8.0, grip_acc_max=150.0, catch_palm_max=0.10)
for reach in (9.5, 7.0, 5.0, 4.0, 2.0, 1.0):
    W = 2
    F = max(1, min(10, math.ceil((reach - HAND) / STEP)))
    s = (reach - HAND) / F
    R = max(4, min(8, round((reach - HAND) / RET_STEP)))
    T = W + F + 1; catch = T + R
    lay = layout(W, F, R)
    sc = dict(reach=reach, W=W, F=F, R=R, taut=T, catch=catch, ticks=[])
    wst = dict(body_clear=(9.0, None), support=(0.0, None), hands=(9.0, 0.0), grip_speed=(0.0, None), grip_acc=(0.0, None),
               grip_speed_60=(0.0, None), catch=None)
    grips = []
    for i, (c, f) in enumerate(lay):
        smp = sample(c, f)
        grips.append(smp["grip"])
        if len(grips) >= 2:
            v = norm(sub(grips[-1], grips[-2])) * 30.0
            if v > wst["grip_speed"][0]: wst["grip_speed"] = (round(v, 2), (i, c, round(f, 2)))
        if len(grips) >= 3:
            acc = norm(sub(sub(grips[-1], grips[-2]), sub(grips[-2], grips[-3]))) * 900.0
            if acc > wst["grip_acc"][0]: wst["grip_acc"] = (round(acc, 1), (i, c, round(f, 2)))
        if i > 0:                                        # вид на 60 к/с: полтика по клипу (в пределах одного клипа)
            (c0, f0) = lay[i - 1]
            if c0 == c:
                gm = sample(c, (f0 + f) / 2)["grip"]
                v = max(norm(sub(gm, grips[-2])), norm(sub(grips[-1], gm))) * 60.0
                if v > wst["grip_speed_60"][0]: wst["grip_speed_60"] = (round(v, 2), (i, c, round(f, 2)))
        ring = head(i, W, F, s, reach, T, R, catch, smp["support"])
        row = dict(tick=i, clip=c, frame=round(f, 2))
        both = c in ("Fly", "Yank") or (c == "Haul" and f <= 4.0) or (c == "Throw" and f >= 4.0)
        if ring is not None and i < catch:
            g = smp["grip"]; b = smp["bones"]
            row["payout"] = round(norm(sub(ring, g)), 3)
            clear = []
            for a_, b_, r in CAPS:
                q0, q1 = b[a_], b[b_]
                if both and a_ == "RightForeArm":        # кулак на цепи: запястье рядом с цепью по построению — 60 % предплечья от локтя
                    q1 = V(q0, q1, 0.6)
                d, w = seg_dist(g, ring, q0, q1)
                clear.append((round(d - r, 3), a_ + "-" + b_))
            cl = min(clear)
            row["body_clear"] = cl
            if cl[0] < wst["body_clear"][0]: wst["body_clear"] = (cl[0], (i, c, round(f, 2), cl[1]))
            if both:
                d = sub(ring, g); L = norm(d); dn = [x / L for x in d]
                sp = sub(smp["support"], g); along = dot(sp, dn)
                off = norm(sub(sp, [x * along for x in dn]))
                row["support_off"] = round(off, 3); row["hands_along"] = round(along, 3)
                if off > wst["support"][0]: wst["support"] = (round(off, 3), (i, c, round(f, 2)))
                wst["hands"] = (min(wst["hands"][0], round(along, 3)), max(wst["hands"][1], round(along, 3)))
        if i == catch:
            g = smp["grip"]; end = [0.0, 0.45, reach]
            d = sub(end, g); L = norm(d); dn = [x / L for x in d]
            sp = sub(smp["support"], g); along = dot(sp, dn)
            off = norm(sub(sp, [x * along for x in dn]))
            row["catch_palm_off"] = round(off, 3); row["catch_palm_along"] = round(along, 3)
            wst["catch"] = (round(off, 3), round(along, 3))
        sc["ticks"].append(row)
    ok = dict(body_clear=wst["body_clear"][0] >= LIM["body_clear_min"], support=wst["support"][0] <= LIM["support_max"],
              hands=LIM["hands_along"][0] <= wst["hands"][0] and wst["hands"][1] <= LIM["hands_along"][1],
              grip_speed=wst["grip_speed"][0] <= LIM["grip_speed_max"], grip_acc=wst["grip_acc"][0] <= LIM["grip_acc_max"],
              catch=wst["catch"][0] <= LIM["catch_palm_max"] and wst["catch"][1] > 0)
    sc["worst"] = wst; sc["pass"] = ok
    report["scenarios"].append(sc)
    print("THROWN %4.1f m F%-2d R%d %s %s" % (reach, F, R, "PASS" if all(ok.values()) else "FAIL " + ",".join(k for k, v in ok.items() if not v),
                                             json.dumps({k: v for k, v in wst.items()}, ensure_ascii=False)))
report["limits"] = LIM
report["pass_main"] = all(all(sc["pass"].values()) for sc in report["scenarios"] if sc["reach"] >= 4.0)
open(OUT, "w", encoding="utf-8").write(json.dumps(report, indent=1, ensure_ascii=False))
print("THROWN", "PASS" if report["pass_main"] else "FAIL", "(дальности 4–9,5 м; стены 1–2 м — для сведения)")
