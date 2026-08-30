import bpy
import bmesh
import json
import math
import os
import sys
from mathutils import Vector, Matrix


def arg_after_separator(default):
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1:]
        if args:
            return os.path.abspath(args[0])
    return os.path.abspath(default)


OUT_DIR = arg_after_separator("./Pelag_Boarder_01_Production")
RENDER_DIR = os.path.join(OUT_DIR, "deformation_tests")
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.armatures,
                       bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


clear_scene()
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1024
scene.render.resolution_y = 1024
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.film_transparent = False
scene.world.color = (0.035, 0.04, 0.05)
scene.render.fps = 30
scene.render.fps_base = 1.0
scene.frame_start = 0
scene.frame_end = 0
scene.frame_set(0)


def collection(name):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    return c


COL_RIG = collection("RIG")
COL_BODY = collection("BODY_UNDER_CLOTHING")
COL_CLOTH = collection("CLOTHING")
COL_PROPS = collection("PROPS")
COL_PREVIEW = collection("PREVIEW_ONLY")


def move_to_collection(obj, target):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    target.objects.link(obj)


PALETTE = {
    "Skin": (0.580, 0.258, 0.078, 1.0),
    "SkinHighlight": (0.890, 0.520, 0.250, 1.0),
    "Hair": (0.023, 0.012, 0.008, 1.0),
    "Cream": (0.920, 0.790, 0.585, 1.0),
    "CreamLight": (0.970, 0.880, 0.745, 1.0),
    # #F05B4F in sRGB, stored approximately linear for Blender materials.
    "Coral": (0.871, 0.105, 0.078, 1.0),
    "CoralShadow": (0.360, 0.030, 0.025, 1.0),
    # Pelag turquoise is secondary only; never paired with yellow/gold.
    "Turquoise": (0.007, 0.479, 0.423, 1.0),
    "Leather": (0.220, 0.105, 0.060, 1.0),
    "LeatherLight": (0.440, 0.225, 0.125, 1.0),
    "Rope": (0.360, 0.245, 0.150, 1.0),
    "Steel": (0.420, 0.500, 0.530, 1.0),
    "SteelDark": (0.075, 0.095, 0.105, 1.0),
    "Outline": (0.008, 0.006, 0.008, 1.0),
}


def material(name, rgba, metallic=0.0, roughness=0.72):
    mat = bpy.data.materials.new("MAT_" + name)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    mat["cel_base_hex"] = "#{:02X}{:02X}{:02X}".format(
        int(rgba[0] * 255), int(rgba[1] * 255), int(rgba[2] * 255))
    mat["toon_levels"] = 3
    return mat


MATS = {name: material(name, color, 0.78 if "Steel" in name else 0.0,
                       0.28 if "Steel" in name else 0.78)
        for name, color in PALETTE.items()}


def vertex_color_material(name, metallic=0.0, roughness=0.72):
    """One shared toon-ready material; per-part palette is stored in AtlasColor."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    color = nodes.new("ShaderNodeVertexColor")
    color.layer_name = "AtlasColor"
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    links.new(color.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    mat["palette_storage"] = "FBX vertex color AtlasColor"
    mat["toon_levels"] = 3
    return mat


MAT_BODY_ATLAS = vertex_color_material("MAT_Pelag_BodyAtlas", 0.0, 0.78)
MAT_CLOTH_ATLAS = vertex_color_material("MAT_Pelag_ClothAtlas", 0.0, 0.76)
MAT_EQUIPMENT_ATLAS = vertex_color_material("MAT_Pelag_EquipmentAtlas", 0.58, 0.38)


def active(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish_mesh(obj, mat, target, smooth=True):
    active(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if smooth:
        for p in obj.data.polygons:
            p.use_smooth = True
    if mat:
        obj.data.materials.append(mat)
    move_to_collection(obj, target)
    if not obj.data.uv_layers:
        active(obj)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
        bpy.ops.object.mode_set(mode="OBJECT")
    obj["production_mesh"] = True
    return obj


def uv_sphere(name, loc, scale, mat, target, segments=32, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    return finish_mesh(obj, mat, target)


def cone_between(name, p1, p2, r1, r2, mat, target, vertices=24):
    a, b = Vector(p1), Vector(p2)
    vec = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2,
                                    depth=vec.length, end_fill_type="NGON",
                                    location=(a + b) * 0.5)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = vec.to_track_quat("Z", "Y")
    return finish_mesh(obj, mat, target)


def cylinder_between(name, p1, p2, radius, mat, target, vertices=24):
    return cone_between(name, p1, p2, radius, radius, mat, target, vertices)


def box(name, loc, scale, mat, target, bevel=0.0, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    finish_mesh(obj, mat, target, smooth=False)
    if bevel > 0:
        mod = obj.modifiers.new("Soft_Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
        active(obj)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        for p in obj.data.polygons:
            p.use_smooth = True
    return obj


def stylized_ellipsoid(name, loc, scale, mat, target, segments=32, rings=20,
                       chin_taper=0.0, face_flatten=0.0, top_width=1.0):
    """Soft authored volume with controllable chin and face planes."""
    obj = uv_sphere(name, loc, scale, mat, target, segments, rings)
    for vert in obj.data.vertices:
        p = vert.co
        z01 = max(-1.0, min(1.0, p.z / max(scale[2], 1e-6)))
        if z01 < -0.05:
            taper = 1.0 - chin_taper * ((-z01 - 0.05) / 0.95) ** 1.35
            p.x *= taper
            p.y *= 0.94 + 0.06 * taper
        elif z01 > 0.35:
            p.x *= 1.0 + (top_width - 1.0) * ((z01 - 0.35) / 0.65)
        if face_flatten > 0.0 and p.y < 0.0:
            front = -scale[1] * (1.0 - face_flatten)
            p.y = max(p.y, front - (front - p.y) * 0.35)
    return obj


def rounded_panel(name, outline_xz, y_center, depth, mat, target, bevel=0.012):
    """Extruded garment panel from a front-view outline, with real thickness."""
    verts = []
    half = depth * 0.5
    for y in (y_center - half, y_center + half):
        verts.extend([(x, y, z) for x, z in outline_xz])
    n = len(outline_xz)
    faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    if bevel > 0:
        mod = obj.modifiers.new("SoftGarmentEdges", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        active(obj)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish_mesh(obj, mat, target, smooth=False)


def hair_wedge(name, root, tip, width, depth, mat, target, roll=0.0):
    """Large tapered hair clump with an authored diamond-like cross-section."""
    root_v = Vector(root)
    tip_v = Vector(tip)
    axis = (tip_v - root_v).normalized()
    side = axis.cross(Vector((0, 1, 0)))
    if side.length < 0.001:
        side = Vector((1, 0, 0))
    side.normalize()
    normal = axis.cross(side).normalized()
    q = Matrix.Rotation(roll, 4, axis)
    side = q @ side
    normal = q @ normal
    neck = root_v + (tip_v - root_v) * 0.64
    verts = [
        root_v + side * width + normal * depth,
        root_v - side * width + normal * depth,
        root_v - side * width - normal * depth,
        root_v + side * width - normal * depth,
        neck + side * width * 0.48 + normal * depth * 0.72,
        neck - side * width * 0.48 + normal * depth * 0.72,
        neck - side * width * 0.48 - normal * depth * 0.72,
        neck + side * width * 0.48 - normal * depth * 0.72,
        tip_v,
    ]
    faces = [
        (0, 1, 2, 3), (0, 4, 5, 1), (1, 5, 6, 2),
        (2, 6, 7, 3), (3, 7, 4, 0),
        (4, 8, 5), (5, 8, 6), (6, 8, 7), (7, 8, 4),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    bevel = obj.modifiers.new("HairEdgeSoftness", "BEVEL")
    bevel.width = 0.008
    bevel.segments = 2
    active(obj)
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return finish_mesh(obj, mat, target, smooth=False)


def elliptical_loft(name, rings, mat, target, segments=32, phase=-math.pi/2):
    """Closed organic volume described by (z, half_width, half_depth) rings."""
    verts = []
    for z, rx, ry in rings:
        for i in range(segments):
            a = phase + math.tau * i / segments
            verts.append((rx * math.cos(a), ry * math.sin(a), z))
    faces = []
    ring_count = len(rings)
    for r in range(ring_count - 1):
        base = r * segments
        nxt = (r + 1) * segments
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((base + i, base + j, nxt + j, nxt + i))
    faces.append(tuple(range(segments))[::-1])
    top = tuple((ring_count - 1) * segments + i for i in range(segments))
    faces.append(top)
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    bevel = obj.modifiers.new("LoftSoftness", "BEVEL")
    bevel.width = .010
    bevel.segments = 2
    active(obj)
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return finish_mesh(obj, mat, target, smooth=True)


def skinned_tube(name, rings, mat, target, segments=16):
    """One connected limb surface.

    rings: [(position, vertical_radius, depth_radius, {bone: weight}), ...]
    The path is expected to lie mostly in XZ; Y is used as the stable depth axis.
    """
    verts = []
    for i, (co, rv, rd, _weights) in enumerate(rings):
        p = Vector(co)
        if i == 0:
            tangent = Vector(rings[1][0]) - p
        elif i == len(rings) - 1:
            tangent = p - Vector(rings[i - 1][0])
        else:
            tangent = Vector(rings[i + 1][0]) - Vector(rings[i - 1][0])
        tangent.normalize()
        depth_axis = Vector((0, 1, 0))
        vertical_axis = depth_axis.cross(tangent).normalized()
        for s in range(segments):
            a = math.tau * s / segments
            verts.append(tuple(p + vertical_axis * (math.cos(a) * rv) +
                               depth_axis * (math.sin(a) * rd)))
    faces = []
    for r in range(len(rings) - 1):
        a0 = r * segments
        b0 = (r + 1) * segments
        for s in range(segments):
            n = (s + 1) % segments
            faces.append((a0 + s, a0 + n, b0 + n, b0 + s))
    faces.append(tuple(range(segments))[::-1])
    last = (len(rings) - 1) * segments
    faces.append(tuple(last + s for s in range(segments)))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    group_names = sorted({bone for ring in rings for bone in ring[3]})
    groups = {bone: obj.vertex_groups.new(name=bone) for bone in group_names}
    for r, (_co, _rv, _rd, weights) in enumerate(rings):
        indices = list(range(r * segments, (r + 1) * segments))
        total = sum(weights.values()) or 1.0
        for bone, weight in weights.items():
            groups[bone].add(indices, weight / total, "REPLACE")
    armature_modifier(obj)
    return finish_mesh(obj, mat, target, smooth=True)


def torus(name, loc, major, minor, mat, target, scale=(1, 1, 1), rotation=(0, 0, 0), major_segments=24, minor_segments=8):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor,
                                    major_segments=major_segments, minor_segments=minor_segments,
                                    location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    return finish_mesh(obj, mat, target)


def curve_tube(name, points, radius, mat, target, resolution=2):
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = resolution
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for bp, co in zip(spline.bezier_points, points):
        bp.co = co
        bp.handle_left_type = "AUTO"
        bp.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, curve)
    target.objects.link(obj)
    active(obj)
    bpy.ops.object.convert(target="MESH")
    return finish_mesh(obj, mat, target)


def join_objects(objects, name, target):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, target)
    return obj


def build_armature():
    data = bpy.data.armatures.new("Pelag_Boarder_01_Armature")
    arm = bpy.data.objects.new("Pelag_Boarder_01_Rig", data)
    COL_RIG.objects.link(arm)
    active(arm)
    bpy.ops.object.mode_set(mode="EDIT")

    bones = {}

    def add(name, head, tail, parent=None, connect=False, deform=True):
        b = data.edit_bones.new(name)
        b.head = head
        b.tail = tail
        b.use_deform = deform
        if parent:
            b.parent = bones[parent]
            b.use_connect = connect
        bones[name] = b
        return b

    add("Root", (0, 0, 0), (0, 0, 0.12), deform=False)
    add("Hips", (0, 0, 0.98), (0, 0, 1.08), "Root")
    add("Spine", (0, 0, 1.02), (0, 0, 1.20), "Hips")
    add("Chest", (0, 0, 1.20), (0, 0, 1.39), "Spine", True)
    add("UpperChest", (0, 0, 1.39), (0, 0, 1.49), "Chest", True)
    add("Neck", (0, 0, 1.49), (0, 0, 1.59), "UpperChest", True)
    add("Head", (0, 0, 1.59), (0, 0, 1.78), "Neck", True)

    side_data = {
        "Left": (1, 0.14),
        "Right": (-1, -0.14),
    }
    for side, (sgn, hip_x) in side_data.items():
        add(side + "UpperLeg", (hip_x, 0, 0.98), (hip_x, 0, 0.53), "Hips")
        add(side + "LowerLeg", (hip_x, 0, 0.53), (hip_x, 0, 0.10), side + "UpperLeg", True)
        add(side + "Foot", (hip_x, 0, 0.10), (hip_x, -0.17, 0.055), side + "LowerLeg", True)
        add(side + "Toes", (hip_x, -0.17, 0.055), (hip_x, -0.29, 0.045), side + "Foot", True)

        shoulder_inner = (sgn * 0.02, 0, 1.46)
        shoulder_outer = (sgn * 0.19, 0, 1.46)
        elbow = (sgn * 0.47, 0, 1.25)
        wrist = (sgn * 0.70, 0, 1.08)
        hand = (sgn * 0.82, 0, 1.00)
        add(side + "Shoulder", shoulder_inner, shoulder_outer, "UpperChest")
        add(side + "UpperArm", shoulder_outer, elbow, side + "Shoulder", True)
        add(side + "LowerArm", elbow, wrist, side + "UpperArm", True)
        add(side + "Hand", wrist, hand, side + "LowerArm", True)

    # Permanent sockets.
    add("Weapon_R", (-0.70, -0.015, 1.08), (-0.77, -0.02, 1.01), "RightHand", deform=False)
    add("Hook_L", (0.70, -0.015, 1.08), (0.77, -0.02, 1.01), "LeftHand", deform=False)
    add("HookBelt_R", (-0.23, 0.00, 0.95), (-0.23, 0.00, 0.84), "Hips", deform=False)
    add("SaberSheath_L", (0.23, 0.02, 0.94), (0.29, 0.02, 0.78), "Hips", deform=False)

    chain_points = [
        (-0.25, 0.02, 0.91), (-0.31, 0.02, 0.84), (-0.32, 0.02, 0.76),
        (-0.29, 0.02, 0.68), (-0.22, 0.02, 0.63), (-0.14, 0.02, 0.66),
        (-0.10, 0.02, 0.73), (-0.12, 0.02, 0.81), (-0.18, 0.02, 0.86),
    ]
    parent = "HookBelt_R"
    for i in range(8):
        name = f"Chain_{i+1:02d}"
        add(name, chain_points[i], chain_points[i + 1], parent, False, True)
        parent = name

    sash_specs = {
        "Sash_L": [(0.16, 0.03, 0.93), (0.19, 0.03, 0.80), (0.21, 0.03, 0.67), (0.19, 0.03, 0.55)],
        "Sash_R": [(-0.13, 0.04, 0.93), (-0.12, 0.04, 0.81), (-0.10, 0.04, 0.69), (-0.08, 0.04, 0.58)],
    }
    for prefix, pts in sash_specs.items():
        parent = "Hips"
        for i in range(3):
            name = f"{prefix}_{i+1:02d}"
            add(name, pts[i], pts[i+1], parent, False, True)
            parent = name

    add("HeadbandTail_01", (0.09, 0.12, 1.70), (0.12, 0.16, 1.56), "Head", False, True)
    add("HeadbandTail_02", (-0.07, 0.12, 1.70), (-0.10, 0.17, 1.55), "Head", False, True)

    # Animator-facing non-deform control rig. Deform bones remain Unity Humanoid compatible.
    add("CTRL_Master", (0, 0.22, 0.02), (0, 0.22, 0.22), None, False, False)
    add("CTRL_Hips", (0, 0.20, 0.98), (0, 0.20, 1.13), "CTRL_Master", False, False)
    add("CTRL_Chest", (0, 0.20, 1.29), (0, 0.20, 1.47), "CTRL_Master", False, False)
    for side, sgn in (("Left", 1), ("Right", -1)):
        add("IK_Hand_" + side[0], (sgn*.70, -.02, 1.08), (sgn*.82, -.02, 1.00), "CTRL_Master", False, False)
        add("Pole_Elbow_" + side[0], (sgn*.47, -.48, 1.25), (sgn*.47, -.48, 1.38), "CTRL_Master", False, False)
        add("IK_Foot_" + side[0], (sgn*.14, 0, .10), (sgn*.14, -.24, .07), "CTRL_Master", False, False)
        add("Pole_Knee_" + side[0], (sgn*.14, -.48, .53), (sgn*.14, -.48, .66), "CTRL_Master", False, False)
        add("CTRL_FootRoll_" + side[0], (sgn*.14, -.17, .055), (sgn*.14, -.29, .045), "IK_Foot_" + side[0], False, False)
    add("CTRL_Chain", (-.24, -.24, .80), (-.24, -.24, .98), "CTRL_Master", False, False)

    bpy.ops.object.mode_set(mode="OBJECT")
    arm.show_in_front = True
    arm.data.display_type = "BBONE"
    arm["unity_humanoid_candidate"] = True
    arm["forward_axis_after_fbx"] = "+Z"
    arm["root_motion"] = False
    return arm


ARM = build_armature()


def add_control_constraints():
    """IK/FK switches default to FK=0 so the neutral A-pose is export-safe."""
    def driver(constraint, control_name):
        control = ARM.pose.bones[control_name]
        control["ik_fk"] = 0.0
        ui = control.id_properties_ui("ik_fk")
        ui.update(min=0.0, max=1.0, description="0 FK / 1 IK")
        fcurve = constraint.driver_add("influence")
        drv = fcurve.driver
        drv.type = "SCRIPTED"
        var = drv.variables.new()
        var.name = "switch"
        var.type = "SINGLE_PROP"
        var.targets[0].id = ARM
        var.targets[0].data_path = 'pose.bones["%s"]["ik_fk"]' % control_name
        drv.expression = "switch"

    for side, code in (("Left", "L"), ("Right", "R")):
        arm_ik = ARM.pose.bones[side + "LowerArm"].constraints.new("IK")
        arm_ik.name = "IKFK_Arm_" + code
        arm_ik.target = ARM
        arm_ik.subtarget = "IK_Hand_" + code
        arm_ik.pole_target = ARM
        arm_ik.pole_subtarget = "Pole_Elbow_" + code
        arm_ik.chain_count = 2
        arm_ik.influence = 0.0
        driver(arm_ik, "IK_Hand_" + code)

        leg_ik = ARM.pose.bones[side + "LowerLeg"].constraints.new("IK")
        leg_ik.name = "IKFK_Leg_" + code
        leg_ik.target = ARM
        leg_ik.subtarget = "IK_Foot_" + code
        leg_ik.pole_target = ARM
        leg_ik.pole_subtarget = "Pole_Knee_" + code
        leg_ik.chain_count = 2
        leg_ik.influence = 0.0
        driver(leg_ik, "IK_Foot_" + code)

        roll = ARM.pose.bones[side + "Foot"].constraints.new("COPY_ROTATION")
        roll.name = "FootRoll_" + code
        roll.target = ARM
        roll.subtarget = "CTRL_FootRoll_" + code
        roll.target_space = "POSE"
        roll.owner_space = "POSE"
        roll.influence = 0.0
        driver(roll, "IK_Foot_" + code)

    ARM.pose.bones["CTRL_Chain"]["chain_follow"] = 1.0
    ARM["control_rig_version"] = "1.0"
    ARM["control_rig_notes"] = "Animator controls are non-deform; Unity uses deform hierarchy only"


add_control_constraints()


def armature_modifier(obj):
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = ARM
    mod.use_vertex_groups = True
    obj.parent = ARM
    return mod


def rigid_skin(obj, bone_name):
    vg = obj.vertex_groups.new(name=bone_name)
    vg.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    armature_modifier(obj)
    obj["skin_mode"] = "rigid_to_" + bone_name
    return obj


def dual_skin(obj, bone_a, bone_b, blend=0.5):
    a = obj.vertex_groups.new(name=bone_a)
    b = obj.vertex_groups.new(name=bone_b)
    ids = range(len(obj.data.vertices))
    a.add(ids, blend, "REPLACE")
    b.add(ids, 1.0 - blend, "REPLACE")
    armature_modifier(obj)
    obj["skin_mode"] = f"blend_{bone_a}_{bone_b}"
    return obj


def vertical_skin(obj, zones):
    groups = {name: obj.vertex_groups.new(name=name) for name, _, _ in zones}
    for v in obj.data.vertices:
        z = (obj.matrix_world @ v.co).z
        weights = []
        for name, center, radius in zones:
            w = max(0.0, 1.0 - abs(z - center) / radius)
            if w > 0:
                weights.append((name, w))
        if not weights:
            name = min(zones, key=lambda t: abs(z - t[1]))[0]
            weights = [(name, 1.0)]
        total = sum(w for _, w in weights)
        for name, w in weights:
            groups[name].add([v.index], w / total, "REPLACE")
    armature_modifier(obj)
    obj["skin_mode"] = "vertical_blend"
    return obj


# Complete body under clothing.
body = []
body.append(vertical_skin(elliptical_loft("CHR_Pelag_Body_Torso",
                          [(1.00,.205,.135),(1.10,.220,.145),(1.26,.235,.150),
                           (1.40,.255,.145),(1.49,.205,.125)],
                          MATS["Skin"], COL_BODY, 36),
                          [("Hips", 1.03, .20), ("Spine", 1.18, .22), ("Chest", 1.34, .22), ("UpperChest", 1.46, .16)]))
body.append(rigid_skin(uv_sphere("CHR_Pelag_Body_Pelvis", (0, 0, 0.98), (0.215, 0.15, 0.17), MATS["Skin"], COL_BODY), "Hips"))
body.append(rigid_skin(cylinder_between("CHR_Pelag_Body_Neck", (0, 0, 1.47), (0, 0, 1.59), 0.075, MATS["Skin"], COL_BODY), "Neck"))
body.append(rigid_skin(stylized_ellipsoid("CHR_Pelag_Body_Head", (0, -0.002, 1.685),
                                          (0.151, 0.136, 0.175), MATS["Skin"], COL_BODY,
                                          40, 24, chin_taper=.24, face_flatten=.10,
                                          top_width=1.04), "Head"))
body.append(rigid_skin(uv_sphere("CHR_Pelag_Ear_L", (0.145, -0.005, 1.68), (0.026, 0.018, 0.045), MATS["Skin"], COL_BODY, 20, 10), "Head"))
body.append(rigid_skin(uv_sphere("CHR_Pelag_Ear_R", (-0.145, -0.005, 1.68), (0.026, 0.018, 0.045), MATS["Skin"], COL_BODY, 20, 10), "Head"))

for side, sgn in (("Left", 1), ("Right", -1)):
    shoulder = (sgn * 0.19, 0, 1.46)
    elbow = (sgn * 0.47, 0, 1.25)
    wrist = (sgn * 0.70, 0, 1.08)
    hand = (sgn * 0.80, -0.005, 1.00)
    arm = skinned_tube(
        f"CHR_Pelag_{side}_ArmHand",
        [
            (shoulder, .086,.082,{side+"UpperArm":1.0}),
            ((sgn*.33,0,1.36), .096,.086,{side+"UpperArm":1.0}),
            (elbow, .074,.070,{side+"UpperArm":.45,side+"LowerArm":.55}),
            ((sgn*.59,0,1.16), .076,.064,{side+"LowerArm":1.0}),
            (wrist, .053,.047,{side+"LowerArm":.7,side+"Hand":.3}),
            ((sgn*.765,-.005,1.025), .072,.052,{side+"Hand":1.0}),
            ((sgn*.815,-.008,.985), .052,.043,{side+"Hand":1.0}),
        ], MATS["Skin"], COL_BODY, 18)
    body.append(arm)
    # Five readable low-poly digits; no finger bones in the LOD1 Humanoid template.
    finger_z = (1.025, 1.010, .995, .980)
    for fi, z in enumerate(finger_z, 1):
        start = (sgn * .795, -.025 + fi*.012, z)
        end = (sgn * (.855 - .005 * abs(fi-2)), -.028 + fi*.012, z-.045)
        digit = cone_between(f"CHR_Pelag_{side}_Finger_{fi:02d}", start, end, .014, .008, MATS["Skin"], COL_BODY, 10)
        grip_group = digit.vertex_groups.new(name="GripDigits_" + side[0])
        grip_group.add(range(len(digit.data.vertices)), 1.0, "REPLACE")
        rigid_skin(digit, side + "Hand")
    thumb_start = (sgn*.775, -.050, 1.015)
    thumb_end = (sgn*.825, -.070, .985)
    thumb = cone_between(f"CHR_Pelag_{side}_Thumb", thumb_start, thumb_end, .016, .009, MATS["Skin"], COL_BODY, 10)
    grip_group = thumb.vertex_groups.new(name="GripDigits_" + side[0])
    grip_group.add(range(len(thumb.data.vertices)), 1.0, "REPLACE")
    rigid_skin(thumb, side + "Hand")

    hip = (sgn * 0.14, 0, 0.98)
    knee = (sgn * 0.14, 0, 0.53)
    ankle = (sgn * 0.14, 0, 0.10)
    body.append(rigid_skin(cone_between(f"CHR_Pelag_{side}_UpperLeg", hip, knee, .115, .092, MATS["Skin"], COL_BODY), side + "UpperLeg"))
    body.append(dual_skin(uv_sphere(f"CHR_Pelag_{side}_Knee", knee, (.096, .09, .10), MATS["Skin"], COL_BODY, 24, 12), side + "UpperLeg", side + "LowerLeg"))
    body.append(rigid_skin(cone_between(f"CHR_Pelag_{side}_LowerLeg", knee, ankle, .088, .062, MATS["Skin"], COL_BODY), side + "LowerLeg"))
    body.append(rigid_skin(uv_sphere(f"CHR_Pelag_{side}_Foot", (sgn * .14, -.10, .065), (.105, .19, .065), MATS["Skin"], COL_BODY, 28, 14), side + "Foot"))

# Hair: one soft cap plus eight large directional clumps. The clumps are the
# character's permanent gameplay silhouette, so they are authored as wedges,
# not a pile of generic spheres.
hair_cap = stylized_ellipsoid("CHR_Pelag_HairCap", (0, .010, 1.785),
                             (.158, .141, .125), MATS["Hair"], COL_BODY,
                             36, 20, chin_taper=.02, face_flatten=.0, top_width=1.08)
rigid_skin(hair_cap, "Head")
hair_specs = [
    ((-.115,-.005,1.805), (-.245,-.020,1.850), .060,.045,-.15),
    ((-.070,-.010,1.845), (-.145,-.030,1.910), .058,.045, .10),
    (( .000,-.010,1.865), ( .030,-.025,1.925), .065,.048,-.05),
    (( .075,-.006,1.845), ( .170,-.020,1.905), .060,.046,-.12),
    (( .125, .000,1.805), ( .255, .000,1.860), .058,.045, .10),
    (( .135, .045,1.760), ( .220, .070,1.700), .055,.050,-.10),
    ((-.135, .050,1.760), (-.215, .075,1.690), .055,.050, .10),
    (( .000, .115,1.775), ( .015, .180,1.690), .070,.050, .05),
]
for i, (root, tip, width, depth, roll) in enumerate(hair_specs, 1):
    rigid_skin(hair_wedge(f"CHR_Pelag_HairClump_{i:02d}", root, tip, width, depth,
                          MATS["Hair"], COL_BODY, roll), "Head")

# Minimal game-readable face geometry.
for side, x in (("L", .052), ("R", -.052)):
    eye = uv_sphere(f"CHR_Pelag_EyeWhite_{side}", (x,-.134,1.705), (.038,.0045,.026), MATS["CreamLight"], COL_BODY, 20, 10)
    rigid_skin(eye, "Head")
    iris = uv_sphere(f"CHR_Pelag_Iris_{side}", (x,-.139,1.701), (.013,.0035,.016), MATS["Hair"], COL_BODY, 16, 8)
    rigid_skin(iris, "Head")
    brow = box(f"CHR_Pelag_Brow_{side}", (x,-.142,1.747), (.036,.006,.006), MATS["Hair"], COL_BODY, .004,
               rotation=(0,math.radians(-8 if x>0 else 8),math.radians(-5 if x>0 else 5)))
    rigid_skin(brow, "Head")
mouth = box("CHR_Pelag_Mouth", (0,-.139,1.625), (.036,.006,.006), MATS["CoralShadow"], COL_BODY, .004)
rigid_skin(mouth, "Head")
nose = stylized_ellipsoid("CHR_Pelag_Nose", (0,-.137,1.666), (.016,.012,.025),
                          MATS["SkinHighlight"], COL_BODY, 16, 8,
                          chin_taper=.10, face_flatten=.0, top_width=1.0)
rigid_skin(nose, "Head")

# Stylized clothing.
tunic = elliptical_loft("CHR_Pelag_Tunic_Torso",
                        [(1.01,.245,.165),(1.12,.270,.176),(1.28,.285,.178),
                         (1.42,.300,.168),(1.50,.235,.145)],
                        MATS["Cream"], COL_CLOTH, 40)
vertical_skin(tunic, [("Spine", 1.16, .24), ("Chest", 1.34, .24), ("UpperChest", 1.46, .18)])

for side, sgn in (("Left", 1), ("Right", -1)):
    shoulder = (sgn * .20, 0, 1.45)
    sleeve_end = (sgn * .34, 0, 1.35)
    rigid_skin(cone_between(f"CHR_Pelag_{side}_ShortSleeve", shoulder, sleeve_end, .105, .10, MATS["Cream"], COL_CLOTH, 28), side + "UpperArm")
    trim = torus(f"CHR_Pelag_{side}_SleeveTrim", sleeve_end, .102, .012, MATS["Turquoise"], COL_CLOTH,
                 scale=(1, .68, 1), rotation=(0, math.radians(55) * sgn, 0), major_segments=20, minor_segments=6)
    rigid_skin(trim, side + "UpperArm")

    # Bracers and sandals.
    bracer = cylinder_between(f"CHR_Pelag_{side}_Bracer", (sgn*.59,0,1.16), (sgn*.68,0,1.09), .072, MATS["Leather"], COL_CLOTH, 20)
    rigid_skin(bracer, side + "LowerArm")
    sandal = stylized_ellipsoid(f"CHR_Pelag_{side}_SandalSole", (sgn*.14,-.105,.035),
                                (.120,.205,.035), MATS["Leather"], COL_CLOTH,
                                28, 10, chin_taper=.0, face_flatten=.0, top_width=1.0)
    rigid_skin(sandal, side + "Foot")
    for si, pts in enumerate((
        [(sgn*.235,-.195,.080),(sgn*.140,-.120,.105),(sgn*.045,-.195,.080)],
        [(sgn*.225,-.105,.082),(sgn*.140,-.025,.110),(sgn*.055,-.105,.082)],
    ),1):
        strap_sandal = curve_tube(f"CHR_Pelag_{side}_SandalStrap_{si}", pts,
                                  .018, MATS["Leather"], COL_CLOTH, resolution=1)
        rigid_skin(strap_sandal, side + "Foot")

# Split skirt panels with the concept's flared, stepped hem. Each panel has
# thickness and a separate thigh assignment for wide steps.
panel_specs = [
    ("Front_L", [( .015,1.00),(.285,1.00),(.325,.49),(.235,.49),(.235,.42),(.015,.42)], -.145, "LeftUpperLeg"),
    ("Front_R", [(-.285,1.00),(-.015,1.00),(-.015,.42),(-.235,.42),(-.235,.49),(-.325,.49)], -.145, "RightUpperLeg"),
    ("Back_L",  [( .010,.99),(.275,.99),(.310,.47),(.215,.47),(.215,.40),(.010,.40)],  .135, "LeftUpperLeg"),
    ("Back_R",  [(-.275,.99),(-.010,.99),(-.010,.40),(-.215,.40),(-.215,.47),(-.310,.47)],  .135, "RightUpperLeg"),
]
for name, outline, y, thigh in panel_specs:
    p = rounded_panel("CHR_Pelag_Skirt_" + name, outline, y, .045,
                      MATS["Cream"], COL_CLOTH, .014)
    dual_skin(p, "Hips", thigh, .45)

# Coral sash built as three broad overlapping wraps rather than a single tube.
for i, (zb, zt, rx, ry, shade) in enumerate((
        (1.000,1.055,.270,.178,"Coral"),
        (.962,1.017,.278,.181,"Coral"),
        (.925,.978,.270,.176,"CoralShadow")), 1):
    sash = elliptical_loft(f"CHR_Pelag_Coral_SashWrap_{i:02d}",
                           [(zb,rx,ry),(zt,rx,ry)], MATS[shade], COL_CLOTH, 40)
    rigid_skin(sash, "Hips")

strap = rounded_panel("CHR_Pelag_Coral_DiagonalBand",
                      [(-.225,1.505),(-.145,1.505),(.230,1.035),(.145,1.015)],
                      -.181, .038, MATS["Coral"], COL_CLOTH, .010)
vertical_skin(strap, [("UpperChest",1.44,.22),("Chest",1.30,.23),("Spine",1.13,.20)])

# Permanent V-neck edge, sized to survive at 80-120 px.
for suffix, pts in (
    ("L", [(-.205,-.170,1.485),(-.115,-.180,1.395),(0,-.183,1.295)]),
    ("R", [( .205,-.170,1.485),( .115,-.180,1.395),(0,-.183,1.295)]),
):
    collar = curve_tube(f"CHR_Pelag_Collar_{suffix}", pts, .018,
                        MATS["Coral"], COL_CLOTH, resolution=2)
    vertical_skin(collar, [("UpperChest",1.45,.20),("Chest",1.33,.20),("Spine",1.20,.18)])

apron_specs = [
    ("L", [(-.205,.99),(-.015,.99),(-.005,.455),(-.205,.455)], "RightUpperLeg"),
    ("R", [( .015,.99),( .220,.99),( .245,.520),( .165,.520),( .165,.455),( .015,.455)], "LeftUpperLeg"),
]
for suffix, outline, thigh in apron_specs:
    p = rounded_panel(f"CHR_Pelag_Coral_Apron_{suffix}", outline, -.176, .036,
                      MATS["Coral"], COL_CLOTH, .010)
    dual_skin(p, "Hips", thigh, .55)

# Headband and secondary tails.
headband = torus("CHR_Pelag_Headband", (0,0,1.758), .153, .018, MATS["Coral"], COL_CLOTH,
                 scale=(1,.86,1), major_segments=32, minor_segments=8)
rigid_skin(headband, "Head")
tail_l = box("CHR_Pelag_HeadbandTail_L", (.095,.145,1.60), (.032,.012,.10), MATS["Coral"], COL_CLOTH, .008,
             rotation=(math.radians(-10),0,math.radians(-10)))
rigid_skin(tail_l, "HeadbandTail_01")
tail_r = box("CHR_Pelag_HeadbandTail_R", (-.085,.15,1.59), (.032,.012,.105), MATS["CoralShadow"], COL_CLOTH, .008,
             rotation=(math.radians(8),0,math.radians(9)))
rigid_skin(tail_r, "HeadbandTail_02")

# Sash tails with dedicated bones.
for prefix, x, y, mat_name in (("Sash_L", .18, -.04, "Coral"), ("Sash_R", -.10, .04, "CoralShadow")):
    for i, z in enumerate((.82,.69,.57),1):
        s = box(f"CHR_Pelag_{prefix}_{i:02d}", (x,y,z), (.045,.018,.075), MATS[mat_name], COL_CLOTH, .008)
        rigid_skin(s, f"{prefix}_{i:02d}")

# Saber and scabbard.
saber_parts = []
saber_parts.append(box("Saber_Blade", (.285,.015,.66), (.022,.014,.37), MATS["Steel"], COL_PROPS, .006,
                       rotation=(0,math.radians(-8),math.radians(-5))))
saber_parts.append(box("Saber_Grip", (.25,.015,1.045), (.035,.032,.09), MATS["Leather"], COL_PROPS, .012,
                       rotation=(0,math.radians(-8),math.radians(-5))))
saber_parts.append(box("Saber_Guard", (.255,.015,.965), (.10,.025,.018), MATS["SteelDark"], COL_PROPS, .009,
                       rotation=(0,math.radians(-8),math.radians(-5))))
saber = join_objects(saber_parts, "PRP_Saber_0p92m", COL_PROPS)
saber["overall_length_m"] = 0.92

scabbard = box("PRP_SaberScabbard", (.305,.045,.67), (.044,.038,.385), MATS["Leather"], COL_PROPS, .018,
               rotation=(0,math.radians(-8),math.radians(-5)))
scabbard["socket"] = "SaberSheath_L"

# Hook with handle and curved head.
hook_parts = []
hook_parts.append(cylinder_between("Hook_Handle", (-.265,.02,.56), (-.265,.02,.44), .024, MATS["Leather"], COL_PROPS, 16))
hook_parts.append(curve_tube("Hook_Head", [(-.265,.02,.44),(-.30,.02,.39),(-.35,.02,.40),(-.37,.02,.45),(-.34,.02,.49)],
                             .022, MATS["Steel"], COL_PROPS))
hook = join_objects(hook_parts, "PRP_Hook_0p22m", COL_PROPS)
hook["head_size_m"] = 0.22
hook["stored_socket"] = "HookBelt_R"

# 25 physical links approximate 2.20 m, folded into a 0.65 m belt silhouette.
links = []
for i in range(25):
    t = i / 24.0
    theta = t * math.pi * 4.0
    x = -.235 + .105 * math.cos(theta)
    y = .035 + .016 * (i % 2)
    z = .72 + .255 * math.sin(theta)
    rot = (math.radians(90 if i % 2 else 0), 0, theta)
    link = torus(f"ChainLink_{i+1:02d}", (x,y,z), .038, .0065, MATS["SteelDark"], COL_PROPS,
                 rotation=rot, major_segments=10, minor_segments=6)
    bone_index = min(7, int(i * 8 / 25)) + 1
    rigid_skin(link, f"Chain_{bone_index:02d}")
    links.append(link)
chain = join_objects(links, "PRP_Chain_2p20m_Folded0p65m", COL_PROPS)
chain["physical_length_m"] = 2.20
chain["stored_silhouette_m"] = 0.65
chain["control_bones"] = 8

# Parent rigid props to sockets while preserving world matrices.
def set_origin_world(obj, location):
    scene.cursor.location = location
    active(obj)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")


set_origin_world(saber, (.25, .015, 1.045))
set_origin_world(scabbard, (.305, .045, 1.055))
set_origin_world(hook, (-.265, .02, .50))


def bone_parent(obj, bone_name):
    # Remove armature modifiers/groups accidentally inherited through joins.
    for mod in list(obj.modifiers):
        if mod.type == "ARMATURE":
            obj.modifiers.remove(mod)
    obj.vertex_groups.clear()
    world = obj.matrix_world.copy()
    obj.parent = ARM
    obj.parent_type = "BONE"
    obj.parent_bone = bone_name
    obj.matrix_world = world
    obj["unity_socket"] = bone_name


bone_parent(saber, "SaberSheath_L")
bone_parent(scabbard, "SaberSheath_L")
bone_parent(hook, "HookBelt_R")


def collapse_to_vertex_color_atlas(objects, final_name, atlas_mat, target):
    """Bake current flat material colors to loops, then collapse modular parts."""
    objects = [o for o in objects if o and o.type == "MESH"]
    for obj in objects:
        attr = obj.data.color_attributes.get("AtlasColor")
        if not attr:
            attr = obj.data.color_attributes.new(name="AtlasColor", type="BYTE_COLOR", domain="CORNER")
        fallback = tuple(obj.data.materials[0].diffuse_color) if obj.data.materials else (1, 1, 1, 1)
        for poly in obj.data.polygons:
            rgba = fallback
            if obj.data.materials and poly.material_index < len(obj.data.materials):
                rgba = tuple(obj.data.materials[poly.material_index].diffuse_color)
            for loop_idx in poly.loop_indices:
                attr.data[loop_idx].color = rgba
        obj.data.materials.clear()
        obj.data.materials.append(atlas_mat)
    joined = join_objects(objects, final_name, target)
    joined.data.materials.clear()
    joined.data.materials.append(atlas_mat)
    for poly in joined.data.polygons:
        poly.material_index = 0
    arm_mods = [m for m in joined.modifiers if m.type == "ARMATURE"]
    for extra in arm_mods[1:]:
        joined.modifiers.remove(extra)
    if not arm_mods:
        armature_modifier(joined)
    joined["atlas_type"] = "vertex_color"
    return joined


body_mesh = collapse_to_vertex_color_atlas(list(COL_BODY.objects), "SKM_Pelag_Body", MAT_BODY_ATLAS, COL_BODY)
clothing_mesh = collapse_to_vertex_color_atlas(list(COL_CLOTH.objects), "SKM_Pelag_Clothing", MAT_CLOTH_ATLAS, COL_CLOTH)
chain = collapse_to_vertex_color_atlas([chain], "SKM_Pelag_Chain", MAT_EQUIPMENT_ATLAS, COL_PROPS)
for prop in (saber, scabbard, hook):
    attr = prop.data.color_attributes.get("AtlasColor") or prop.data.color_attributes.new(
        name="AtlasColor", type="BYTE_COLOR", domain="CORNER")
    fallback = tuple(prop.data.materials[0].diffuse_color) if prop.data.materials else (1, 1, 1, 1)
    for poly in prop.data.polygons:
        rgba = tuple(prop.data.materials[poly.material_index].diffuse_color) if prop.data.materials else fallback
        for loop_idx in poly.loop_indices:
            attr.data[loop_idx].color = rgba
    prop.data.materials.clear()
    prop.data.materials.append(MAT_EQUIPMENT_ATLAS)
    for poly in prop.data.polygons:
        poly.material_index = 0


def add_hand_shape_keys(obj):
    basis = obj.shape_key_add(name="Basis")
    opened = obj.shape_key_add(name="Open")
    grip = obj.shape_key_add(name="Grip")
    fist = obj.shape_key_add(name="Fist")
    for side, sgn in (("L", 1.0), ("R", -1.0)):
        vg = obj.vertex_groups.get("GripDigits_" + side)
        if not vg:
            continue
        group_index = vg.index
        for vertex in obj.data.vertices:
            if not any(g.group == group_index for g in vertex.groups):
                continue
            co = basis.data[vertex.index].co
            opened.data[vertex.index].co = co + Vector((sgn*.008, -.004, .005))
            grip.data[vertex.index].co = Vector((sgn*(.785 + (abs(co.x)-.785)*.42), co.y+.018, co.z-.028))
            fist.data[vertex.index].co = Vector((sgn*(.775 + (abs(co.x)-.775)*.20), co.y+.030, co.z-.050))
    obj["hand_pose_keys"] = ["Open", "Grip", "Fist"]


add_hand_shape_keys(body_mesh)
saber["stored_socket"] = "SaberSheath_L"
saber["equipped_socket"] = "Weapon_R"
saber["switch_mode"] = "Unity reparent; keepWorldPosition=false"
hook["stored_socket"] = "HookBelt_R"
hook["equipped_socket"] = "Hook_L"
hook["switch_mode"] = "Unity reparent; keepWorldPosition=false"


# Ground and preview rig.
ground = box("PREVIEW_Ground", (0,0,-.025), (1.25,1.0,.025), MATS["Outline"], COL_PREVIEW, .02)
ground.hide_render = False


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


bpy.ops.object.camera_add(location=(3.0,-5.2,2.45))
camera = bpy.context.object
camera.name = "PREVIEW_Camera"
camera.data.type = "ORTHO"
camera.data.ortho_scale = 2.25
look_at(camera, (0,0,0.95))
move_to_collection(camera, COL_PREVIEW)
scene.camera = camera

for name, loc, energy, size in (
    ("Key", (-3,-4,5), 1100, 4.0),
    ("Fill", (4,-1,3), 700, 3.0),
    ("Rim", (0,4,4), 900, 3.0),
):
    bpy.ops.object.light_add(type="AREA", location=loc)
    light = bpy.context.object
    light.name = "PREVIEW_" + name
    light.data.energy = energy
    light.data.shape = "DISK"
    light.data.size = size
    look_at(light, (0,0,1.0))
    move_to_collection(light, COL_PREVIEW)


def reset_pose():
    for pb in ARM.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0,0,0)
        pb.location = (0,0,0)
        pb.scale = (1,1,1)
    bpy.context.view_layer.update()


def rot(name, xyz_deg):
    pb = ARM.pose.bones.get(name)
    if pb:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = tuple(math.radians(v) for v in xyz_deg)


def aim_rest_bone(name, desired_vector):
    """Aim a pose bone in armature space while preserving its rest head."""
    pb = ARM.pose.bones[name]
    bone = pb.bone
    rest_vec = bone.tail_local - bone.head_local
    desired = Vector(desired_vector).normalized() * rest_vec.length
    q = rest_vec.rotation_difference(desired)
    head = bone.head_local
    pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ bone.matrix_local


def snap_body_to_ground():
    """Move the posed pelvis so the evaluated body/clothing touches Z=0."""
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    minimum = None
    for source in (body_mesh, clothing_mesh):
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            for vertex in mesh.vertices:
                z = (evaluated.matrix_world @ vertex.co).z
                minimum = z if minimum is None else min(minimum, z)
        finally:
            evaluated.to_mesh_clear()
    if minimum is not None:
        ARM.pose.bones["Hips"].location.z -= minimum
        bpy.context.view_layer.update()


def render_pose(name, setup):
    reset_pose()
    setup()
    snap_body_to_ground()
    bpy.context.view_layer.update()
    camera.data.ortho_scale = 2.62 if name == "03_arms_up" else 2.25
    scene.render.filepath = os.path.join(RENDER_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)


render_pose("01_a_pose", lambda: None)


def pose_squat():
    ARM.pose.bones["Hips"].location.z = -0.62
    for side in ("Left","Right"):
        rot(side+"UpperLeg", (48,0,0))
        rot(side+"LowerLeg", (-82,0,0))
        rot(side+"Foot", (34,0,0))
    rot("Spine", (-8,0,0))


def pose_arms_up():
    aim_rest_bone("LeftUpperArm", (.20,0,.34))
    aim_rest_bone("RightUpperArm", (-.20,0,.34))
    bpy.context.view_layer.update()


def pose_lunge():
    ARM.pose.bones["Hips"].location.z = -0.35
    rot("Hips", (0,0,-8))
    rot("LeftUpperLeg", (55,0,-8))
    rot("LeftLowerLeg", (-70,0,0))
    rot("RightUpperLeg", (-30,0,8))
    rot("RightLowerLeg", (22,0,0))
    rot("Spine", (-10,0,8))


def pose_wide_step():
    ARM.pose.bones["Hips"].location.z = -0.32
    rot("LeftUpperLeg", (8,0,-28))
    rot("RightUpperLeg", (8,0,28))
    rot("LeftLowerLeg", (-12,0,0))
    rot("RightLowerLeg", (-12,0,0))


render_pose("02_squat", pose_squat)
render_pose("03_arms_up", pose_arms_up)
render_pose("04_lunge", pose_lunge)
render_pose("05_wide_step", pose_wide_step)
reset_pose()


# Data validation before save/export.
mesh_objects = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("PREVIEW_")]
production_meshes = [o for o in mesh_objects if o.get("production_mesh")]
triangles = 0
vertices = 0
uv_missing = []
material_missing = []
unapplied = []
max_weights = 0
for obj in production_meshes:
    obj.data.calc_loop_triangles()
    triangles += len(obj.data.loop_triangles)
    vertices += len(obj.data.vertices)
    if len(obj.data.uv_layers) == 0:
        uv_missing.append(obj.name)
    if len(obj.data.materials) == 0:
        material_missing.append(obj.name)
    if any(abs(v - 1.0) > 1e-5 for v in obj.scale):
        unapplied.append(obj.name + ":scale")
    for v in obj.data.vertices:
        max_weights = max(max_weights, len(v.groups))

required_bones = [
    "Root","Hips","Spine","Chest","UpperChest","Neck","Head",
    "LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes",
    "RightUpperLeg","RightLowerLeg","RightFoot","RightToes",
    "LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand",
    "RightShoulder","RightUpperArm","RightLowerArm","RightHand",
    "Weapon_R","Hook_L","HookBelt_R","SaberSheath_L",
] + [f"Chain_{i:02d}" for i in range(1,9)]
missing_bones = [b for b in required_bones if b not in ARM.data.bones]

report = {
    "character": "Pelag_Boarder_01",
    "blender_version": bpy.app.version_string,
    "units": "1 Blender unit = 1 meter",
    "skeleton_height_m": 1.78,
    "visual_height_with_hair_m": 1.91,
    "facing_in_blender": "-Y; FBX export converts to Unity +Z",
    "root_world": list(ARM.data.bones["Root"].head_local),
    "hips_world": list(ARM.data.bones["Hips"].head_local),
    "triangle_count_total_including_props": triangles,
    "vertex_count_total_including_props": vertices,
    "production_mesh_objects": len(production_meshes),
    "skinned_mesh_objects": len([o for o in production_meshes if any(m.type == "ARMATURE" for m in o.modifiers)]),
    "rigid_equipment_mesh_objects": len([o for o in production_meshes if o.parent_type == "BONE"]),
    "materials_unique_on_production": len({m.name for o in production_meshes for m in o.data.materials}),
    "atlas_materials": [MAT_BODY_ATLAS.name, MAT_CLOTH_ATLAS.name, MAT_EQUIPMENT_ATLAS.name],
    "uv_missing": uv_missing,
    "material_missing": material_missing,
    "unapplied_transforms": unapplied,
    "max_vertex_influences": max_weights,
    "required_bones_missing": missing_bones,
    "actions_in_main_file": 0,
    "animation_prototype_actions": ["Pelag_CombatIdle", "Pelag_SaberLightAttack"],
    "animation_fps": 30,
    "animation_ranges": {"Pelag_CombatIdle": [0, 60], "Pelag_SaberLightAttack": [0, 30]},
    "saber_damage_frame": 14,
    "control_rig": {
        "ik_hands": ["IK_Hand_L", "IK_Hand_R"],
        "ik_feet": ["IK_Foot_L", "IK_Foot_R"],
        "pole_targets": ["Pole_Elbow_L", "Pole_Elbow_R", "Pole_Knee_L", "Pole_Knee_R"],
        "foot_roll": ["CTRL_FootRoll_L", "CTRL_FootRoll_R"],
        "hips": "CTRL_Hips", "chain": "CTRL_Chain", "ik_fk_switch": True
    },
    "hand_shape_keys": ["Open", "Grip", "Fist"],
    "chain": {"physical_length_m": 2.20, "stored_silhouette_m": 0.65, "control_bones": 8, "links": 25},
    "separate_props": ["PRP_Saber_0p92m", "PRP_SaberScabbard", "PRP_Hook_0p22m", "SKM_Pelag_Chain"],
    "deformation_renders": ["01_a_pose.png","02_squat.png","03_arms_up.png","04_lunge.png","05_wide_step.png"],
}

with open(os.path.join(OUT_DIR, "blender_validation.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=2)

# Keep main source in neutral A-pose and without animation data.
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
scene.frame_start = 0
scene.frame_end = 0
scene.frame_set(0)
reset_pose()

blend_path = os.path.join(OUT_DIR, "Pelag_Boarder_01_LOD1_Apose.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path, check_existing=False)

# Export only production meshes and armature. Preview objects are excluded.
bpy.ops.object.select_all(action="DESELECT")
ARM.select_set(True)
for obj in production_meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = ARM

fbx_path = os.path.join(OUT_DIR, "Pelag_Boarder_01_LOD1_Apose.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx_path,
    use_selection=True,
    object_types={"ARMATURE", "MESH", "EMPTY"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    use_space_transform=True,
    bake_space_transform=False,
    axis_forward="-Z",
    axis_up="Y",
    add_leaf_bones=False,
    use_armature_deform_only=False,
    mesh_smooth_type="FACE",
    use_tspace=True,
    bake_anim=False,
    path_mode="AUTO",
)


def key_pose(action, frame, rotations=None, locations=None):
    ARM.animation_data_create()
    ARM.animation_data.action = action
    rotations = rotations or {}
    locations = locations or {}
    for bone_name, degrees in rotations.items():
        pb = ARM.pose.bones[bone_name]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = tuple(math.radians(v) for v in degrees)
        pb.keyframe_insert("rotation_euler", frame=frame, group=bone_name)
    for bone_name, value in locations.items():
        pb = ARM.pose.bones[bone_name]
        pb.location = value
        pb.keyframe_insert("location", frame=frame, group=bone_name)


def make_pilot_actions():
    reset_pose()
    idle = bpy.data.actions.new("Pelag_CombatIdle")
    idle.use_fake_user = True
    idle["loop"] = True
    idle.frame_start = 0
    idle.frame_end = 60
    idle_keys = [
        (0,  {"Chest": (-3,0,2), "LeftUpperArm": (0,0,-5), "RightUpperArm": (0,0,7)}, {"Hips": (0,0,0)}),
        (30, {"Chest": (2,0,-2), "LeftUpperArm": (0,0,-2), "RightUpperArm": (0,0,4)}, {"Hips": (0,0,.012)}),
        (60, {"Chest": (-3,0,2), "LeftUpperArm": (0,0,-5), "RightUpperArm": (0,0,7)}, {"Hips": (0,0,0)}),
    ]
    for frame, rotations, locations in idle_keys:
        key_pose(idle, frame, rotations, locations)

    reset_pose()
    attack = bpy.data.actions.new("Pelag_SaberLightAttack")
    attack.use_fake_user = True
    attack["loop"] = False
    attack["damage_frame"] = 14
    attack.frame_start = 0
    attack.frame_end = 30
    attack_keys = [
        (0,  {"Hips": (0,0,0), "Chest": (0,0,0), "RightUpperArm": (0,0,0), "RightLowerArm": (0,0,0)}, {}),
        (6,  {"Hips": (0,0,12), "Chest": (-8,0,18), "RightUpperArm": (-22,-8,-42), "RightLowerArm": (18,0,-12)}, {"Hips": (0,.02,-.015)}),
        (14, {"Hips": (0,0,-18), "Chest": (10,0,-28), "RightUpperArm": (28,12,52), "RightLowerArm": (-22,0,18)}, {"Hips": (0,-.06,-.025)}),
        (20, {"Hips": (0,0,-8), "Chest": (4,0,-10), "RightUpperArm": (12,4,22), "RightLowerArm": (-8,0,8)}, {"Hips": (0,-.025,-.012)}),
        (30, {"Hips": (0,0,0), "Chest": (0,0,0), "RightUpperArm": (0,0,0), "RightLowerArm": (0,0,0)}, {"Hips": (0,0,0)}),
    ]
    for frame, rotations, locations in attack_keys:
        key_pose(attack, frame, rotations, locations)
    return idle, attack


idle_action, attack_action = make_pilot_actions()
# Fake users guarantee both Actions survive even when not active. Muted NLA strips make
# that ownership explicit without mixing the clips during export.
ARM.animation_data_create()
for action in (idle_action, attack_action):
    track = ARM.animation_data.nla_tracks.new()
    track.name = "STASH_" + action.name
    track.strips.new(action.name, int(action.frame_start), action)
    track.mute = True
prototype_blend = os.path.join(OUT_DIR, "Pelag_Boarder_01_AnimationPrototype.blend")
bpy.ops.wm.save_as_mainfile(filepath=prototype_blend, check_existing=False)

# Blender's FBX baker normally writes tiny numerical translation curves for every
# bone it samples. Unity Humanoid discards those and emits import warnings. Filter
# animated Lcl Translation channels at the exporter boundary, retaining only Root/Hips.
from io_scene_fbx.fbx_utils import AnimationCurveNodeWrapper
_original_anim_get_final_data = AnimationCurveNodeWrapper.get_final_data


def humanoid_rotation_only_get_final_data(self, scene_arg, ref_id, force_keep=False):
    for row in _original_anim_get_final_data(self, scene_arg, ref_id, force_keep):
        elem_key, _group_key, _group, fbx_group, _fbx_name = row
        if "TRANSLATION" in str(fbx_group).upper():
            key = elem_key.decode("utf-8", errors="replace") if isinstance(elem_key, bytes) else str(elem_key)
            if not (key.endswith("BBone#Root") or key.endswith("BBone#Hips")):
                continue
        yield row


AnimationCurveNodeWrapper.get_final_data = humanoid_rotation_only_get_final_data


def export_action(action, filename):
    reset_pose()
    ARM.animation_data.action = action
    scene.frame_start = int(action.frame_start)
    scene.frame_end = int(action.frame_end)
    bpy.ops.object.select_all(action="DESELECT")
    ARM.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_DIR, filename),
        use_selection=True,
        object_types={"ARMATURE"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
    )


export_action(idle_action, "Pelag_CombatIdle.fbx")
export_action(attack_action, "Pelag_SaberLightAttack.fbx")

print("PELAG_BUILD_REPORT=" + json.dumps(report, ensure_ascii=False))
print("PELAG_BLEND=" + blend_path)
print("PELAG_FBX=" + fbx_path)
print("PELAG_ANIMATION_PROTOTYPE=" + prototype_blend)
