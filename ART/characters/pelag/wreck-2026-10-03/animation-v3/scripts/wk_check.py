"""Крушение v2: пределы по снимкам позы при сборке (те же замеры, что ab_check.py Абордажа / sq_check.py Шквала):
  * поворот кости за тик: тело ≤ 35°, руки (плечо, предплечье, кисть, пальцы обеих) в тики удара ≤ 70°
    (Swing1 0→10 — снятие, мах и проводка до позы 3 (= Swing2 0→2), Swing2 0→7, Slam 0→10 — дуга, удар и тик после, Stow 0→5 — мах на спину;
    формы: *_Braced как база, Slam_Drag 0→10, ChargeRelease 0→5; Charge — цикл, везде ≤ 35°), вне удара ≤ 35°;
  * стопы по сетке: под землю не уходят (−4 мм), опорная стопа не скользит (между кадрами и через ¼ кадра);
  * пересечения: кисти/предплечья обеих рук с обоими бёдрами, голенями, торсом, головой; ноги между собой;
  * наклон назад — никогда; скрутка груди к тазу ≤ 45°; крутка предплечья ≤ 70°; лицо = стойка ± 5° (голова за грудью);
    смещение таза к привязке ≤ 0,5 длины корпуса (перенос в Unity); кисти на рукояти 0,10–0,25 м.
"""
import math
from wk_rig import M, SIDES

LIM, LIM_STRIKE = 35.0, 70.0
P = "Pelag_AN_Wreck2_"
STRIKE = {P + "Swing1": (0, 10), P + "Swing2": (0, 7), P + "Slam": (0, 10), P + "Stow": (0, 5),
          P + "Swing1_Braced": (0, 10), P + "Swing2_Braced": (0, 7), P + "Slam_Drag": (0, 10), P + "ChargeRelease": (0, 5)}
PAIRS = [(("RightHand",), "LeftThigh"), (("RightForeArm",), "LeftThigh"), (("LeftHand",), "RightThigh"), (("LeftForeArm",), "RightThigh"),
         (("RightHand", "RightForeArm"), "RightThigh"), (("LeftHand", "LeftForeArm"), "LeftThigh"),
         (("RightHand", "RightForeArm"), "LeftShin"), (("RightHand", "RightForeArm"), "RightShin"),
         (("LeftHand", "LeftForeArm"), "LeftShin"), (("LeftHand", "LeftForeArm"), "RightShin"),
         (("LeftShin",), "RightShin"), (("LeftFoot",), "RightShin"), (("RightFoot",), "LeftShin"), (("LeftFoot",), "RightFoot"),
         (("RightHand", "RightForeArm"), "torso"), (("LeftHand", "LeftForeArm"), "torso"), (("RightHand", "RightForeArm"), "head"),
         (("LeftHand", "LeftForeArm"), "head")]


def is_arm(b):
    return any(b.startswith(s + x) for s in SIDES for x in ("Arm", "ForeArm", "Hand"))


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
        if not n.startswith("mixamorig:") or n.endswith("4") or n.endswith("_End"): continue
        a = math.degrees(qa.rotation_difference(sb[n][1]).angle)
        d[n.replace("mixamorig:", "")] = min(a, 360 - a)
    return d


def speed(clip, snaps):
    bad, worst = [], {}
    st = STRIKE.get(clip)
    for f in range(len(snaps) - 1):
        strike = st is not None and st[0] <= f < st[1]
        for bone, ang in deltas(snaps[f], snaps[f + 1]).items():
            arm = is_arm(bone)
            lim = LIM_STRIKE if (strike and arm) else LIM
            k = "arm" if arm else "body"
            if ang > worst.get(k, (0,))[0]: worst[k] = (round(ang, 1), "%d→%d" % (f, f + 1), bone, strike)
            if ang > lim + 1e-6: bad.append(("%d→%d" % (f, f + 1), bone, round(ang, 1), lim))
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


def planted(vs_a, vs_b, ids):
    """Медиана сдвига вершин стопы, стоящих на земле в обоих снимках (м), и их число."""
    on = [i for i in ids if vs_a[i].z < 0.012 and vs_b[i].z < 0.012]
    if len(on) < 8: return None, len(on)
    dd = sorted(((vs_b[i].x - vs_a[i].x) ** 2 + (vs_b[i].y - vs_a[i].y) ** 2) ** .5 for i in on)
    return dd[len(dd) // 2], len(on)


def check(rig, body, snaps_by_clip, rows_by_clip, quick=False):
    mesh = Mesh(rig, body)
    rig.restore(snaps_by_clip["_stance"])
    base_relP = rig.head_m()["relP"]
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    base_contacts = {(a, b): (t, dd) for a, b, t, dd, w in mesh.contacts(body.verts())}
    summary, viol = {}, []
    for clip, snaps in snaps_by_clip.items():
        if clip.startswith("_"): continue
        rows = rows_by_clip[clip]
        bad, worst = speed(clip, snaps)
        for b in bad: viol.append((clip, "скорость", b))
        prev_vs, slide = None, (0.0, None)
        mx = dict(lean=(99, -99), twist=0.0, fore=0.0, relP=0.0, hip=0.0, low=9.0, gap=(9.0, 0.0))
        cont = []
        for f, snap in enumerate(snaps):
            vs = mesh.at(snap)
            r = rows[f]
            low = mesh.feet_low(vs)
            hip = (rig.P("Hips") - rig.rest[M("Hips")].translation).length / L
            relP = rig.head_m()["relP"] - base_relP
            fore = max(abs(v) for v in r["forearm_twist"].values())
            mx["lean"] = (min(mx["lean"][0], r["lean"]), max(mx["lean"][1], r["lean"]))
            mx["twist"] = max(mx["twist"], abs(r["twist"])); mx["fore"] = max(mx["fore"], fore)
            mx["relP"] = max(mx["relP"], abs(relP)); mx["hip"] = max(mx["hip"], hip); mx["low"] = min(mx["low"], min(low.values()))
            r["hip_ratio"] = round(hip, 3); r["relP_over_stance"] = round(relP, 2); r["feet_low"] = {s: round(low[s], 4) for s in SIDES}
            if r["lean"] < 0: viol.append((clip, "наклон назад f%d" % f, r["lean"]))
            if abs(r["twist"]) > 45: viol.append((clip, "скрутка f%d" % f, r["twist"]))
            if fore > 70: viol.append((clip, "крутка предплечья f%d" % f, r["forearm_twist"]))
            if abs(relP) > 5: viol.append((clip, "голова не за грудью f%d" % f, round(relP, 1)))
            if hip > 0.5: viol.append((clip, "таз f%d" % f, round(hip, 3)))
            if min(low.values()) < -0.004: viol.append((clip, "под землёй f%d" % f, r["feet_low"]))
            if r.get("on_grip"):
                mx["gap"] = (min(mx["gap"][0], r["hand_gap"]), max(mx["gap"][1], r["hand_gap"]))
                if not 0.10 <= r["hand_gap"] <= 0.25: viol.append((clip, "кисти на рукояти f%d" % f, r["hand_gap"]))
            if not quick:
                c = [h for h in mesh.contacts(vs)
                     if h[2] > base_contacts.get((h[0], h[1]), (0, 0))[0] or h[3] > base_contacts.get((h[0], h[1]), (0, 0))[1]]
                r["contacts"] = c
                for h in c: viol.append((clip, "пересечение f%d" % f, h)); cont.append((f, h))
            if prev_vs is not None:
                for s in SIDES:
                    med, n = planted(prev_vs, vs, mesh.feet[s])
                    if med is not None:
                        if med > slide[0]: slide = (round(med, 4), "%s f%d→%d" % (s, f - 1, f))
                        if med > 0.01: viol.append((clip, "скольжение f%d→%d" % (f - 1, f), (s, round(med, 3), n)))
            prev_vs = vs
        sub = subframe(mesh, snaps)
        if sub["low"][0] < -0.004: viol.append((clip, "под землёй между кадрами", sub["low"]))
        if sub["slide"][0] > 0.006: viol.append((clip, "скольжение между кадрами", sub["slide"]))
        summary[clip] = dict(worst=worst, n_bad=len(bad), slide=slide, sub=sub, lean=mx["lean"], twist=round(mx["twist"], 1),
                             fore=round(mx["fore"], 1), relP=round(mx["relP"], 2), hip=round(mx["hip"], 3),
                             low=round(mx["low"], 4), gap=mx["gap"], contacts=len(cont))
    return summary, viol


def subframe(mesh, snaps, step=0.25):
    """Через ¼ кадра (вид на 60+ к/с): стопы не под землёй, точки на земле не едут."""
    n = len(snaps) - 1
    low, slide = (9.0, None), (0.0, None)
    prev = None
    for k in range(int(n / step) + 1):
        x = round(k * step, 4)
        vs = mesh.at(interp(snaps, x))
        fl = mesh.feet_low(vs)
        for s in SIDES:
            if fl[s] < low[0]: low = (round(fl[s], 4), "%s x%.2f" % (s, x))
        if prev is not None:
            for s in SIDES:
                med, _ = planted(prev, vs, mesh.feet[s])
                if med is not None and med > slide[0]: slide = (round(med, 4), "%s x%.2f" % (s, x))
        prev = vs
    return dict(low=low, slide=slide)


def print_summary(summary, viol):
    for c, s in summary.items():
        print("CHK %-7s bad=%-3d body %s arm %s | lean %s tw %.0f fore %.0f relP %.1f hip %.3f low %.4f gap %s slide %s sub %s cont %d" % (
            c[len(P):], s["n_bad"], s["worst"].get("body"), s["worst"].get("arm"), s["lean"], s["twist"], s["fore"], s["relP"],
            s["hip"], s["low"], s["gap"], s["slide"], s["sub"], s["contacts"]))
    print("VIOLATIONS", len(viol))
    for v in viol[:300]: print("  V", v[0][len(P):], v[1], v[2])
