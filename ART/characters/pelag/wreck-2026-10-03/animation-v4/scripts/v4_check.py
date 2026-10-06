"""Крушение v4: пределы клипа (как wk_check v3) в МИРЕ с ходом корня выпада.

Отличия от wk_check: снимок может нести "_root" (сдвиг корня, как Sim везёт героя на выпаде) — стопы, скольжение и
пересечения меряются с ним, смещение таза — без него; скорость кости: ≤ 35°/тик вне удара, ≤ 70°/тик в окне удара
для ВСЕХ костей (формулировка задачи 06.10; у принятой сабли таз в махе 51–65°/тик).
"""
import math, bpy
from mathutils import Vector
import wk_check
from wk_check import Mesh, planted, deltas
from wk_rig import M, SIDES

LIM, LIM_STRIKE = 35.0, 70.0


def install_root(rig):
    """rig.restore ставит и сдвиг корня из снимка ("_root") — его видят меш, стопы и пересечения."""
    base = rig.restore.__func__ if hasattr(rig.restore, "__func__") else None

    def restore(snap):
        for pb in rig.dst.pose.bones:
            loc, rot = snap[pb.name]
            pb.location = loc; pb.rotation_quaternion = rot
        r = snap.get("_root")
        rig.dst.location = r[0] if r is not None else Vector((0, 0, 0))
        bpy.context.view_layer.update()
    rig.restore = restore
    return base


def speed(snaps, strike):
    bad, worst = [], (0.0, "")
    for f in range(len(snaps) - 1):
        st = strike[0] <= f < strike[1]
        for bone, ang in deltas(snaps[f], snaps[f + 1]).items():
            lim = LIM_STRIKE if st else LIM
            if ang > worst[0]: worst = (round(ang, 1), "%d→%d %s%s" % (f, f + 1, bone, " (удар)" if st else ""))
            if ang > lim + 1e-6: bad.append(("%d→%d" % (f, f + 1), bone, round(ang, 1), lim))
    return bad, worst


def check(rig, body, clip, snaps, rows, strike, stance, quick=False, sub_snaps=None):
    mesh = Mesh(rig, body)
    rig.restore(stance)
    base_relP = rig.head_m()["relP"]
    L = (rig.rest[M("Hips")].translation - rig.rest[M("Head")].translation).length
    base_contacts = {(a, b): (t, dd) for a, b, t, dd, w in mesh.contacts(body.verts())}
    viol = []
    bad, worst = speed(snaps, strike)
    for b in bad: viol.append((clip, "скорость", b))
    prev_vs, slide = None, (0.0, None)
    mx = dict(lean=(99, -99), twist=0.0, fore=0.0, relP=0.0, hip=0.0, low=9.0, gap=(9.0, 0.0))
    ncont = 0
    for f, snap in enumerate(snaps):
        vs = mesh.at(snap)
        r = rows[f]
        low = mesh.feet_low(vs)
        root = snap.get("_root", (Vector((0, 0, 0)),))[0]
        hip = (rig.P("Hips") - root - rig.rest[M("Hips")].translation).length / L
        relP = rig.head_m()["relP"] - base_relP
        fore = max(abs(v) for v in r["forearm_twist"].values())
        mx["lean"] = (min(mx["lean"][0], r["lean"]), max(mx["lean"][1], r["lean"]))
        mx["twist"] = max(mx["twist"], abs(r["twist"])); mx["fore"] = max(mx["fore"], fore)
        mx["relP"] = max(mx["relP"], abs(relP)); mx["hip"] = max(mx["hip"], hip); mx["low"] = min(mx["low"], min(low.values()))
        r.update(hip_ratio=round(hip, 3), relP_over_stance=round(relP, 2), feet_low={s: round(low[s], 4) for s in SIDES})
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
            for h in c: viol.append((clip, "пересечение f%d" % f, h)); ncont += 1
        if prev_vs is not None:
            for s in SIDES:
                med, n = planted(prev_vs, vs, mesh.feet[s])
                if med is not None:
                    if med > slide[0]: slide = (round(med, 4), "%s f%d→%d" % (s, f - 1, f))
                    if med > 0.01: viol.append((clip, "скольжение f%d→%d" % (f - 1, f), (s, round(med, 3), n)))
        prev_vs = vs
    sub = wk_check.subframe(mesh, sub_snaps or snaps, step=0.5 if sub_snaps else 0.25)
    if sub["low"][0] < -0.004: viol.append((clip, "под землёй между кадрами", sub["low"]))
    if sub["slide"][0] > 0.006: viol.append((clip, "скольжение между кадрами", sub["slide"]))
    summary = dict(worst=worst, n_bad=len(bad), slide=slide, sub=sub, lean=mx["lean"], twist=round(mx["twist"], 1),
                   fore=round(mx["fore"], 1), relP=round(mx["relP"], 2), hip=round(mx["hip"], 3), low=round(mx["low"], 4),
                   gap=mx["gap"], contacts=ncont)
    print("CHK %-7s speed %s bad=%d | lean %s tw %.0f fore %.0f relP %.1f hip %.3f low %.4f gap %s slide %s sub %s cont %d" % (
        clip, worst, len(bad), summary["lean"], summary["twist"], summary["fore"], summary["relP"], summary["hip"],
        summary["low"], summary["gap"], slide, sub, ncont))
    return summary, viol
