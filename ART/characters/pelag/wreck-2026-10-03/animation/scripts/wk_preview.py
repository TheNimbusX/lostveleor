# Крушение v2: быстрый показ поз прямо с рига сборки (exec внутри wk_author.py при WK_PREVIEW=<папка>).
# Цепь — прямая от кольца рукояти по оси хвата (1,60 м) и брусок головы 0,94 м: только чтобы видеть ось хвата.
PV = os.environ.get("WK_PREVIEW", "")
if PV:
    from mathutils import Vector, Matrix
    os.makedirs(PV, exist_ok=True)
    sc = bpy.context.scene
    TEXP = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v6\Pelag_v6_BaseColor.jpg"
    mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEXP)
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
    rig.mesh.data.materials.clear(); rig.mesh.data.materials.append(mat)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
    gm = bpy.data.materials.new("ground"); gm.diffuse_color = (0.46, 0.34, 0.22, 1); bpy.context.active_object.data.materials.append(gm)
    iron = bpy.data.materials.new("iron"); iron.diffuse_color = (0.12, 0.12, 0.14, 1)

    def box(name):
        me = bpy.data.meshes.new(name)
        v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
        me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
        o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(iron)
        return o

    def place(o, a, b, w):
        v = b - a; y = v.normalized(); x = y.cross(Vector((0, 0, 1)))
        if x.length < 1e-6: x = Vector((1, 0, 0))
        x.normalize(); z = x.cross(y)
        o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * v.length, z * w)).transposed().to_4x4()

    chain_o, head_o, grip_o = box("chain"), box("head"), box("grip")
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
    sc.display.light_direction = (0.25, 0.30, 0.92)
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); w.color = (0.45, 0.5, 0.5); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'
    sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("WK_PREVIEW_RES", "320"))
    a15 = math.radians(15)
    # side — сбоку (герой смотрит вправо), q34 — 3/4 спереди-слева, front — спереди, top — сверху, game — камера игры
    VIEWS = {"side": ((1, 0), 0), "q34": ((0.5, 0.866), 8), "front": ((0, 1), 5), "top": ((0.01, 0.0), 89),
             "game": ((math.cos(a15), -math.sin(a15)), 48), "back": ((-0.5, -0.866), 10)}

    def look(view_dir, pitch_deg, target, scale, dist=30.0):
        pr = math.radians(pitch_deg)
        v = Vector((view_dir[0], view_dir[1], 0)).normalized()
        fwd = Vector((v.x * math.cos(pr), v.y * math.cos(pr), -math.sin(pr)))
        cam.location = target - fwd * dist
        cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = scale

    want = os.environ.get("WK_PREVIEW_VIEWS", "side,q34").split(",")
    allf = os.environ.get("WK_PREVIEW_FRAMES", "key") == "all"
    for name, ss in snaps.items():
        for f, snap in enumerate(ss):
            if not allf and not rows[name][f]["key"]: continue
            rig.restore(snap)
            fl_ = lambda r: Vector((r[1], -r[0], r[2]))
            gL, gR, ax = fl_(rows[name][f]["fistL"]), fl_(rows[name][f]["fistR"]), fl_(rows[name][f]["grip_axis"])
            cd = fl_(rows[name][f]["chain_dir"]).normalized()
            place(grip_o, gL - ax * 0.05, gL + ax * 0.18, 0.035)
            rg = gR
            end = rg + cd * 1.60
            ax = cd
            place(chain_o, rg, end, 0.03)
            place(head_o, end, end + ax * 0.94, 0.16)
            for vn in want:
                vd, pitch = VIEWS[vn]
                look(vd, pitch, Vector((0, -0.5, 1.1)), float(os.environ.get("WK_PREVIEW_SCALE", "5.2")))
                sc.render.filepath = os.path.join(PV, "%s_%s_%03d.png" % (name[len(P):], vn, f))
                bpy.ops.render.render(write_still=True)
    print("preview done")
