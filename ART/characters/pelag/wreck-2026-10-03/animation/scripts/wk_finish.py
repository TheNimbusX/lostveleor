# Крушение v2: стыки, пределы, проверочный кистень, выгрузка FBX/Bind/.blend и timing.json.
# Выполняется внутри wk_author.py (exec, общее пространство имён: rig, body, snaps, rows, CLIPS, stance_snap, OUT, P).
import wk_check, wk_flail
from wk_sample import big_bone_deltas

# ------------------------------------------------------------- стыки (крупные кости ≤ 5°)
SEAMS = [("stance", 0, P + "Swing1", 0), (P + "Swing1", 8, P + "Swing2", 0), (P + "Swing1", 12, P + "Wait1", 0),
         (P + "Swing2", 7, P + "Slam", 0), (P + "Swing2", 11, P + "Wait2", 0), (P + "Wait1", 12, P + "Wait1", 0),
         (P + "Wait2", 12, P + "Wait2", 0), (P + "Slam", 20, P + "Stow", 0), (P + "Stow", 7, "stance", 0),
         # формы (лист B2): Панцирь — те же кадры, что база; Волнорез — после Swing2 7; Девятый вал — цикл и отпускание
         ("stance", 0, P + "Swing1_Braced", 0), (P + "Swing1_Braced", 8, P + "Swing2_Braced", 0),
         (P + "Swing1_Braced", 12, P + "Wait1", 0), (P + "Swing2_Braced", 7, P + "Slam", 0), (P + "Swing2_Braced", 11, P + "Wait2", 0),
         (P + "Swing2", 7, P + "Slam_Drag", 0), (P + "Slam_Drag", 21, P + "Stow", 0),
         (P + "Charge", 12, P + "Charge", 0), (P + "Charge", 3, P + "ChargeRelease", 0), (P + "ChargeRelease", 15, P + "Stow", 0)]
BLENDS = [(P + "Slam", 5, P + "Charge", 0, "вход в заряд: вид смешивает 3 кадра (правая стоит, левая доходит шаг шире)")]
seams = []
for a, fa, b, fb in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sa = stance_snap if a == "stance" else snaps[a][fa]
    sb = stance_snap if b == "stance" else snaps[b][fb]
    w = big_bone_deltas(sa, sb)
    seams.append(dict(from_clip=a, from_frame=fa, to_clip=b, to_frame=fb, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("SEAM %s@%d -> %s@%d: %.2f° %s" % (a, fa, b, fb, w[0], w[1]))

blends = []
for a, fa, b, fb, why in BLENDS:
    if a in snaps and b in snaps:
        w = big_bone_deltas(snaps[a][fa], snaps[b][fb])
        blends.append(dict(from_clip=a, from_frame=fa, to_clip=b, to_frame=fb, max_bone_deg=round(w[0], 2), bone=w[1], note=why))
        print("BLEND %s@%d -> %s@%d: %.2f° %s" % (a, fa, b, fb, w[0], w[1]))

# ------------------------------------------------------------- пределы
summary, viol = wk_check.check(rig, body, dict(snaps, _stance=stance_snap), rows, quick=bool(os.environ.get("WK_QUICK")))
wk_check.print_summary(summary, viol)

# ------------------------------------------------------------- проверочный кистень по треку хвата (кольцо рукояти)
def ring_track(clip, a, b):
    return [tuple(rows[clip][f]["ring"]) for f in range(a, b + 1)]


SIM = {}
if all(P + c in rows for c in ("Swing1", "Swing2", "Slam")):
    # серия в самом быстром темпе: Swing1 0..8 (8 = Swing2 0) → Swing2 1..7 (7 = Slam 0) → Slam 1..20; удары 7 / 14 / 24
    track = ring_track(P + "Swing1", 0, 8) + ring_track(P + "Swing2", 1, 7) + ring_track(P + "Slam", 1, 20)
    rig.restore(snaps[P + "Swing1"][0])
    sp2 = rig.P("Spine2")
    back = (-sp2.y - 0.20, sp2.x + 0.05, sp2.z - 0.12)                    # голова на спине (центр), оси корня
    held = [back, back, back]

    def report(samples, label, c0=0):
        out = {}
        for nm, c in (("swing1", 7), ("swing2", 14)):
            if c >= c0: out[nm] = wk_flail.contact_report(samples, c - c0)
        ground = next((s for s in samples if s[0] >= 20 - c0 and s[1][2] <= wk_flail.HEAD_FLOOR + 1e-4), None)
        out["slam_contact_frame"] = wk_flail.contact_report(samples, 24 - c0)
        if ground:
            x, p_, v_, t_, _ = ground
            out["slam_first_ground"] = dict(frame=round(x + c0, 3), head=[round(c, 3) for c in p_],
                                            dist=round(math.hypot(p_[0], p_[1]), 3), speed=round(wk_flail.norm(v_), 2))
        out["path"] = [[round(s[0] + c0, 3)] + [round(c, 3) for c in s[1]] + [round(wk_flail.norm(s[2]), 2), int(s[3])]
                       for s in samples if abs(s[0] * 2 - round(s[0] * 2)) < 1e-6]
        SIM[label] = out
        print("SIM", label, {k: v for k, v in out.items() if k != "path"})

    s_back = wk_flail.simulate(track, back, held_until=1.0, held_track=held)
    report(s_back, "from_back")
    # тот же хват, если голова в кадре 7 уже на дуге маха (плановый контакт: 2,35 м впереди, 24 м/с влево)
    s_ideal = wk_flail.simulate(track[7:], (2.35, 0.0, 0.80), vel0=(0.0, 24.0, 0.0))
    report(s_ideal, "from_ideal_swing1_contact", c0=7)
    SIM["grip_speed"] = dict(zip(("speed_ms", "accel_ms2"), wk_flail.grip_speed(track)))

if P + "Charge" in rows:
    # Девятый вал: «вертолёт» 4 оборота по треку хвата (голова на плановой оси, 30 м/с по кругу), затем отпускание
    loop = ring_track(P + "Charge", 0, 11)
    r0 = rows[P + "Charge"][0]; cd = r0["chain_dir"]
    az = math.atan2(cd[1], cd[0])
    head0 = tuple(r0["ring"][i] + wk_flail.R * c for i, c in enumerate((math.cos(az), math.sin(az), 0.0)))
    v0 = (-30.0 * math.sin(az), 30.0 * math.cos(az), 0.0)
    N = 4
    tr = loop * N + [loop[0]]
    smp = wk_flail.simulate(tr, head0, vel0=v0)
    cx_ = sum(p_[0] for p_ in loop) / len(loop); cy_ = sum(p_[1] for p_ in loop) / len(loop)
    revs = []
    for k_ in range(N + 1):
        x, p_, v_, t_, _ = wk_flail.at_frame(smp, 12 * k_)
        revs.append(dict(frame=12 * k_, head=[round(c, 3) for c in p_], radius=round(math.hypot(p_[0] - cx_, p_[1] - cy_), 3),
                         speed=round(wk_flail.norm(v_), 2), taut=bool(t_)))
    hs = [s_[1][2] for s_ in smp]
    SIM["charge_loop"] = dict(revolutions=revs, head_height=[round(min(hs), 3), round(max(hs), 3)],
                              taut_share=round(sum(1 for s_ in smp if s_[3]) / len(smp), 3),
                              grip_speed=dict(zip(("speed_ms", "accel_ms2"), wk_flail.grip_speed(loop + loop[:2]))))
    print("SIM charge_loop", {k_: v_ for k_, v_ in SIM["charge_loop"].items() if k_ != "grip_speed"})
    if P + "ChargeRelease" in rows:
        rel = ring_track(P + "ChargeRelease", 0, 15)
        smp2 = wk_flail.simulate(loop * 2 + loop[:3] + rel, head0, vel0=v0)
        c0 = 27
        ground = next((s_ for s_ in smp2 if s_[0] >= c0 and s_[1][2] <= wk_flail.HEAD_FLOOR + 1e-4), None)
        out = dict(note="2 оборота заряда с кадра 0 и отпускание в кадре 3 цикла (= ChargeRelease 0; anchor-core: 8 запечек по фазе)",
                   contact_frame=wk_flail.contact_report(smp2, c0 + 4))
        if ground:
            x, p_, v_, t_, _ = ground
            out["first_ground"] = dict(frame=round(x - c0, 3), head=[round(c, 3) for c in p_], dist=round(math.hypot(p_[0], p_[1]), 3),
                                       speed=round(wk_flail.norm(v_), 2))
        SIM["charge_release"] = out
        print("SIM charge_release", out)
        # отпускание из каждой фазы цикла (Charge k → ChargeRelease 1…, первый кадр — кадр k цикла): голова в кадре 4, первая земля
        byph = []
        for k_ in range(12):
            lp = loop * 3 + loop[:k_]
            s3 = wk_flail.simulate(lp + [loop[k_]] + rel[1:], head0, vel0=v0)
            c_ = len(lp)
            g3 = next((s_ for s_ in s3 if s_[0] >= c_ and s_[1][2] <= wk_flail.HEAD_FLOOR + 1e-4), None)
            r4 = wk_flail.contact_report(s3, c_ + 4)
            byph.append(dict(phase=k_, f4_head=r4["head"], f4_speed=r4["speed"],
                             first_ground=None if g3 is None else dict(frame=round(g3[0] - c_, 2), head=[round(c, 2) for c in g3[1]],
                                                                        dist=round(math.hypot(g3[1][0], g3[1][1]), 2))))
        SIM["charge_release_by_phase"] = byph
        for b_ in byph: print("SIM release phase", b_)

exec(open(os.path.join(HERE, "wk_export.py"), encoding="utf-8").read())
exec(open(os.path.join(HERE, "wk_timing.py"), encoding="utf-8").read())
