# Бросок якоря (копия Абордажа v2): быстрый показ поз прямо на риге сборки (exec внутри at_author.py при AT_PREVIEW=<папка>); цепь — прямая из левой кисти вдоль Dir.
# Кадры: <папка>/<клип>_<вид>_<кадр>.png, виды side (сбоку, герой смотрит вправо) и q34 (3/4 спереди) и game.
PV = os.environ.get("AT_PREVIEW", "")
if PV:
    os.makedirs(PV, exist_ok=True)
    sc = bpy.context.scene
    TEXP = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v6\Pelag_v6_BaseColor.jpg"
    mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEXP)
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
    rig.mesh.data.materials.clear(); rig.mesh.data.materials.append(mat)
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    gm = bpy.data.materials.new("ground"); gm.diffuse_color = (0.46, 0.34, 0.22, 1); bpy.context.active_object.data.materials.append(gm)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
    sc.display.light_direction = (0.25, 0.30, 0.92)
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w"); w.color = (0.45, 0.5, 0.5); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'
    sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("AT_PREVIEW_RES", "300"))
    a15 = math.radians(15)
    VIEWS = {"side": ((1, 0), 0), "q34": ((0.5, 0.866), 8), "game": ((math.cos(a15), -math.sin(a15)), 48),
             "top": ((0.0, -1.0), 89.0), "front": ((0.0, 1.0), 0)}

    def look(view_dir, pitch_deg, target, scale, dist=30.0):
        from mathutils import Vector
        pr = math.radians(pitch_deg)
        v = Vector((view_dir[0], view_dir[1], 0)).normalized()
        fwd = Vector((v.x * math.cos(pr), v.y * math.cos(pr), -math.sin(pr)))
        cam.location = target - fwd * dist
        cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = scale

    from mathutils import Vector
    want = os.environ.get("AT_PREVIEW_VIEWS", "side,q34").split(",")
    cm = bpy.data.materials.new("chain"); cm.diffuse_color = (0.12, 0.12, 0.14, 1)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.012, depth=1.0)
    chain = bpy.context.active_object; chain.data.materials.append(cm)
    SHOW_CHAIN = {P + "Fly", P + "Yank", P + "Haul"}
    PICK = set(x for x in os.environ.get("AT_PREVIEW_FRAMES", "").split(",") if x)     # «Fly:3,Haul:0» — только эти кадры
    for name, ss in snaps.items():
        for f, snap in enumerate(ss):
            if PICK and "%s:%d" % (name[len(P):], f) not in PICK: continue
            rig.restore(snap)
            on = name in SHOW_CHAIN or (name == P + "Throw" and f >= 2)
            chain.hide_render = not on
            if on:
                g = rig.P("LeftHand"); L = 2.5
                chain.location = g + Vector((0, -L / 2, -0.02)); chain.rotation_euler = (math.radians(90), 0, 0); chain.scale = (1, 1, L)
            for vn in want:
                vd, pitch = VIEWS[vn]
                look(vd, pitch, Vector((0, -0.05, 0.8)) if vn != "top" else Vector((0, -0.25, 0.8)), 2.4)
                sc.render.filepath = os.path.join(PV, "%s_%s_%03d.png" % (name[len(P):], vn, f))
                bpy.ops.render.render(write_still=True)
    print("preview done")
