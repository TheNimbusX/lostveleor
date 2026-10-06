# Абордаж v2: ключи, выгрузка FBX/Bind/.blend. Выполняется внутри ab_author.py (exec, общее пространство имён).
if not os.environ.get("AB_NOEXPORT"):
    dst = rig.dst
    dst.animation_data_create()
    actions = {}
    for name, ss in snaps.items():
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        dst.animation_data.action = act
        prev = {}
        for f, snap in enumerate(ss):
            for pb in dst.pose.bones:
                loc, rot = snap[pb.name]
                rot = rot.copy()
                q = prev.get(pb.name)
                if q is not None and q.dot(rot) < 0: rot.negate()
                prev[pb.name] = rot.copy()
                pb.location = loc; pb.rotation_quaternion = rot
                pb.keyframe_insert("location", frame=f, group=pb.name)
                pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
        actions[name] = act
        dst.animation_data.action = None
    bpy.ops.object.select_all(action='DESELECT')
    dst.select_set(True); bpy.context.view_layer.objects.active = dst
    dst.scale = rig.export_scale          # риг выгружается в своём масштабе (.01), как Dash/Roll v6
    bpy.context.view_layer.update()
    sc = bpy.context.scene
    for name, act in actions.items():
        dst.animation_data.action = act
        sc.frame_start, sc.frame_end = 0, len(snaps[name]) - 1
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True,
                                 object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
                                 bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                                 bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
        print("exported", name, len(snaps[name]), "frames")
    dst.animation_data.action = None
    for pb in dst.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    # Привязка — покой v6, стоит при повороте объекта +90° по X (как Pelag_AN_DashBind/RollBind): поворот сохраняется.
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Pelag_AN_Abordage2Bind.fbx"), use_selection=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=False, armature_nodetype='NULL')
    dst.animation_data.action = actions.get("Pelag_AN_Abordage2_Pull")
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Pelag_Abordage2_Work.blend"))
    print("saved blend")
