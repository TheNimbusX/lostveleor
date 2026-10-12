"""Скачки по ключам мастер-действия (Blender): blender -b -P f_jumps.py -- <work_dir> [bones,..] [t0:t1]
Угол локального поворота кости за 1/60 с (2 ключа по 1/120) и смена угловой скорости; топ без пальцев."""
import bpy, sys, os, math
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=os.path.join(argv[0], "Pelag_AnchorThrow2_Work.blend"))
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
act = bpy.data.actions["AnchorThrow2_Master"]
HK = 4; STEP = 2                     # 60 к/с
q = {}
for fc in act.fcurves if hasattr(act, "fcurves") else []:
    pass
names = [pb.name for pb in rig.pose.bones]
sc = bpy.context.scene
N = int(act.frame_range[1])
rows = []
for f in range(0, N + 1, STEP):
    sc.frame_set(f)
    rows.append({n: rig.pose.bones[n].rotation_quaternion.copy() for n in names})
FING = ("Thumb", "Index", "Middle", "Ring", "Pinky")
sel = argv[1].split(",") if len(argv) > 1 and argv[1] else None
win = [float(x) for x in argv[2].split(":")] if len(argv) > 2 else None
out = []
prev_w = {}
for i in range(1, len(rows)):
    t = i * STEP / HK
    for n in names:
        if any(x in n for x in FING): continue
        a, b = rows[i - 1][n], rows[i][n]
        d = a.rotation_difference(b)
        ang = math.degrees(d.angle); ang = min(ang, 360 - ang)
        w = d.axis * ang
        j = (w - prev_w[n]).length if n in prev_w else 0.0
        prev_w[n] = w
        out.append((ang, j, t, n.replace("mixamorig:", "")))
if sel:
    for t0 in sorted(set(o[2] for o in out)):
        if win and not (win[0] <= t0 <= win[1]): continue
        print("t%5.2f " % t0 + " ".join("%s %5.1f/%5.1f" % (o[3], o[0], o[1]) for o in out if o[2] == t0 and o[3] in sel))
else:
    print("TOP angle/60fps:")
    for o in sorted(out, reverse=True)[:25]: print("  %6.1f  jerk %6.1f  t%5.2f  %s" % o)
    print("TOP jerk:")
    for o in sorted(out, key=lambda o: -o[1])[:25]: print("  %6.1f  jerk %6.1f  t%5.2f  %s" % o)
