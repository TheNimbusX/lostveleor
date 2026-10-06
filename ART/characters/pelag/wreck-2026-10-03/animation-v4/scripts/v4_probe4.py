import bpy, sys, math
W = sys.argv[sys.argv.index("--") + 1]; clip = sys.argv[sys.argv.index("--") + 2]; SIDE = sys.argv[sys.argv.index("--") + 3] if len(sys.argv) > sys.argv.index("--") + 3 else "Right"
bpy.ops.wm.open_mainfile(filepath=W + "/Pelag_Wreck4_Work.blend")
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
arm.animation_data.action = bpy.data.actions["Pelag_AN_Wreck4_" + clip]
prev = None
P = lambda n: arm.matrix_world @ arm.pose.bones["mixamorig:" + n].head
for k in range(0, 2 * int(arm.animation_data.action.frame_range[1]) + 1):
    f = k / 2; bpy.context.scene.frame_set(int(f), subframe=f - int(f))
    q = {n: (arm.matrix_world @ arm.pose.bones["mixamorig:" + n].matrix).to_quaternion() for n in (SIDE + "Arm", SIDE + "ForeArm", SIDE + "Hand")}
    d = {n: (math.degrees(prev[n].rotation_difference(q[n]).angle) if prev else 0) for n in q}
    print("%4.1f fist %s elbow %s  dArm %5.1f dFore %5.1f dHand %5.1f" % (f, tuple(round(c, 2) for c in P(SIDE + "Hand")), tuple(round(c, 2) for c in P(SIDE + "ForeArm")), d[SIDE + "Arm"], d[SIDE + "ForeArm"], d[SIDE + "Hand"]))
    prev = q
