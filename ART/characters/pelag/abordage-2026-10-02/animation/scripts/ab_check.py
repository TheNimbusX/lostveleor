"""Абордаж v2: проверка пределов по снимкам позы при сборке (те же замеры, что sq_check.py Шквала):

  * скорость костей за тик во ВСЕХ раскладках: полёт якоря A = 1–6 (Throw), тяга P = 2–12 (Pull), остальные клипы 1:1;
    предел 35°, правая рука (RightArm/ForeArm/Hand) в тики удара — 70° (бросок: тики 0→1, 1→2 и первый тик полёта
    якоря; кулак: два последних тика тяги);
  * стопы по СЕТКЕ: в полёте тяги (кадры 1–11 и дробные кадры раскладок) обе ≥ 3 % роста; под землю не уходят нигде;
  * опорная стопа, пока корень стоит (Throw, Pull 0, Punch, Recover): точки касания земли не едут;
  * пересечения: кисть/предплечье с бёдрами и голенями обеих сторон, бёдра, голени, стопы, торс, голова;
  * наклон (назад — никогда; в полёте 25–40°; на ударе ≥ 10°), скрутка ≤ 45°, крутка предплечья ≤ 70°, голова за грудью.
"""
import math, bpy
from mathutils import Vector
from ab_rig import M, SIDES, yaw_of

HEIGHT = 1.80
AIR = 0.03 * HEIGHT
LIM, LIM_STRIKE = 35.0, 70.0
ARM_R = ("RightArm", "RightForeArm", "RightHand")
P = "Pelag_AN_Abordage2_"
THROW, PULL, PUNCH, RECOVER = P + "Throw", P + "Pull", P + "Punch", P + "Recover"
THROW_REL, THROW_END = 2, 8          # выпуск и натяг (кадры Throw)
PULL_END, PULL_FLIGHT = 12, (1, 11)  # контакт и кадры полёта Pull
PAIRS = [(("RightHand",), "LeftThigh"), (("RightForeArm",), "LeftThigh"), (("LeftHand",), "RightThigh"), (("LeftForeArm",), "RightThigh"),
         (("RightHand", "RightForeArm"), "RightThigh"), (("LeftHand", "LeftForeArm"), "LeftThigh"),
         (("RightHand", "RightForeArm"), "LeftShin"), (("RightHand", "RightForeArm"), "RightShin"),
         (("LeftHand", "LeftForeArm"), "LeftShin"), (("LeftHand", "LeftForeArm"), "RightShin"),
         (("LeftShin",), "RightShin"), (("LeftFoot",), "RightShin"), (("RightFoot",), "LeftShin"), (("LeftFoot",), "RightFoot"),
         (("RightHand", "RightForeArm"), "LeftForeArm"), (("RightHand", "RightForeArm"), "LeftHand"),
         (("RightHand", "RightForeArm"), "torso"), (("LeftHand", "LeftForeArm"), "torso"), (("RightHand", "RightForeArm"), "head"),
         (("LeftHand", "LeftForeArm"), "head")]


def throw_retime(A):
    """Тики броска 0..2+A → кадры Throw: замах 0, 1, выпуск 2; полёт якоря A тиков (1–6) равномерно до натяга 8."""
    return [0.0, 1.0, 2.0] + [THROW_REL + (THROW_END - THROW_REL) * k / A for k in range(1, A + 1)]


def pull_retime(P_):
    """Тики тяги 0..P → кадры Pull (P = 12 — 1:1). Участки: срыв [0→1] — 1 тик; полёт [1→9] — P − 4 тиков;
    к земле и кулак [9→10→11→12] — последние 3 тика. Коротким тягам полёт не нужен: P = 4 — 0, 1, 10, 11, 12;
    P = 3 — 0, 1, 11, 12; P = 2 — 0, 11, 12 (удар — последние два тика всегда)."""
    if P_ == 2: return [0.0, 11.0, 12.0]
    if P_ == 3: return [0.0, 1.0, 11.0, 12.0]
    if P_ == 4: return [0.0, 1.0, 10.0, 11.0, 12.0]
    return [0.0, 1.0] + [1.0 + 8.0 * j / (P_ - 4) for j in range(1, P_ - 3)] + [10.0, 11.0, 12.0]


RT_THROW = {A: throw_retime(A) for A in range(1, 7)}
RT_PULL = {p: pull_retime(p) for p in range(2, 13)}


def standing(clip, n):
    if clip == PULL: return [0]
    return list(range(n))


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


def ticks(clip, n):
    """(метка, кадр_от, кадр_до, тик_удара) по всем раскладкам."""
    out = []
    if clip == THROW:
        for A, fr in RT_THROW.items():
            for k in range(1, len(fr)):
                # удар руки: выпуск 1→2 и первый тик полёта якоря; замах 0→1 — обычный тик (≤ 35°, как Load Шквала)
                out.append(("A%d t%d" % (A, k), fr[k - 1], fr[k], k in (2, 3)))
    elif clip == PULL:
        for P_, fr in RT_PULL.items():
            for k in range(1, len(fr)):
                out.append(("P%d t%d" % (P_, k), fr[k - 1], fr[k], k >= P_ - 1))
    else:
        for f in range(n - 1):
            out.append(("%d→%d" % (f, f + 1), f, f + 1, False))
    return out


def speed(clip, snaps):
    bad, worst = [], {}
    seen = set()
    for lab, a, b, strike in ticks(clip, len(snaps)):
        key = (round(a, 4), round(b, 4), strike)
        if key in seen: continue
        seen.add(key)
        # путь кости за тик: вид идёт по клипу по прямой во времени, проходя все целые кадры между a и b
        pts = [a] + [float(k) for k in range(int(math.floor(a)) + 1, int(math.ceil(b - 1e-9)))] + [b]
        ss = [interp(snaps, x) for x in pts]
        d = {}
        for j in range(len(ss) - 1):
            for bone, ang in deltas(ss[j], ss[j + 1]).items():
                d[bone] = d.get(bone, 0.0) + ang
        for bone, ang in d.items():
            lim = LIM_STRIKE if (strike and bone in ARM_R) else LIM
            k = "arm" if bone in ARM_R else "body"
            if ang > worst.get(k, (0,))[0]: worst[k] = (round(ang, 1), lab, bone, strike)
            if ang > lim + 1e-6: bad.append((lab, bone, round(ang, 1), lim))
    return bad, worst


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
        stand = not (clip == PULL and 0 < x < PULL_END)
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


def check(rig, body, snaps_by_clip, quick=False):
    mesh = Mesh(rig, body)
    rig.restore(snaps_by_clip["_stance"])
    base_relP = rig.head_m()["relP"]; base_relY = rig.head_m()["relY"]
    vs0 = body.verts()
    base_contacts = {(a, b): (t, dd) for a, b, t, dd, w in mesh.contacts(vs0)}
    summary, viol = {}, []
    for clip, snaps in snaps_by_clip.items():
        if clip.startswith("_"): continue
        n = len(snaps)
        bad, worst = speed(clip, snaps)
        for b in bad: viol.append((clip, "скорость", b))
        rows, prev_vs = [], None
        stand = set(standing(clip, n))
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
            if clip == PULL and PULL_FLIGHT[0] <= f <= PULL_FLIGHT[1]:
                for s in SIDES:
                    if low[s] < AIR: viol.append((clip, "стопа в полёте f%d" % f, (s, round(low[s], 4))))
                if f <= 9 and not (25 <= m["lean"] <= 40): viol.append((clip, "наклон полёта f%d" % f, m["lean"]))
            # назад — только в замахе (Throw 1, отклон от цели, как лист 1, не глубже 8°); на ударе и в тяге — никогда
            if m["lean"] < (-8 if (clip == THROW and f == 1) else 0): viol.append((clip, "наклон назад f%d" % f, m["lean"]))
            if clip == PUNCH and f == 0 and m["lean"] < 10: viol.append((clip, "наклон на ударе f0", m["lean"]))
            if abs(m["twist"]) > 45: viol.append((clip, "скрутка f%d" % f, m["twist"]))
            if max(abs(m["fore"]), abs(m["foreL"])) > 70: viol.append((clip, "крутка предплечья f%d" % f, (m["fore"], m["foreL"])))
            if m["relP"] > base_relP + 0.5: viol.append((clip, "голова вверх f%d" % f, m["relP"]))
            if abs(m["relY"] - base_relY) > 30.5: viol.append((clip, "голова вбок f%d" % f, m["relY"]))
            if m["hip_ratio"] > 0.5: viol.append((clip, "таз f%d" % f, m["hip_ratio"]))
            if prev_vs is not None and f in stand and (f - 1) in stand:
                for s in SIDES:
                    ids = [i for i in mesh.feet[s] if prev_vs[i].z < 0.012 and vs[i].z < 0.012]
                    if len(ids) >= 8:
                        dd = sorted(((vs[i].x - prev_vs[i].x) ** 2 + (vs[i].y - prev_vs[i].y) ** 2) ** .5 for i in ids)
                        med = dd[len(dd) // 2]
                        if med > slide_max[0]: slide_max = (med, "%s f%d→%d" % (s, f - 1, f))
                        if med > 0.01: viol.append((clip, "скольжение f%d→%d" % (f - 1, f), (s, round(med, 3), len(ids))))
            prev_vs = vs
            rows.append(m)
        frac_low = None
        if clip == PULL:
            xs = sorted({x for fr in RT_PULL.values() for x in fr if PULL_FLIGHT[0] <= x <= PULL_FLIGHT[1] + 0.999 and abs(x - round(x)) > 1e-6})
            for x in xs:
                if x > PULL_FLIGHT[1]: continue
                vs = mesh.at(interp(snaps, x))
                lo = min(mesh.feet_low(vs).values())
                frac_low = lo if frac_low is None else min(frac_low, lo)
                if lo < AIR: viol.append((clip, "стопа в полёте x%.2f" % x, round(lo, 4)))
        sub = subframe_feet(mesh, snaps, clip)
        if sub["low"][0] < -0.003: viol.append((clip, "под землёй между кадрами", sub["low"]))
        if sub["slide"][0] > 0.004: viol.append((clip, "скольжение между кадрами", sub["slide"]))
        if sub["drift"][0] > 0.006: viol.append((clip, "дрейф опоры", sub["drift"]))
        summary[clip] = dict(worst=worst, n_bad=len(bad), slide=(round(slide_max[0], 4), slide_max[1]), frac_low=frac_low, rows=rows,
                             sub=sub)
    return summary, viol


def print_summary(summary, viol):
    for c, s in summary.items():
        rows = s["rows"]
        fl = [min(r["feet"].values()) for i, r in enumerate(rows) if c == PULL and PULL_FLIGHT[0] <= i <= PULL_FLIGHT[1]]
        print("CHK %-8s bad=%-3d worst body %s arm %s | air %s frac %s | slide %s | lean %.1f..%.1f tw %.0f fore %.0f..%.0f hip %.3f" % (
            c[len(P):], s["n_bad"], s["worst"].get("body"), s["worst"].get("arm"),
            round(min(fl), 3) if fl else "-", round(s["frac_low"], 3) if s["frac_low"] is not None else "-", s["slide"],
            min(r["lean"] for r in rows), max(r["lean"] for r in rows), max(abs(r["twist"]) for r in rows),
            min(r["fore"] for r in rows), max(r["fore"] for r in rows), max(r["hip_ratio"] for r in rows)))
        for i, r in enumerate(rows):
            print("   f%02d lean %5.1f tw %5.1f pY %6.1f cY %6.1f relP %5.1f relY %6.1f face %6.1f fore %6.1f/%6.1f reach R %.2f L %.2f hip %.3f feet L %.3f R %.3f %s" % (
                i, r["lean"], r["twist"], r["pyaw"], r["cyaw"], r["relP"], r["relY"], r["faceYaw"], r["fore"], r["foreL"], r["reachR"], r["reachL"],
                r["hip_ratio"], r["feet"]["Left"], r["feet"]["Right"], r.get("contacts") or ""))
    for c, s in summary.items():
        print("SUB %-8s %s" % (c[len(P):], s.get("sub")))
    print("VIOLATIONS", len(viol))
    for v in viol[:400]: print("  V", v[0][len(P):], v[1], v[2])
