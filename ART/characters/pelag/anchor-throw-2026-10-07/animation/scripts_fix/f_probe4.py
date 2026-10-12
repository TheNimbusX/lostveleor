import bpy, sys, os
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=os.path.join(argv[0], "Pelag_AnchorThrow2_Work.blend"))
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
rig.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
P = lambda n: rig.matrix_world @ rig.pose.bones["mixamorig:" + n].head
for t in (16.0, 17.0, 18.0, 19.0, 19.5, 20.0):
    bpy.context.scene.frame_set(int(t * 4)); bpy.context.view_layer.update()
    ax = (P("RightHandIndex1") - P("RightHandPinky1")).normalized()
    print("T", t, "axis", tuple(round(x, 2) for x in ax), "hand", tuple(round(x, 3) for x in P("RightHand")), "sh", tuple(round(x, 3) for x in P("RightArm")))
