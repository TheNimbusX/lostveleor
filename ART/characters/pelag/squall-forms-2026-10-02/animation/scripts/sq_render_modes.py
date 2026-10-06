# Шквал v2: режимы съёмки (выполняется внутри sq_render.py).
report = []
FLIGHT_END = 6       # кадр контакта Forehand/Backhand: полёт [0..6] перевременяется на F тиков


def retime(k, F):
    """Тик полёта k (0..F) → кадр клипа: тик 1 = толчок (кадр 1), дальше равномерно до контакта (кадр 6)."""
    if k <= 0: return 0.0
    if F == 2: return 3.0 if k == 1 else 6.0
    return 1.0 + (FLIGHT_END - 1) * (k - 1) / (F - 1)


if mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 360
    for c in CLIPS:
        f0, f1 = TR.range[c]
        for t in range(0, f1 - f0 + 1):
            ratio = pose_at(c, t)
            feet = {s: round(min((tgt.matrix_world @ tgt.pose.bones[f"mixamorig:{s}{b}"].head).z for b in ("ToeBase", "Toe_End", "Foot")), 3)
                    for s in ("Left", "Right")}
            report.append(dict(clip=c, frame=t, hip_offset_ratio=round(ratio, 3), feet_min_z=feet))
            for vn, (vd, pitch) in VIEWS.items():
                look(vd, pitch, Vector((0, 0.05, 0.75)), 2.5)
                sc.render.filepath = os.path.join(out, f"{c[len(P):]}_{vn}_{t:03d}.png")
                bpy.ops.render.render(write_still=True)
    json.dump(dict(frames=report, max_hip_offset_ratio=max(r["hip_offset_ratio"] for r in report)),
              open(os.path.join(out, "v6_check.json"), "w"), indent=1)
    print("sheet frames done; max hip ratio", max(r["hip_offset_ratio"] for r in report))
else:
    # Серия из 4 прыжков в масштабе игры: зигзаг 2,1 / 1,7 / 2,4 / 1,8 м, полёт = clamp(round(d / 0,66), 2, 6) тиков,
    # опора 2 тика, поворот корня к следующей цели — S-кривая на 2 тика опоры + 1-й тик полёта (как в плане вида).
    # путь поперёк кадра игровой камеры: u — вдоль экрана, v — в глубину (зигзаг)
    au = Vector((math.sin(a15), math.cos(a15), 0)); av = Vector((math.cos(a15), -math.sin(a15), 0))
    loc = [(0, 0), (2.0, 0.65), (3.35, -0.4), (5.6, 0.45), (7.25, -0.25)]
    pts = [au * (u - 3.6) + av * v for u, v in loc]
    dirs = [(pts[i + 1] - pts[i]).normalized() for i in range(4)]
    yaw_of = lambda d: math.atan2(d.x, -d.y)
    seq = []          # (клип, кадр, позиция, рысканье)
    # Опора: левая стопа стоит (кадры 6–8), поэтому поворот корня к следующей цели — вокруг её лодыжки, а не центра корня.
    pose_at(P + "Forehand", 6.0)
    la = tgt.matrix_world @ tgt.pose.bones["mixamorig:LeftFoot"].head
    a_loc = Vector((la.x, la.y, 0.0))
    Rz = lambda y: Matrix.Rotation(y, 3, 'Z')
    y0 = yaw_of(dirs[0])
    for _ in range(6): seq.append((P + "Load", 0.0, pts[0], y0))
    for k in (1, 2): seq.append((P + "Load", float(k), pts[0], y0))
    start = pts[0]
    for i in range(4):
        clip = P + ("Forehand" if i % 2 == 0 else "Backhand")
        F = max(2, min(6, round((pts[i + 1] - start).length / 0.66)))
        ya = yaw_of((pts[i + 1] - start).normalized())
        for k in range(1, F + 1):
            seq.append((clip, retime(k, F), start.lerp(pts[i + 1], k / F), ya))
        if i < 3:
            A = pts[i + 1] + Rz(ya) @ a_loc                     # лодыжка левой стопы в мире
            yb0 = yaw_of(dirs[i + 1]); dy = (yb0 - ya + math.pi) % (2 * math.pi) - math.pi
            for sidx, fr in ((1, 7.0), (2, 8.0)):
                s_ = sidx / 3.0; s_ = s_ * s_ * (3 - 2 * s_)
                yy = ya + dy * s_
                seq.append((clip, fr, A - Rz(yy) @ a_loc, yy))
            start = A - Rz(ya + dy) @ a_loc                      # первый тик полёта дотягивает поворот вокруг той же лодыжки
        else:
            fin = P + ("FinishFore" if i % 2 == 0 else "FinishBack")
            for fr in range(1, 10): seq.append((fin, float(fr), pts[4], ya))
            for _ in range(10): seq.append((fin, 9.0, pts[4], ya))
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    for n, (clip, fr, pos, yaw) in enumerate(seq):
        pose_at(clip, fr, pos, yaw)
        look(VIEWS["game"][0], 48, Vector((0.0, 0.0, 0.6)), 10.5)
        sc.render.filepath = os.path.join(out, f"gif_{n:03d}.png")
        bpy.ops.render.render(write_still=True)
    json.dump([dict(clip=c, frame=f, x=round(p.x, 2), y=round(p.y, 2), yaw_deg=round(math.degrees(y), 1)) for c, f, p, y in seq],
              open(os.path.join(out, "gif_seq.json"), "w"), indent=1)
    print("gif frames", len(seq))
