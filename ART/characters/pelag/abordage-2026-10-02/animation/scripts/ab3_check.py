"""Абордаж v3: пределы новых клипов теми же замерами, что ab_check.py (путь кости за тик во всех раскладках,
стопы по сетке, опора не едет, пересечения, наклон, скрутка, предплечье, голова за грудью, таз).

Раскладки (кадр клипа на тик):
  Throw     замах W = 3 (цель за спиной — 4) тиков: кадр 3k/W; полёт якоря A = 1…6: кадр 3 + 6j/A; натяг — кадр 9.
            Удар правой руки (≤ 70°): тик выпуска (W−1 → W) и первый тик полёта; остальное ≤ 35°.
  PullShort P = 4…5: P5 — 0,1,2,3,4,5; P4 — 0,2,3,4,5 (нырок всегда на тике; взвод — P−2, кулак пошёл — P−1,
            контакт — P). P ≤ 3 — принятый Pull (0,1,11,12 / 0,11,12): нырок за тик не помещается. Удар правой — два
            последних тика (как Pull).
  Uppercut, Slam — 1:1; удар правой — тик 0→1 (в контакт), проводка и удержание — обычные тики.
"""
import math
import ab_check
from ab_check import Mesh, pose_metrics, deltas, interp, ARM_R, LIM, LIM_STRIKE, AIR
from ab_rig import SIDES

P = "Pelag_AN_Abordage2_"
THROW, PULL, PUNCH, RECOVER = P + "Throw", P + "Pull", P + "Punch", P + "Recover"
SHORT, UPPER, SLAM = P + "PullShort", P + "Uppercut", P + "Slam"
REL3, BITE3 = 3, 9


def throw3_retime(W, A):
    return [3.0 * k / W for k in range(W + 1)] + [REL3 + (BITE3 - REL3) * j / A for j in range(1, A + 1)]


SHORT_RT = {5: [0.0, 1.0, 2.0, 3.0, 4.0, 5.0], 4: [0.0, 2.0, 3.0, 4.0, 5.0]}
RT_THROW3 = {(W, A): throw3_retime(W, A) for W in (3, 4) for A in range(1, 7)}


def ticks3(clip, n):
    out = []
    if clip == THROW:
        for (W, A), fr in RT_THROW3.items():
            for k in range(1, len(fr)):
                out.append(("W%dA%d t%d" % (W, A, k), fr[k - 1], fr[k], k in (W, W + 1)))
    elif clip == SHORT:
        for P_, fr in SHORT_RT.items():
            for k in range(1, len(fr)):
                out.append(("S%d t%d" % (P_, k), fr[k - 1], fr[k], k >= P_ - 1))
    elif clip in (UPPER, SLAM):
        strike = (0,)
        for f in range(n - 1):
            out.append(("%d→%d" % (f, f + 1), f, f + 1, f in strike))
    else:
        return ab_check.ticks(clip, n)
    return out


def standing3(clip, x):
    """Корень стоит (опорная стопа не должна ехать): Throw весь, PullShort кадр 0, Uppercut/Slam с контакта (1)."""
    if clip == THROW: return True
    if clip == SHORT: return x <= 1e-6
    if clip in (UPPER, SLAM): return x >= 1 - 1e-6
    if clip == PULL: return not (0 < x < 12)
    return True


def speed3(clip, snaps):
    bad, worst, seen = [], {}, set()
    for lab, a, b, strike in ticks3(clip, len(snaps)):
        key = (round(a, 4), round(b, 4), strike)
        if key in seen: continue
        seen.add(key)
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


def subframe3(mesh, snaps, clip, step=0.25):
    n = len(snaps) - 1
    xs = [round(k * step, 4) for k in range(int(n / step) + 1)]
    low, slide, drift = (9.0, None), (0.0, None), (0.0, None)
    prev, first = None, {}
    for x in xs:
        vs = mesh.at(interp(snaps, x))
        fl = mesh.feet_low(vs)
        for sd in SIDES:
            if fl[sd] < low[0]: low = (round(fl[sd], 4), "%s x%.2f" % (sd, x))
        g = {(sd, i): (vs[i].x, vs[i].y) for sd in SIDES for i in mesh.feet[sd] if vs[i].z < 0.012} if standing3(clip, x) else {}
        if prev is not None:
            for k, p in g.items():
                q = prev.get(k)
                if q is not None:
                    dd = math.hypot(p[0] - q[0], p[1] - q[1])
                    if dd > slide[0]: slide = (round(dd, 4), "%s x%.2f" % (k[0], x))
        nf = {}
        for k, p in g.items():
            f0 = first.get(k) if (prev is not None and k in prev) else None
            nf[k] = p if f0 is None else f0
            dd = math.hypot(p[0] - nf[k][0], p[1] - nf[k][1])
            if dd > drift[0]: drift = (round(dd, 4), "%s ..x%.2f" % (k[0], x))
        first, prev = nf, g
    return dict(low=low, slide=slide, drift=drift)


def check3(rig, body, snaps_by_clip, quick=False):
    mesh = Mesh(rig, body)
    rig.restore(snaps_by_clip["_stance"])
    base_relP = rig.head_m()["relP"]; base_relY = rig.head_m()["relY"]
    base_contacts = {(a, b): (t, dd) for a, b, t, dd, w in mesh.contacts(body.verts())}
    summary, viol = {}, []
    for clip, snaps in snaps_by_clip.items():
        if clip.startswith("_"): continue
        n = len(snaps)
        bad, worst = speed3(clip, snaps)
        for b in bad: viol.append((clip, "скорость", b))
        rows, prev_vs, slide_max = [], None, (0.0, None)
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
            air = (clip == PULL and 1 <= f <= 11) or (clip == SHORT and 1 <= f <= 4) or (clip in (UPPER, SLAM) and f == 0)
            if air:
                for s in SIDES:
                    if low[s] < AIR: viol.append((clip, "стопа в полёте f%d" % f, (s, round(low[s], 4))))
            if clip == SHORT and 1 <= f <= 3 and not (25 <= m["lean"] <= 60): viol.append((clip, "наклон нырка f%d" % f, m["lean"]))
            back_ok = -8 if (clip == THROW and f in (1, 2)) else 0
            if m["lean"] < back_ok: viol.append((clip, "наклон назад f%d" % f, m["lean"]))
            if clip in (UPPER, SLAM) and f == 1 and m["lean"] < 10: viol.append((clip, "наклон на ударе f1", m["lean"]))
            if abs(m["twist"]) > 45: viol.append((clip, "скрутка f%d" % f, m["twist"]))
            if max(abs(m["fore"]), abs(m["foreL"])) > 70: viol.append((clip, "крутка предплечья f%d" % f, (m["fore"], m["foreL"])))
            if m["relP"] > base_relP + 0.5: viol.append((clip, "голова вверх f%d" % f, m["relP"]))
            if abs(m["relY"] - base_relY) > 30.5: viol.append((clip, "голова вбок f%d" % f, m["relY"]))
            if m["hip_ratio"] > 0.5: viol.append((clip, "таз f%d" % f, m["hip_ratio"]))
            if prev_vs is not None and standing3(clip, f) and standing3(clip, f - 1):
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
        if clip == SHORT:
            for x in [k * 0.25 for k in range(4, 17)]:      # кадры 1…4 и между ними (вид на 60+ к/с)
                lo = min(mesh.feet_low(mesh.at(interp(snaps, x))).values())
                frac_low = lo if frac_low is None else min(frac_low, lo)
                if lo < AIR: viol.append((clip, "стопа в полёте x%.2f" % x, round(lo, 4)))
        sub = subframe3(mesh, snaps, clip)
        if sub["low"][0] < -0.003: viol.append((clip, "под землёй между кадрами", sub["low"]))
        if sub["slide"][0] > 0.004: viol.append((clip, "скольжение между кадрами", sub["slide"]))
        if sub["drift"][0] > 0.006: viol.append((clip, "дрейф опоры", sub["drift"]))
        summary[clip] = dict(worst=worst, n_bad=len(bad), slide=(round(slide_max[0], 4), slide_max[1]), frac_low=frac_low,
                             rows=rows, sub=sub)
    return summary, viol
