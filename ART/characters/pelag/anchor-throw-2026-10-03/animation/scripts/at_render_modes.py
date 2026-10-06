# Бросок якоря: режимы съёмки (выполняется внутри at_render.py). Голова — по формуле Sim AnchorThrowHead
# (razlom/Assets/Game.Sim/Core/Simulation.AnchorThrow.cs), кадры клипов — по раскладкам at_check.layout (W, F, R).
import at_check
report = []
R_FOE = 0.85                                   # Хранитель
HAND, STEP, RET_STEP = 0.5, 1.0, 0.8           # AbordageHandReach, AnchorThrowFlightStep, AnchorThrowReturnStep
BEND = 2                                       # MaelstromPullBend


def prog(k, R):
    """MaelstromPullProgress(k, R): разгон Водоворота (b = 2)."""
    if k <= 0: return 0.0
    if k >= R: return 1.0
    return k * (R + BEND * k) / ((BEND + 1) * R * R)


def place_head(ring, d):
    """Голова якоря размера игры (0,94 м): кольцо сзади, веретено вперёд по d, рога поперёк у носа."""
    crown = ring + d * 0.80
    place(anchor, ring, crown, 0.07)
    side = d.cross(Vector((0, 0, 1)))
    if side.length < 1e-6: side = Vector((1, 0, 0))
    side.normalize()
    place(anchor_arm, crown - d * 0.06 - side * 0.36, crown - d * 0.06 + side * 0.36, 0.08)


def grip():
    """Кольцо рукояти в левой кисти: кость LeftHand + немного к пальцам (grip_socket.json — 4,5 см по кости)."""
    h, m = bone("LeftHand"), bone("LeftHandMiddle1")
    return h + (m - h) * 0.55


if mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 360
    for c in CLIPS:
        f0, f1 = TR.range[c]
        for t in range(0, f1 - f0 + 1):
            ratio = pose_at(c, t)
            feet = {s: round(min(bone(s + b).z for b in ("ToeBase", "Toe_End", "Foot")), 3) for s in ("Left", "Right")}
            report.append(dict(clip=c, frame=t, hip_offset_ratio=round(ratio, 3), feet_min_z=feet))
            line = c in (P + "Fly", P + "Yank", P + "Haul") or (c == P + "Throw" and t >= 2)
            hide((chain, anchor, anchor_arm), not line)
            if line:                                                      # голова далеко на линии: цепь прямая из рукояти
                g = grip(); ring = Vector((g.x, g.y - 2.6, 0.9))
                place(chain, g, ring, 0.022); place_head(ring, Vector((0, -1, 0)))
            for vn, (vd, pitch) in VIEWS.items():
                look(vd, pitch, Vector((0, -0.05, 0.75)), 2.5)
                sc.render.filepath = os.path.join(out, f"{c[len(P):]}_{vn}_{t:03d}.png")
                bpy.ops.render.render(write_still=True)
    json.dump(dict(frames=report, max_hip_offset_ratio=max(r["hip_offset_ratio"] for r in report)),
              open(os.path.join(out, "v6_check.json"), "w"), indent=1)
    print("sheet frames done; max hip ratio", max(r["hip_offset_ratio"] for r in report))
else:
    # Три броска в масштабе игры: 7 м в линию трёх Хранителей (2 / 4 / 6 м), стена на 4 м, стена на 2 м.
    import bmesh
    foes = []
    for i in range(3):
        o = bpy.data.objects.new("foe%d" % i, bpy.data.meshes.new("foe%d" % i))
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=R_FOE * 0.8, radius2=R_FOE * 0.6, depth=2.0)
        bm.to_mesh(o.data); bm.free(); o.data.materials.append(FOE); sc.collection.objects.link(o); foes.append(o)
    wall = unit_box("wall", solid("wall", (0.35, 0.33, 0.30, 1)))
    au = Vector((math.sin(a15), math.cos(a15), 0))           # вдоль экрана игровой камеры
    yaw_of = lambda d: math.atan2(d.x, -d.y)
    seq = []
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    n = 0
    for reach, foe_at in ((7.0, (2.0, 4.0, 6.0)), (4.0, ()), (2.0, ())):
        W = 2
        F = max(1, min(10, math.ceil((reach - HAND) / STEP)))
        s = (reach - HAND) / F
        Rr = max(4, min(8, round((reach - HAND) / RET_STEP)))
        T = W + F + 1; catch = T + Rr; end = catch + 9
        lay = {t: (c, f) for t, c, f, _ in at_check.layout(W, F, Rr)}
        dirv = au.copy()
        start = -dirv * 2.6
        yaw = yaw_of(dirv)
        side = dirv.cross(Vector((0, 0, 1))).normalized()
        ring_spots = [start + (dirv * math.cos(a) - side * math.sin(a)) * 1.60 for a in (math.radians(66), 0.0, math.radians(-66))]
        hits = [W + max(1, math.ceil((d - R_FOE - HAND) / s)) for d in foe_at]
        for i, o in enumerate(foes):
            o.hide_render = i >= len(foe_at)
        wall.hide_render = not foe_at == ()
        if not foe_at: place(wall, start + dirv * (reach + 0.3) - side * 1.2 + Vector((0, 0, 0.6)), start + dirv * (reach + 0.3) + side * 1.2 + Vector((0, 0, 0.6)), 0.5, 1.2)
        rel_hand = None
        for tick in range(0, end + 4):
            clip, fr = lay.get(tick, (P + "Catch", 9.0))
            pose_at(clip, fr, start, yaw)
            hR = bone("RightHand"); g = grip()
            hand_pt = start + dirv * HAND
            if tick <= W:
                ring = hR + Vector((0, 0, 0.02)); hz = None
            elif tick < W + F:
                k = tick - W; at = min(reach, HAND + s * k)
                ring = start + dirv * at; ring.z = 1.3 + (0.9 - 1.3) * at / reach
            elif tick <= T:
                ring = start + dirv * reach; ring.z = 0.9
            elif tick < catch:
                p_ = prog(tick - T, Rr)
                ring = (start + dirv * reach).lerp(hand_pt, p_)
                ring.z = 0.45 if tick < catch - 1 else (0.45 + hR.z) * 0.5
            else:
                ring = hR + Vector((0, 0, 0.02))
            on = tick <= catch + 2
            hide((anchor, anchor_arm), not on)
            hide((chain,), not (W <= tick <= catch))
            if on:
                d = dirv if W <= tick < catch else Vector((0, 0, -1))
                place_head(ring, d)
            if W <= tick <= catch: place(chain, g, ring, 0.022)
            for i, dd in enumerate(foe_at):
                pos = start + dirv * dd
                if tick > T:
                    pos = pos.lerp(ring_spots[i], prog(tick - T, Rr))
                foes[i].location = pos + Vector((0, 0, 1.0))
            look(VIEWS["game"][0], 48, Vector((0.0, 0.0, 0.6)), 10.5)
            sc.render.filepath = os.path.join(out, f"gif_{n:03d}.png")
            bpy.ops.render.render(write_still=True)
            seq.append(dict(reach=reach, F=F, R=Rr, T=T, catch=catch, tick=tick, clip=clip, frame=round(fr, 2),
                            hit=[h for h in hits if h == tick]))
            n += 1
    json.dump(seq, open(os.path.join(out, "gif_seq.json"), "w"), indent=1)
    print("gif frames", n)
