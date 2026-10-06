# Абордаж v2: режимы съёмки (выполняется внутри ab_render.py).
from ab_check import throw_retime, pull_retime
report = []
R_FOE, R_HERO, GAP = 0.85, 0.45, 0.10          # Хранитель, герой, зазор посадки (спека 2.3: посадка 1,40 м от центра)

if mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 360
    hide((chain, anchor, anchor_arm), True)
    for c in CLIPS:
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
    # Три броска в масштабе игры: цель (Хранитель, r 0,85) в 2 / 5 / 7 м. Тики как в спеке 2.2:
    # A = clamp(ceil((d − 0,5 − r) / 1,5), 1, 6); L = d − 1,40; P = clamp(round(L / 0,66), 2, 12).
    foe = bpy.data.objects.new("foe", bpy.data.meshes.new("foe"))
    import bmesh
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=R_FOE * 0.8, radius2=R_FOE * 0.6, depth=2.0)
    bm.to_mesh(foe.data); bm.free(); foe.data.materials.append(FOE); sc.collection.objects.link(foe)
    au = Vector((math.sin(a15), math.cos(a15), 0))           # вдоль экрана игровой камеры
    yaw_of = lambda d: math.atan2(d.x, -d.y)
    seq = []
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    n = 0
    for d in (2.0, 5.0, 7.0):
        A = max(1, min(6, math.ceil((d - 0.5 - R_FOE) / 1.5)))
        L = d - (R_FOE + R_HERO + GAP)
        Pn = max(2, min(12, round(L / 0.66)))
        dirv = au.copy()
        start = -dirv * (d / 2)
        centre = start + dirv * d
        land = start + dirv * L
        foe.location = centre + Vector((0, 0, 1.0))
        yaw = yaw_of(dirv)
        hook = centre - dirv * R_FOE * 0.8 + Vector((0, 0, 1.0))
        frames = []                                           # (клип, кадр, корень, тик)
        for k, fr in enumerate(throw_retime(A)):
            frames.append((P + "Throw", fr, start, k))
        B = 2 + A
        for k, fr in enumerate(pull_retime(Pn)):
            if k == 0: continue
            frames.append((P + "Pull", fr, start.lerp(land, k / Pn), B + k))
        hit = B + Pn
        for k in (1, 2, 3): frames.append((P + "Punch", float(k), land, hit + k))
        for k in range(1, 7): frames.append((P + "Recover", float(k), land, hit + 3 + k))
        for k in range(1, 7): frames.append((P + "Recover", 6.0, land, hit + 9 + k))
        if os.environ.get("AB_GIF_HALF"):     # правка 02.10: и полутики — вид на 60 к/с проходит клип по прямой между тиками
            half = [frames[0]]
            for (c0, f0, r0, t0), (c1, f1, r1, t1) in zip(frames, frames[1:]):
                if c0 == c1: half.append((c1, (f0 + f1) / 2, r0.lerp(r1, .5), t0 + .5))
                else: half.append((c1, f1 / 2, r0.lerp(r1, .5), t0 + .5))   # стык: кадр 0 нового клипа = конец прошлого
                half.append((c1, f1, r1, t1))
            frames = half
        rel_hand = None
        for clip, fr, root, tick in frames:
            pose_at(clip, fr, root, yaw)
            hR, hL = bone("RightHand"), bone("LeftHand")
            if tick <= 2:
                at = hR + Vector((0, 0, 0.05)); rel_hand = at.copy()
            elif tick <= B:
                at = rel_hand.lerp(hook, (tick - 2) / A)
            else:
                at = hook
            show_anchor = 1 <= tick <= hit
            hide((anchor, anchor_arm), not show_anchor)
            hide((chain,), not (2 <= tick <= hit))
            if show_anchor: place_anchor(at, dirv if tick >= 2 else Vector((0, 0, -1)))
            if 2 <= tick <= hit: place(chain, hL, at, 0.025)
            look(VIEWS["game"][0], 48, Vector((0.0, 0.0, 0.6)), 10.5)
            sc.render.filepath = os.path.join(out, f"gif_{n:03d}.png")
            bpy.ops.render.render(write_still=True)
            seq.append(dict(dist=d, A=A, P=Pn, tick=tick, clip=clip, frame=round(fr, 2), hit=hit))
            n += 1
    json.dump(seq, open(os.path.join(out, "gif_seq.json"), "w"), indent=1)
    print("gif frames", n)
