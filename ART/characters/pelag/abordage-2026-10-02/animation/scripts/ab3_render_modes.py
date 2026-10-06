# Абордаж v3: режимы съёмки (выполняется внутри ab3_render.py → ab_render.py).
from ab_check import pull_retime
from ab3_check import throw3_retime, SHORT_RT
report = []
R_FOE, R_HERO, GAP = 0.85, 0.45, 0.10          # Хранитель, герой, зазор посадки (спека 2.3: посадка 1,40 м от центра)
NEWC = [P + c for c in ("Throw", "PullShort", "Uppercut", "Slam")]

if mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 360
    hide((chain, anchor, anchor_arm), True)
    for c in NEWC:
        f0, f1 = TR.range[c]
        for t in range(0, f1 - f0 + 1):
            ratio = pose_at(c, t)
            feet = {s: round(min(bone(s + b).z for b in ("ToeBase", "Toe_End", "Foot")), 3) for s in ("Left", "Right")}
            report.append(dict(clip=c, frame=t, hip_offset_ratio=round(ratio, 3), feet_min_z=feet))
            for vn, (vd, pitch) in VIEWS.items():
                look(vd, pitch, Vector((0, -0.05, 0.75)), 2.5)
                sc.render.filepath = os.path.join(out, f"{c[len(P):]}_{vn}_{t:03d}.png")
                bpy.ops.render.render(write_still=True)
    json.dump(dict(frames=report, max_hip_offset_ratio=max(r["hip_offset_ratio"] for r in report)),
              open(os.path.join(out, "v6_check.json"), "w"), indent=1)
    print("sheet frames done; max hip ratio", max(r["hip_offset_ratio"] for r in report))
else:
    # Три каста в масштабе игры (цель — Хранитель r 0,85): A = clamp(ceil((d − 0,5 − r) / 1,5), 1, 6);
    # L = d − 1,40; P = clamp(round(L / 0,66), 2, 12). Короткая тяга P = 4…5 — PullShort, длиннее и P ≤ 3 — Pull.
    # Формы: Гейзер — Uppercut, Обвал — Slam (оба с кадра 11 Pull / 4 PullShort, тик B+P−1), база/Пробоина — Punch.
    foe = bpy.data.objects.new("foe", bpy.data.meshes.new("foe"))
    import bmesh
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=R_FOE * 0.8, radius2=R_FOE * 0.6, depth=2.0)
    bm.to_mesh(foe.data); bm.free(); foe.data.materials.append(FOE); sc.collection.objects.link(foe)
    au = Vector((math.sin(a15), math.cos(a15), 0))           # вдоль экрана игровой камеры
    yaw_of = lambda d: math.atan2(d.x, -d.y)
    smooth = lambda x: x * x * (3 - 2 * x)
    ANK_L = Vector((0.163, -0.476, 0.0))                     # левая лодыжка в осях корня (мир Blender при yaw 0)
    seq = []
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    n = 0
    CASTS = [(3.9, "Uppercut", False, "Гейзер"), (4.4, "Slam", False, "Обвал"), (7.0, "Punch", True, "база / Пробоина, цель за спиной")]
    for d, form, behind, label in CASTS:
        A = max(1, min(6, math.ceil((d - 0.5 - R_FOE) / 1.5)))
        L = d - (R_FOE + R_HERO + GAP)
        Pn = max(2, min(12, round(L / 0.66)))
        W = 4 if behind else 3
        dirv = au.copy()
        start = -dirv * (d / 2)
        centre = start + dirv * d
        land = start + dirv * L
        foe.location = centre + Vector((0, 0, 1.0))
        yaw = yaw_of(dirv)
        yaw0 = yaw + (math.pi if behind else 0.0)
        hook = centre - dirv * R_FOE * 0.8 + Vector((0, 0, 1.0))
        short = Pn in SHORT_RT
        pclip = P + ("PullShort" if short else "Pull")
        prt = SHORT_RT[Pn] if short else pull_retime(Pn)
        frames = []                                           # (клип, кадр, корень, рысканье, тик)
        for k, fr in enumerate(throw3_retime(W, A)):
            frames.append((P + "Throw", fr, None, k))
        B = W + A
        hit = B + Pn
        for k, fr in enumerate(prt):
            if k == 0: continue
            if form != "Punch" and k >= Pn - 1:
                frames.append((P + form, float(k - (Pn - 1)), start.lerp(land, k / Pn), B + k))
            else:
                frames.append((pclip, fr, start.lerp(land, k / Pn), B + k))
        hold = P + form
        f_off = 1 if form != "Punch" else 0
        for k in (1, 2, 3): frames.append((hold, float(k + f_off), land, hit + k))
        for k in range(1, 7): frames.append((P + "Recover", float(k), land, hit + 3 + k))
        for k in range(1, 5): frames.append((P + "Recover", 6.0, land, hit + 9 + k))
        half = [frames[0]]
        for (c0, f0, r0, t0), (c1, f1, r1, t1) in zip(frames, frames[1:]):
            rr = None if r0 is None and r1 is None else (r1 if r0 is None else r0.lerp(r1 if r1 is not None else r0, .5))
            if c0 == c1: half.append((c1, (f0 + f1) / 2, rr, t0 + .5))
            elif c1 in (P + "Uppercut", P + "Slam") and c0 in (P + "Pull", P + "PullShort"):
                half.append((c0, (f0 + (4.0 if c0 == P + "PullShort" else 11.0)) / 2, rr, t0 + .5))   # тяга доходит до своего «кулак пошёл»
            else: half.append((c1, f1 / 2, rr, t0 + .5))      # стык: кадр 0 нового клипа = конец прошлого
            half.append((c1, f1, r1, t1))
        rel_hand = None
        for clip, fr, root, tick in half:
            if root is None:                                  # замах: корень стоит, вид доворачивает его вокруг левой лодыжки
                u = smooth(min(1.0, tick / W))
                yw = yaw0 + ((yaw - yaw0 + math.pi) % (2 * math.pi) - math.pi) * u
                ank = start + Vector((0, 0, 0)) + (Matrix.Rotation(yaw, 3, 'Z') @ ANK_L)
                root = ank - Matrix.Rotation(yw, 3, 'Z') @ ANK_L
            else:
                yw = yaw
            pose_at(clip, fr, root, yw)
            hR, hL = bone("RightHand"), bone("LeftHand")
            fwd = Matrix.Rotation(yw, 3, 'Z') @ Vector((0, -1, 0))
            if tick < W:                                      # якорь на цепи за правым плечом
                at = hR - fwd * 0.26 + Vector((0, 0, -0.42)); rel_hand = hR.copy()
                dirA = Vector((0, 0, -1))
            elif tick <= B:
                at = hR.lerp(hook, (tick - W) / A) if tick > W else hR + Vector((0, 0, 0.05))
                dirA = dirv
            else:
                at = hook; dirA = dirv
            show_anchor = tick <= hit
            hide((anchor, anchor_arm), not show_anchor)
            hide((chain,), not (tick <= hit))
            if show_anchor: place_anchor(at, dirA)
            if tick <= hit: place(chain, hR if tick < W else hL, at, 0.025)
            look(VIEWS["game"][0], 48, Vector((0.0, 0.0, 0.6)), 10.5)
            sc.render.filepath = os.path.join(out, f"gif_{n:03d}.png")
            bpy.ops.render.render(write_still=True)
            seq.append(dict(dist=d, A=A, P=Pn, W=W, tick=tick, clip=clip, frame=round(fr, 2), hit=hit, form=label,
                            pull=pclip[len(P):]))
            n += 1
    json.dump(seq, open(os.path.join(out, "gif_seq.json"), "w"), indent=1)
    print("gif frames", n)
