"""Бросок якоря: проверка пределов по снимкам позы при сборке (замеры at_check Абордажа v2 / sq_check Шквала):

  * скорость костей за тик во ВСЕХ раскладках Sim (замах W = 2–3, полёт F = 1–10, возврат R = 4–8): путь кости
    за тик — по всем кадрам, которые тик проходит, и через стыки клипов (Throw → Fly → Yank → Haul → Catch);
    предел 35°, правая рука (RightArm/ForeArm/Hand) в тики удара (выпуск и первый тик полёта) — 70°;
  * стопы: под землёй нигде; точки касания земли не едут (корень стоит весь навык: Sim героя не двигает);
  * пересечения: кисть/предплечье с бёдрами и голенями обеих сторон, бёдра, голени, стопы, торс, голова;
  * наклон: бросок вперёд ≤ 35°, назад только замах (≥ −8°), натяг и тяга НАЗАД 12–25° (спека §4), скрутка ≤ 45°,
    крутка предплечья ≤ 70°, голова за грудью, таз ≤ 0,5 длины корпуса.
"""
import math, bpy
from mathutils import Vector
from at_rig import M, SIDES, yaw_of

HEIGHT = 1.80
LIM, LIM_STRIKE = 35.0, 70.0
ARM_R = ("RightArm", "RightForeArm", "RightHand")
P = "Pelag_AN_AnchorThrow_"
THROW, FLY, YANK, HAUL, CATCH = P + "Throw", P + "Fly", P + "Yank", P + "Haul", P + "Catch"
ORDER = [THROW, FLY, YANK, HAUL, CATCH]
LAST = {THROW: 5, FLY: 3, YANK: 2, HAUL: 6, CATCH: 9}
RELEASE = 2                          # кадр выпуска (Throw)
HOLD, EXIT = 3, 6                    # удержание и выход (Simulation: как Абордаж/Шквал)
PAIRS = [(("RightHand",), "LeftThigh"), (("RightForeArm",), "LeftThigh"), (("LeftHand",), "RightThigh"), (("LeftForeArm",), "RightThigh"),
         (("RightHand", "RightForeArm"), "RightThigh"), (("LeftHand", "LeftForeArm"), "LeftThigh"),
         (("RightHand", "RightForeArm"), "LeftShin"), (("RightHand", "RightForeArm"), "RightShin"),
         (("LeftHand", "LeftForeArm"), "LeftShin"), (("LeftHand", "LeftForeArm"), "RightShin"),
         (("LeftShin",), "RightShin"), (("LeftFoot",), "RightShin"), (("RightFoot",), "LeftShin"), (("LeftFoot",), "RightFoot"),
         (("RightHand", "RightForeArm"), "LeftForeArm"), (("RightHand", "RightForeArm"), "LeftHand"),
         (("RightHand", "RightForeArm"), "torso"), (("LeftHand", "LeftForeArm"), "torso"), (("RightHand", "RightForeArm"), "head"),
         (("LeftHand", "LeftForeArm"), "head")]


# ------------------------------------------------------------------ раскладки Sim → кадры клипов
def windup_retime(W):
    """Тики каста 0..W → кадры Throw 0..2 (W = 2 — 1:1; курсор за спиной W = 3 — те же кадры на 3 тика)."""
    return [2.0 * k / W for k in range(W + 1)]


def flight_retime(F):
    """Тики от выпуска u = 0..F+1 (F+1 — натяг T) → виртуальный кадр v: v 0..3 = Throw 2..5, v 3..6 = Fly 0..3.
    F ≥ 3: три тика проводки 1:1, упор Fly растянут на F − 2 тиков; F = 2: 0, 1, 3,5, 6; F = 1: 0, 1, 6."""
    if F >= 3: return [0.0, 1.0, 2.0, 3.0] + [round(3.0 + 3.0 * j / (F - 2), 4) for j in range(1, F - 1)]
    if F == 2: return [0.0, 1.0, 3.5, 6.0]
    return [0.0, 1.0, 6.0]


def haul_retime(R):
    """Тики возврата k = 2..R → кадры Haul (Yank — k = 0, 1, 2 всегда 1:1; Haul 0 = Yank 2 = Haul 3 — та же поза).
    R = 8 — 1:1 (два перехвата), 7 и 6 — сжатие ≤ 1,5 кадра за тик, 5 и 4 — один перехват с кадра 3."""
    return {8: [0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0], 7: [0.0, 1.0, 2.0, 3.0, 4.5, 6.0], 6: [0.0, 1.5, 3.0, 4.5, 6.0],
            5: [3.0, 4.0, 5.0, 6.0], 4: [3.0, 4.5, 6.0]}[R]


def layout(W, F, R):
    """Весь каст по тикам Sim: [(тик, клип, кадр, тик_удара)], C = 0 — тик нажатия (спека §2.2)."""
    out = [(k, THROW, f, False) for k, f in enumerate(windup_retime(W))]
    out[-1] = (W, THROW, 2.0, True)                    # выпуск: тик C+W пришёл из удара руки
    for u, v in enumerate(flight_retime(F)):
        if u == 0: continue
        clip, fr = (THROW, 2.0 + v) if v <= 3.0 else (FLY, v - 3.0)
        if v >= 6.0: clip, fr = YANK, 0.0              # натяг T: Fly 3 = Yank 0
        out.append((W + u, clip, fr, u == 1))
    T = W + F + 1
    out.append((T + 1, YANK, 1.0, False))
    hr = haul_retime(R)
    out.append((T + 2, HAUL, hr[0], False)) if hr[0] > 0 else out.append((T + 2, YANK, 2.0, False))
    for j, fr in enumerate(hr[1:], start=3):
        out.append((T + j, HAUL, fr, False))
    catch = T + R
    out[-1] = (catch, CATCH, 0.0, False)              # ловля: Haul 6 = Catch 0
    for j in range(1, HOLD + EXIT + 1):
        out.append((catch + j, CATCH, float(j), False))
    return out


LAYOUTS = {(W, F, R): layout(W, F, R) for W in (2, 3) for F in range(1, 11) for R in range(4, 9)}


def entry_frame(clip, lay):
    """С какого кадра раскладка входит в клип (Haul короткого возврата — с кадра 3)."""
    for _, c, f, _ in lay:
        if c == clip: return 0.0 if clip != HAUL else (f if f in (0.0, 3.0) else 0.0)
    return 0.0


def path_points(a, b, lay):
    """Кадры, которые вид проходит за тик от (клип, кадр) a к b: по прямой во времени, через целые кадры и стыки."""
    (ca, fa), (cb, fb) = a, b
    def seg(c, x0, x1):
        return [(c, x0)] + [(c, float(k)) for k in range(int(math.floor(x0)) + 1, int(math.ceil(x1 - 1e-9)))] + [(c, x1)]
    if ca == cb: return seg(ca, fa, fb)
    ia, ib = ORDER.index(ca), ORDER.index(cb)
    pts = seg(ca, fa, float(LAST[ca]))
    for c in ORDER[ia + 1:ib]:
        pts += seg(c, entry_frame(c, lay), float(LAST[c]))
    return pts + seg(cb, entry_frame(cb, lay) if not (cb == HAUL and fb < 3.0) else 0.0, fb)


def interp(snaps, x):
    i = int(math.floor(x + 1e-9)); t = x - i
    if t < 1e-6 or i >= len(snaps) - 1: return snaps[min(i, len(snaps) - 1)]
    a, b = snaps[i], snaps[i + 1]
    out = {}
    for n, (la, qa) in a.items():
        lb, qb = b[n]
        if qa.dot(qb) < 0: qb = -qb
        out[n] = (la.lerp(lb, t), qa.slerp(qb, t))
    return out


def deltas(sa, sb):
    d = {}
    for n, (la, qa) in sa.items():
        if not n.startswith("mixamorig:"): continue
        a = math.degrees(qa.rotation_difference(sb[n][1]).angle)
        d[n.replace("mixamorig:", "")] = min(a, 360 - a)
    return d


def tick_pairs():
    """Уникальные тики всех раскладок: (метка, от, до, удар, раскладка)."""
    seen, out = set(), []
    for key, lay in LAYOUTS.items():
        for (t0, c0, f0, _), (t1, c1, f1, s1) in zip(lay, lay[1:]):
            k = (c0, round(f0, 4), c1, round(f1, 4), s1, entry_frame(HAUL, lay))
            if k in seen: continue
            seen.add(k)
            out.append(("W%dF%dR%d t%d %s%.2f>%s%.2f" % (key[0], key[1], key[2], t1, c0[len(P):][:2], f0, c1[len(P):][:2], f1),
                        (c0, f0), (c1, f1), s1, lay))
    return out


def speed(snaps_by_clip):
    bad, worst, per = [], {}, []
    for lab, a, b, strike, lay in tick_pairs():
        pts = path_points(a, b, lay)
        ss = [interp(snaps_by_clip[c], x) for c, x in pts]
        d = {}
        for j in range(len(ss) - 1):
            for bone, ang in deltas(ss[j], ss[j + 1]).items():
                d[bone] = d.get(bone, 0.0) + ang
        top = max(d.items(), key=lambda kv: kv[1])
        per.append([lab, strike, top[0], round(top[1], 1)])
        for bone, ang in d.items():
            lim = LIM_STRIKE if (strike and bone in ARM_R) else LIM
            k = "arm" if bone in ARM_R else "body"
            if ang > worst.get(k, (0,))[0]: worst[k] = (round(ang, 1), lab, bone, strike)
            if ang > lim + 1e-6: bad.append((lab, bone, round(ang, 1), lim))
    return bad, worst, per


class Mesh:
    def __init__(self, rig, body):
        self.rig, self.body = rig, body
        self.feet = {s: [i for i, p in enumerate(body.vpart) if p == s + "Foot"] for s in SIDES}

    def at(self, snap):
        self.rig.restore(snap)
        return self.body.verts()

    def feet_low(self, vs):
        return {s: min(vs[i].z for i in self.feet[s]) for s in SIDES}

    def contacts(self, vs):
        hits = []
        for a, b in PAIRS:
            tri, deep, worst, _ = self.body.contact(list(a), b, vs)
            if tri or deep: hits.append(("+".join(a), b, tri, deep, round(worst, 4)))
        return hits


def pose_metrics(rig):
    v = rig.P("Neck") - rig.P("Hips")
    lean = math.degrees(math.atan2(-v.y, v.z))
    pyaw, cyaw = rig.pelvis_yaw(), rig.chest_yaw()
    hm = rig.head_m()
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    return dict(lean=round(lean, 1), twist=round((cyaw - pyaw + 180) % 360 - 180, 1), pyaw=round(pyaw, 1), cyaw=round(cyaw, 1),
                relP=hm["relP"], relY=hm["relY"], faceYaw=hm["faceYaw"], fore=round(rig.forearm_twist("Right"), 1),
                foreL=round(rig.forearm_twist("Left"), 1),
                hip_ratio=round((rig.P("Hips") - rig.rest[M("Hips")].translation).length / L, 3),
                reachR=round((rig.P("RightHand") - rig.P("RightArm")).length / sum(rig.ARM["Right"][x] for x in ("L1", "L2")), 2),
                reachL=round((rig.P("LeftHand") - rig.P("LeftArm")).length / sum(rig.ARM["Left"][x] for x in ("L1", "L2")), 2),
                handR=[round(c, 3) for c in rig.P("RightHand")], handL=[round(c, 3) for c in rig.P("LeftHand")])


def subframe_feet(mesh, snaps, clip, step=0.25):
    """Между целыми кадрами (как вид на 60+ к/с): стопы не под землёй, точки на земле не едут.
    Пока корень стоит (всё, кроме полёта Pull 0 < x < 12): шаг между соседними сэмплами и дрейф за непрерывное касание."""
    n = len(snaps) - 1
    xs = [round(k * step, 4) for k in range(int(n / step) + 1)]
    low, slide, drift = (9.0, None), (0.0, None), (0.0, None)
    prev, first = None, {}
    for x in xs:
        vs = mesh.at(interp(snaps, x))
        fl = mesh.feet_low(vs)
        for sd in SIDES:
            if fl[sd] < low[0]: low = (round(fl[sd], 4), "%s x%.2f" % (sd, x))
        stand = True
        g = {(sd, i): (vs[i].x, vs[i].y) for sd in SIDES for i in mesh.feet[sd] if vs[i].z < 0.012} if stand else {}
        if prev is not None:
            for k, p in g.items():
                q = prev.get(k)
                if q is not None:
                    dd = math.hypot(p[0] - q[0], p[1] - q[1])
                    if dd > slide[0]: slide = (round(dd, 4), "%s x%.2f" % (k[0], x))
        nf = {}
        for k, p in g.items():
            f0 = first.get(k) if (prev is not None and k in prev) else None
            if f0 is None: f0 = p
            nf[k] = f0
            dd = math.hypot(p[0] - f0[0], p[1] - f0[1])
            if dd > drift[0]: drift = (round(dd, 4), "%s ..x%.2f" % (k[0], x))
        first, prev = nf, g
    return dict(low=low, slide=slide, drift=drift)




def lean_rule(clip, f):
    """(мин, макс) наклона корпуса (+ вперёд): замах назад ≤ 8°, бросок вперёд ≤ 35°, натяг и тяга назад 12–25°."""
    if clip == THROW: return (-8.0, 35.0) if f == 1 else (0.0, 35.0)
    if clip == FLY: return (0.0, 35.0)
    if clip == YANK: return (-25.0, 35.0) if f == 0 else (-25.0, -12.0)
    if clip == HAUL: return (-25.0, -12.0)
    return (-25.0, 35.0)


def check(rig, body, snaps_by_clip, quick=False):
    mesh = Mesh(rig, body)
    rig.restore(snaps_by_clip["_stance"])
    base_relP = rig.head_m()["relP"]; base_relY = rig.head_m()["relY"]
    vs0 = body.verts()
    base_contacts = {(a, b): (t, dd) for a, b, t, dd, w in mesh.contacts(vs0)}
    summary, viol = {}, []
    clips = {c: s for c, s in snaps_by_clip.items() if not c.startswith("_")}
    bad, worst, per = speed(clips) if all(c in clips for c in ORDER) else ([], {}, [])
    for b in bad: viol.append(("ALL", "скорость", b))
    for clip, snaps in clips.items():
        n = len(snaps)
        rows, prev_vs = [], None
        slide_max = (0.0, None)
        for f in range(n):
            vs = mesh.at(snaps[f])
            m = pose_metrics(rig)
            low = mesh.feet_low(vs)
            m["feet"] = {s: round(low[s], 4) for s in SIDES}
            if not quick:
                c = [h for h in mesh.contacts(vs)
                     if h[2] > base_contacts.get((h[0], h[1]), (0, 0))[0] or h[3] > base_contacts.get((h[0], h[1]), (0, 0))[1]]
                m["contacts"] = c
                for h in c: viol.append((clip, "пересечение f%d" % f, h))
            if min(low.values()) < -0.004: viol.append((clip, "под землёй f%d" % f, m["feet"]))
            lo, hi = lean_rule(clip, f)
            if not (lo - 0.05 <= m["lean"] <= hi + 0.05): viol.append((clip, "наклон f%d" % f, (m["lean"], lo, hi)))
            if abs(m["twist"]) > 45: viol.append((clip, "скрутка f%d" % f, m["twist"]))
            if max(abs(m["fore"]), abs(m["foreL"])) > 70: viol.append((clip, "крутка предплечья f%d" % f, (m["fore"], m["foreL"])))
            if m["relP"] > base_relP + 0.5: viol.append((clip, "голова вверх f%d" % f, m["relP"]))
            if abs(m["relY"] - base_relY) > 30.5: viol.append((clip, "голова вбок f%d" % f, m["relY"]))
            if m["hip_ratio"] > 0.5: viol.append((clip, "таз f%d" % f, m["hip_ratio"]))
            if prev_vs is not None:
                for s in SIDES:
                    ids = [i for i in mesh.feet[s] if prev_vs[i].z < 0.012 and vs[i].z < 0.012]
                    if len(ids) >= 8:
                        dd = sorted(((vs[i].x - prev_vs[i].x) ** 2 + (vs[i].y - prev_vs[i].y) ** 2) ** .5 for i in ids)
                        med = dd[len(dd) // 2]
                        if med > slide_max[0]: slide_max = (med, "%s f%d→%d" % (s, f - 1, f))
                        if med > 0.01: viol.append((clip, "скольжение f%d→%d" % (f - 1, f), (s, round(med, 3), len(ids))))
            prev_vs = vs
            rows.append(m)
        sub = subframe_feet(mesh, snaps, clip)
        if sub["low"][0] < -0.003: viol.append((clip, "под землёй между кадрами", sub["low"]))
        if sub["slide"][0] > 0.004: viol.append((clip, "скольжение между кадрами", sub["slide"]))
        if sub["drift"][0] > 0.006: viol.append((clip, "дрейф опоры", sub["drift"]))
        summary[clip] = dict(slide=(round(slide_max[0], 4), slide_max[1]), rows=rows, sub=sub)
    summary["_speed"] = dict(worst=worst, n_bad=len(bad), n_ticks=len(per))
    return summary, viol, per


def print_summary(summary, viol):
    sp = summary.get("_speed", {})
    print("CHK speed bad=%s ticks=%s worst body %s arm %s" % (sp.get("n_bad"), sp.get("n_ticks"), sp.get("worst", {}).get("body"),
                                                               sp.get("worst", {}).get("arm")))
    for c, s in summary.items():
        if c.startswith("_"): continue
        rows = s["rows"]
        print("CHK %-6s slide %s | lean %.1f..%.1f tw %.0f fore %.0f..%.0f hip %.3f" % (
            c[len(P):], s["slide"], min(r["lean"] for r in rows), max(r["lean"] for r in rows), max(abs(r["twist"]) for r in rows),
            min(r["fore"] for r in rows), max(r["fore"] for r in rows), max(r["hip_ratio"] for r in rows)))
        for i, r in enumerate(rows):
            print("   f%02d lean %5.1f tw %5.1f pY %6.1f cY %6.1f relP %5.1f relY %6.1f face %6.1f fore %6.1f/%6.1f reach R %.2f L %.2f hip %.3f feet L %.3f R %.3f hR %s hL %s %s" % (
                i, r["lean"], r["twist"], r["pyaw"], r["cyaw"], r["relP"], r["relY"], r["faceYaw"], r["fore"], r["foreL"], r["reachR"], r["reachL"],
                r["hip_ratio"], r["feet"]["Left"], r["feet"]["Right"], r["handR"], r["handL"], r.get("contacts") or ""))
    for c, s in summary.items():
        if not c.startswith("_"): print("SUB %-6s %s" % (c[len(P):], s.get("sub")))
    print("VIOLATIONS", len(viol))
    for v in viol[:400]: print("  V", v[0].replace(P, ""), v[1], v[2])
