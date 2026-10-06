"""Шквал v2: проверка пределов по снимкам позы — те же замеры, что у независимой проверки
(artifacts/tools/squall-rework/clips-adv/measure.py + render.py), но прямо при сборке:

  * скорость костей за тик во всех раскладках полёта F = 2–6 (дробные кадры — slerp соседних кадров) и в опоре;
    предел 35°, рука с саблей (RightArm/ForeArm/Hand) в тики удара — 70°;
  * стопы по СЕТКЕ: в полёте (кадры 1–5 и дробные кадры раскладок) обе ≥ 3 % роста; под землю не уходят;
  * опорная стопа, пока корень стоит: точки касания земли не едут;
  * пересечения: любая кисть/предплечье с любым бедром и голенью (обе стороны), бёдра, голени, стопы;
  * наклон, скрутка, крутка предплечья, голова, клинок на контакте, смещение таза при переносе.
"""
import math, bpy
from mathutils import Vector
from b_common import blade
from s_lib import sagittal_lean
from sq_rig import M, SIDES, yaw_of

HEIGHT = 1.80                      # покой Pelag_v6 по сетке
AIR = 0.03 * HEIGHT                # 5,4 см
LIM, LIM_STRIKE = 35.0, 70.0
SABRE = ("RightArm", "RightForeArm", "RightHand")
RT = {2: [0, 3, 6], 3: [0, 1, 3.5, 6], 4: [0, 1, 2 + 2 / 3, 4 + 1 / 3, 6], 5: [0, 1, 2.25, 3.5, 4.75, 6], 6: [0, 1, 2, 3, 4, 5, 6]}
P = "Pelag_AN_Squall2_"
SLASH = (P + "Forehand", P + "Backhand")
FLIGHT = SLASH + (P + "ReturnFore", P + "ReturnBack")
STRIKE = {P + "FinishFore": {(0, 1), (1, 2)}, P + "FinishBack": {(0, 1), (1, 2)}}
PAIRS = [(("RightHand",), "LeftThigh"), (("RightForeArm",), "LeftThigh"), (("LeftHand",), "RightThigh"), (("LeftForeArm",), "RightThigh"),
         (("RightHand", "RightForeArm"), "RightThigh"), (("LeftHand", "LeftForeArm"), "LeftThigh"),
         (("RightHand", "RightForeArm"), "LeftShin"), (("RightHand", "RightForeArm"), "RightShin"),
         (("LeftHand", "LeftForeArm"), "LeftShin"), (("LeftHand", "LeftForeArm"), "RightShin"),
         (("LeftShin",), "RightShin"), (("LeftFoot",), "RightShin"), (("RightFoot",), "LeftShin"), (("LeftFoot",), "RightFoot"),
         (("RightHand", "RightForeArm"), "LeftForeArm"), (("RightHand", "RightForeArm"), "LeftHand"),
         (("RightHand", "RightForeArm"), "torso"), (("LeftHand", "LeftForeArm"), "torso"), (("RightHand", "RightForeArm"), "head")]


def standing(clip, n):
    """Кадры, где корень стоит (стопы на земле не должны ехать)."""
    if clip in SLASH: return list(range(6, n))
    if clip in FLIGHT: return list(range(6, n))
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
    """(метка, кадр_от, кадр_до, тик_удара)."""
    out = []
    if clip in FLIGHT:
        for F, fr in RT.items():
            for k in range(1, len(fr)):
                out.append(("F%d t%d" % (F, k), fr[k - 1], fr[k], clip in SLASH and k == F))
        for f in range(6, n - 1):
            out.append(("опора %d→%d" % (f, f + 1), f, f + 1, clip in SLASH and f == 6))
    else:
        for f in range(n - 1):
            out.append(("%d→%d" % (f, f + 1), f, f + 1, (f, f + 1) in STRIKE.get(clip, ())))
    return out


def speed(clip, snaps):
    bad, worst = [], {}
    import os
    dump = os.environ.get("SQ_DUMP", "")
    for lab, a, b, strike in ticks(clip, len(snaps)):
        d = deltas(interp(snaps, a), interp(snaps, b))
        if dump: print("DUMP %s %-10s %s" % (clip[len(P):], lab, " ".join("%s %.1f" % (n, d[n]) for n in dump.split(","))))
        for bone, ang in d.items():
            lim = LIM_STRIKE if (strike and bone in SABRE) else LIM
            key = "sabre" if bone in SABRE else "body"
            if ang > worst.get(key, (0,))[0]: worst[key] = (round(ang, 1), lab, bone, strike)
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

    def blade_hits(self, vs):
        """Клинок (корень→остриё, как ставит игра) сквозь тело: луч по клинку в обе стороны против сетки без правой кисти/предплечья."""
        from mathutils.bvhtree import BVHTree
        ids = [i for i, p in enumerate(self.body.tpart) if p not in ("RightHand", "RightForeArm")]
        tree = BVHTree.FromPolygons(vs, [self.body.tris[i] for i in ids], all_triangles=True)
        r, t = blade(self.rig.dst)
        d = t - r; L = d.length; d.normalize()
        hits = []
        for a, dirv in ((r, d), (t, -d)):
            loc, nor, fi, dist = tree.ray_cast(a, dirv, L)
            if loc is not None: hits.append((self.body.tpart[ids[fi]], round(dist / L, 2)))
        return hits

    def contacts(self, vs):
        hits = []
        for a, b in PAIRS:
            tri, deep, worst, _ = self.body.contact(list(a), b, vs)
            if tri or deep: hits.append(("+".join(a), b, tri, deep, round(worst, 4)))
        return hits


def pose_metrics(rig):
    d = rig.dst
    v = rig.P("Neck") - rig.P("Hips")
    lean = math.degrees(math.atan2(-v.y, v.z))
    pyaw, cyaw = rig.pelvis_yaw(), rig.chest_yaw()
    hm = rig.head_m()
    r, t = blade(d); bd = (t - r).normalized()
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    return dict(lean=round(lean, 1), twist=round((cyaw - pyaw + 180) % 360 - 180, 1), pyaw=round(pyaw, 1), cyaw=round(cyaw, 1),
                relP=hm["relP"], relY=hm["relY"], fore=round(rig.forearm_twist("Right"), 1), foreL=round(rig.forearm_twist("Left"), 1),
                blade_yaw=round(yaw_of(bd), 1), blade_pitch=round(math.degrees(math.asin(max(-1, min(1, bd.z)))), 1),
                hip_ratio=round((rig.P("Hips") - rig.rest[M("Hips")].translation).length / L, 3),
                reach=round((rig.P("RightHand") - rig.P("RightArm")).length / sum(rig.ARM["Right"][x] for x in ("L1", "L2")), 2))


def check(rig, body, snaps_by_clip, quick=False):
    """Возвращает (сводка, нарушения)."""
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
        rows, lows, prev_vs = [], [], None
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
            bh = mesh.blade_hits(vs)
            m["blade_hits"] = bh
            if bh: viol.append((clip, "клинок сквозь тело f%d" % f, bh))
            if min(low.values()) < -0.004: viol.append((clip, "под землёй f%d" % f, m["feet"]))
            if clip in FLIGHT and 1 <= f <= 5:
                for s in SIDES:
                    if low[s] < AIR: viol.append((clip, "стопа в полёте f%d" % f, (s, round(low[s], 4))))
            if m["lean"] < 0: viol.append((clip, "наклон назад f%d" % f, m["lean"]))
            if abs(m["twist"]) > 45: viol.append((clip, "скрутка f%d" % f, m["twist"]))
            if abs(m["fore"]) > 70: viol.append((clip, "крутка предплечья f%d" % f, m["fore"]))
            if m["relP"] > base_relP + 0.5: viol.append((clip, "голова вверх f%d" % f, m["relP"]))
            if abs(m["relY"] - base_relY) > 30.5: viol.append((clip, "голова вбок f%d" % f, m["relY"]))
            # скольжение: вершины стопы у земли в обоих кадрах, пока корень стоит
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
        # дробные кадры раскладок полёта: стопы над землёй
        frac_low = None
        if clip in FLIGHT:
            xs = sorted({x for fr in RT.values() for x in fr if 0 < x < 6 and abs(x - round(x)) > 1e-6})
            for x in xs:
                vs = mesh.at(interp(snaps, x))
                low = mesh.feet_low(vs)
                lo = min(low.values())
                frac_low = lo if frac_low is None else min(frac_low, lo)
                if lo < AIR: viol.append((clip, "стопа в полёте x%.2f" % x, {s: round(v, 4) for s, v in low.items()}))
        summary[clip] = dict(worst=worst, n_bad=len(bad), slide=(round(slide_max[0], 4), slide_max[1]), frac_low=frac_low, rows=rows)
    return summary, viol


def print_summary(summary, viol):
    for c, s in summary.items():
        rows = s["rows"]
        fl = [min(r["feet"].values()) for i, r in enumerate(rows) if c in FLIGHT and 1 <= i <= 5]
        print("CHK %-22s bad=%-3d worst body %s sabre %s | air %s frac %s | slide %s | lean %.1f..%.1f tw %.0f fore %.0f..%.0f hip %.3f" % (
            c[len(P):], s["n_bad"], s["worst"].get("body"), s["worst"].get("sabre"),
            round(min(fl), 3) if fl else "-", round(s["frac_low"], 3) if s["frac_low"] is not None else "-", s["slide"],
            min(r["lean"] for r in rows), max(r["lean"] for r in rows), max(abs(r["twist"]) for r in rows),
            min(r["fore"] for r in rows), max(r["fore"] for r in rows), max(r["hip_ratio"] for r in rows)))
        for i, r in enumerate(rows):
            print("   f%02d lean %5.1f tw %5.1f pY %6.1f cY %6.1f relP %5.1f relY %6.1f fore %6.1f bl %6.1f/%5.1f reach %.2f feet L %.3f R %.3f %s" % (
                i, r["lean"], r["twist"], r["pyaw"], r["cyaw"], r["relP"], r["relY"], r["fore"], r["blade_yaw"], r["blade_pitch"], r["reach"],
                r["feet"]["Left"], r["feet"]["Right"], r.get("contacts") or ""))
    print("VIOLATIONS", len(viol))
    for v in viol[:400]: print("  V", v[0][len(P):], v[1], v[2])
